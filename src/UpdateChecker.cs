using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CamWatch
{
    [DataContract]
    internal sealed class UpdateManifest
    {
        [DataMember(Name = "camwatch")] public UpdateEntry Camwatch;
    }

    [DataContract]
    internal sealed class UpdateEntry
    {
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "url")] public string Url;
        [DataMember(Name = "sha256")] public string Sha256;
    }

    // 自动更新：启动后在后台读取 GitHub Releases 的 build-latest 清单，
    // 只有自己的版本落后时才下载自己的 exe，校验 SHA-256 后由批处理替换并重启。
    internal static class UpdateChecker
    {
        private const string ManifestUrl = "https://github.com/ycyz2505/camwatch/releases/download/build-latest/manifest.json";

        // 国内访问 GitHub 常常不稳，按顺序尝试加速源，最后才直连。
        private static readonly string[] Accelerators = new string[]
        {
            "https://ghfast.top/",
            "https://gh-proxy.com/",
            "https://ghproxy.net/",
            ""
        };

        private static readonly object Gate = new object();
        private static bool _started;

        static UpdateChecker()
        {
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
            catch { }
        }

        public static string CurrentVersion
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? version.ToString() : "0.0.0.0";
            }
        }

        public static void Start(Logger logger, Action requestExit)
        {
            if (Environment.GetEnvironmentVariable("CLASS_NO_SELF_UPDATE") != null) return;
            lock (Gate)
            {
                if (_started) return;
                _started = true;
            }

            Thread worker = new Thread(delegate() { Run(logger, requestExit); });
            worker.IsBackground = true;
            worker.Name = "camwatch-update";
            worker.Start();
        }

        private static void Run(Logger logger, Action requestExit)
        {
            try
            {
                Thread.Sleep(3000);
                WriteStatus("正在检查 GitHub Releases…");

                UpdateManifest manifest = FetchManifest();
                if (manifest == null || manifest.Camwatch == null)
                {
                    WriteStatus("检查更新失败：无法获取 manifest.json，继续使用当前版本。");
                    return;
                }

                UpdateEntry entry = manifest.Camwatch;
                if (string.IsNullOrEmpty(entry.Version) || !VersionGreater(entry.Version, CurrentVersion))
                {
                    WriteStatus("已是最新版本（" + (string.IsNullOrEmpty(entry.Version) ? CurrentVersion : entry.Version) + "）。");
                    return;
                }

                if (string.IsNullOrEmpty(entry.Url))
                {
                    WriteStatus("发现新版本 " + entry.Version + "，但清单缺少下载地址。");
                    return;
                }

                WriteStatus("发现新版本 " + entry.Version + "，正在下载…");
                byte[] data = FetchAsset(entry.Url, entry.Sha256);
                if (data == null)
                {
                    WriteStatus("下载新版本失败，继续使用当前版本。");
                    return;
                }

                if (!Apply(data))
                {
                    WriteStatus("写入新版本失败，继续使用当前版本。");
                    return;
                }

                WriteStatus("新版本 " + entry.Version + " 已下载，正在重启…");
                requestExit();
                Thread.Sleep(5000);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                try { logger.AppendError("自动更新失败: " + ex.Message); }
                catch { }
                WriteStatus("自动更新失败：" + ex.Message);
            }
        }

        private static UpdateManifest FetchManifest()
        {
            for (int i = 0; i < Accelerators.Length; i++)
            {
                try
                {
                    byte[] data = Fetch(Accelerators[i] + ManifestUrl, 8000, 1024 * 1024);
                    UpdateManifest manifest = Json.FromBytes<UpdateManifest>(data);
                    if (manifest != null && manifest.Camwatch != null && !string.IsNullOrEmpty(manifest.Camwatch.Version)) return manifest;
                }
                catch { }
            }
            return null;
        }

        private static byte[] FetchAsset(string url, string expectedSha)
        {
            for (int i = 0; i < Accelerators.Length; i++)
            {
                try
                {
                    byte[] data = Fetch(Accelerators[i] + url, 120000, 64 * 1024 * 1024);
                    if (string.IsNullOrEmpty(expectedSha) || string.Equals(Sha256Hex(data), expectedSha, StringComparison.OrdinalIgnoreCase)) return data;
                }
                catch { }
            }
            return null;
        }

        private static byte[] Fetch(string url, int timeoutMs, int maxBytes)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "camwatch-updater/1.0";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                int status = (int)response.StatusCode;
                if (status < 200 || status >= 300) throw new InvalidOperationException("HTTP " + status);

                using (Stream stream = response.GetResponseStream())
                using (MemoryStream memory = new MemoryStream())
                {
                    byte[] buffer = new byte[16384];
                    int read;
                    while (stream != null && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (memory.Length + read > maxBytes) throw new InvalidOperationException("响应过大");
                        memory.Write(buffer, 0, read);
                    }
                    byte[] data = memory.ToArray();
                    if (data.Length == 0) throw new InvalidOperationException("空响应");
                    return data;
                }
            }
        }

        private static bool Apply(byte[] data)
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(exePath)) return false;

            string exeDir = Path.GetDirectoryName(exePath);
            string exeName = Path.GetFileName(exePath);
            if (string.IsNullOrEmpty(exeDir) || string.IsNullOrEmpty(exeName)) return false;

            string baseName = Path.GetFileNameWithoutExtension(exeName);
            string newName = baseName + "-new.exe";
            string newPath = Path.Combine(exeDir, newName);

            try
            {
                File.WriteAllBytes(newPath, data);

                string batName = "update-" + Process.GetCurrentProcess().Id + ".bat";
                string batPath = Path.Combine(exeDir, batName);
                string bat = "@echo off\r\n"
                    + "setlocal\r\n"
                    + "set n=0\r\n"
                    + ":retry\r\n"
                    + "set /a n+=1\r\n"
                    + "if %n% gtr 60 exit /b 1\r\n"
                    + "ping -n 2 127.0.0.1 >nul\r\n"
                    + "move /y \"%~dp0" + newName + "\" \"%~dp0" + exeName + "\" >nul 2>nul\r\n"
                    + "if errorlevel 1 goto retry\r\n"
                    + "start \"\" \"%~dp0" + exeName + "\"\r\n"
                    + "del \"%~f0\"\r\n";

                File.WriteAllText(batPath, bat, new UTF8Encoding(false));

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/C \"" + batPath + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = exeDir;
                Process.Start(psi);
                return true;
            }
            catch
            {
                try { File.Delete(newPath); }
                catch { }
                return false;
            }
        }

        private static bool VersionGreater(string a, string b)
        {
            int ia = 0;
            int ib = 0;
            for (int n = 0; n < 8; n++)
            {
                long va = NextVersionPart(a, ref ia);
                long vb = NextVersionPart(b, ref ib);
                if (va != vb) return va > vb;
                if (ia >= a.Length && ib >= b.Length) break;
            }
            return false;
        }

        private static long NextVersionPart(string value, ref int index)
        {
            while (index < value.Length && (value[index] < '0' || value[index] > '9')) index++;

            long number = 0;
            while (index < value.Length && value[index] >= '0' && value[index] <= '9')
            {
                number = number * 10 + (value[index] - '0');
                index++;
            }
            return number;
        }

        private static string Sha256Hex(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static void WriteStatus(string message)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string path = Path.Combine(dir, "camwatch-update.txt");
                File.WriteAllText(path, "camwatch " + CurrentVersion + "\r\n" + message + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
