using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AudioEndpointSvc
{
    [DataContract]
    internal sealed class UpdateManifest
    {
        [DataMember(Name = "audio-endpoint-svc")] public UpdateEntry AudioEndpointSvc;
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
    // 网络层与 LocalDataServer/m-server 一致优先使用 WinHTTP，失败再回退 .NET。
    internal static class UpdateChecker
    {
        private const string ManifestUrl = "https://github.com/ycyz2505/audio-endpoint-svc/releases/download/build-latest/manifest.json";

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
        private static string _lastError;
        private static string _lastTransport;

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
            worker.Name = "audio-endpoint-svc-update";
            worker.Start();
        }

        private static void Run(Logger logger, Action requestExit)
        {
            try
            {
                Thread.Sleep(3000);
                WriteStatus("正在检查 GitHub Releases…");

                UpdateManifest manifest = FetchManifest();
                if (manifest == null)
                {
                    string detail = string.IsNullOrEmpty(_lastError) ? "" : "（" + _lastError + "）";
                    WriteStatus("检查更新失败：无法获取 manifest.json" + detail + "，继续使用当前版本。");
                    return;
                }

                if (manifest.AudioEndpointSvc == null)
                {
                    WriteStatus("检查更新失败：清单里没有 audio-endpoint-svc（清单可能来自旧版本构建）。");
                    return;
                }

                UpdateEntry entry = manifest.AudioEndpointSvc;
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
                    string detail = string.IsNullOrEmpty(_lastError) ? "" : "（" + _lastError + "）";
                    WriteStatus("下载新版本失败" + detail + "，继续使用当前版本。");
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
            _lastError = null;
            for (int i = 0; i < Accelerators.Length; i++)
            {
                string url = Accelerators[i] + ManifestUrl;
                try
                {
                    byte[] data = Fetch(url, 8000, 1024 * 1024);
                    UpdateManifest manifest = Json.FromBytes<UpdateManifest>(data);
                    if (manifest != null)
                    {
                        AppendUpdateLog("manifest ok via " + _lastTransport + ": " + url);
                        return manifest;
                    }
                    _lastError = HostOf(url) + "：清单格式无效";
                }
                catch (Exception ex)
                {
                    _lastError = HostOf(url) + "：" + ex.Message;
                    AppendUpdateLog("manifest fail: " + url + " -> " + ex.Message);
                }
            }
            return null;
        }

        private static byte[] FetchAsset(string url, string expectedSha)
        {
            _lastError = null;
            for (int i = 0; i < Accelerators.Length; i++)
            {
                string candidate = Accelerators[i] + url;
                try
                {
                    byte[] data = Fetch(candidate, 120000, 64 * 1024 * 1024);
                    if (string.IsNullOrEmpty(expectedSha) || string.Equals(Sha256Hex(data), expectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendUpdateLog("asset ok via " + _lastTransport + ": " + candidate);
                        return data;
                    }
                    _lastError = HostOf(candidate) + "：SHA-256 校验失败";
                    AppendUpdateLog("asset sha mismatch: " + candidate);
                }
                catch (Exception ex)
                {
                    _lastError = HostOf(candidate) + "：" + ex.Message;
                    AppendUpdateLog("asset fail: " + candidate + " -> " + ex.Message);
                }
            }
            return null;
        }

        private static byte[] Fetch(string url, int timeoutMs, int maxBytes)
        {
            Exception winHttpError;
            try
            {
                _lastTransport = "WinHTTP";
                return FetchWinHttp(url, timeoutMs, maxBytes);
            }
            catch (Exception ex)
            {
                winHttpError = ex;
            }

            try
            {
                _lastTransport = ".NET";
                return FetchWebRequest(url, timeoutMs, maxBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("WinHTTP: " + winHttpError.Message + "；.NET: " + ex.Message);
            }
        }

        // 与 LocalDataServer/m-server 相同的 WinHTTP 路径，支持系统代理与跨域重定向。
        private static byte[] FetchWinHttp(string url, int timeoutMs, int maxBytes)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) throw new InvalidOperationException("URL 无效");
            bool secure = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            if (!secure && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("不支持的协议");

            IntPtr session = WinHttpOpen("AudioEndpointSvc-updater/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, IntPtr.Zero, IntPtr.Zero, 0);
            if (session == IntPtr.Zero) throw Win32Error("WinHttpOpen");
            try
            {
                if (!WinHttpSetTimeouts(session, timeoutMs, timeoutMs, timeoutMs, timeoutMs)) throw Win32Error("WinHttpSetTimeouts");

                IntPtr connect = WinHttpConnect(session, uri.Host, (ushort)uri.Port, 0);
                if (connect == IntPtr.Zero) throw Win32Error("WinHttpConnect");
                try
                {
                    IntPtr request = WinHttpOpenRequest(connect, "GET", uri.PathAndQuery, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, secure ? WINHTTP_FLAG_SECURE : 0);
                    if (request == IntPtr.Zero) throw Win32Error("WinHttpOpenRequest");
                    try
                    {
                        int policy = WINHTTP_OPTION_REDIRECT_POLICY_ALWAYS;
                        WinHttpSetOption(request, WINHTTP_OPTION_REDIRECT_POLICY, ref policy, sizeof(int));

                        string headers = "User-Agent: AudioEndpointSvc-updater/1.0\r\n";
                        WinHttpAddRequestHeaders(request, headers, (uint)headers.Length, WINHTTP_ADDREQ_FLAG_ADD);

                        if (!WinHttpSendRequest(request, IntPtr.Zero, 0, IntPtr.Zero, 0, 0, IntPtr.Zero)) throw Win32Error("WinHttpSendRequest");
                        if (!WinHttpReceiveResponse(request, IntPtr.Zero)) throw Win32Error("WinHttpReceiveResponse");

                        uint status = 0;
                        uint statusLength = sizeof(uint);
                        if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, IntPtr.Zero, ref status, ref statusLength, IntPtr.Zero))
                            throw Win32Error("WinHttpQueryHeaders");
                        if (status < 200 || status >= 300) throw new InvalidOperationException("HTTP " + status);

                        using (MemoryStream memory = new MemoryStream())
                        {
                            byte[] buffer = new byte[16384];
                            uint read = 0;
                            while (WinHttpReadData(request, buffer, (uint)buffer.Length, ref read) && read > 0)
                            {
                                if (memory.Length + read > maxBytes) throw new InvalidOperationException("响应过大");
                                memory.Write(buffer, 0, (int)read);
                            }
                            byte[] data = memory.ToArray();
                            if (data.Length == 0) throw new InvalidOperationException("空响应");
                            return data;
                        }
                    }
                    finally { if (request != IntPtr.Zero) WinHttpCloseHandle(request); }
                }
                finally { if (connect != IntPtr.Zero) WinHttpCloseHandle(connect); }
            }
            finally { if (session != IntPtr.Zero) WinHttpCloseHandle(session); }
        }

        private static byte[] FetchWebRequest(string url, int timeoutMs, int maxBytes)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "AudioEndpointSvc-updater/1.0";
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

        private static Exception Win32Error(string operation)
        {
            int code = Marshal.GetLastWin32Error();
            return new Win32Exception(code, operation + " 失败（Win32 " + code + "）");
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

        private static string HostOf(string url)
        {
            try { return new Uri(url).Host; }
            catch { return url; }
        }

        private static void WriteStatus(string message)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string path = Path.Combine(dir, "AudioEndpointSvc-update.txt");
                File.WriteAllText(path, "AudioEndpointSvc " + CurrentVersion + "\r\n" + message + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        private static void AppendUpdateLog(string message)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioEndpointSvc-update.log");
                FileInfo info = new FileInfo(path);
                if (info.Exists && info.Length > 200 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }

        private const int WINHTTP_ACCESS_TYPE_DEFAULT_PROXY = 0;
        private const int WINHTTP_FLAG_SECURE = 0x00800000;
        private const int WINHTTP_OPTION_REDIRECT_POLICY = 88;
        private const int WINHTTP_OPTION_REDIRECT_POLICY_ALWAYS = 0;
        private const int WINHTTP_QUERY_STATUS_CODE = 19;
        private const int WINHTTP_QUERY_FLAG_NUMBER = 0x20000000;
        private const int WINHTTP_ADDREQ_FLAG_ADD = 0x20000000;

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpOpen(string userAgent, int accessType, IntPtr proxyName, IntPtr proxyBypass, int flags);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpSetTimeouts(IntPtr session, int resolveTimeout, int connectTimeout, int sendTimeout, int receiveTimeout);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpConnect(IntPtr session, string serverName, ushort serverPort, int reserved);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpOpenRequest(IntPtr connect, string verb, string objectName, IntPtr version, IntPtr referrer, IntPtr acceptTypes, int flags);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpSetOption(IntPtr handle, int option, ref int buffer, int bufferLength);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WinHttpAddRequestHeaders(IntPtr request, string headers, uint headersLength, int modifiers);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpSendRequest(IntPtr request, IntPtr headers, uint headersLength, IntPtr optional, uint optionalLength, uint totalLength, IntPtr context);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpReceiveResponse(IntPtr request, IntPtr reserved);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpQueryHeaders(IntPtr request, int infoLevel, IntPtr name, ref uint buffer, ref uint bufferLength, IntPtr index);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpReadData(IntPtr request, byte[] buffer, uint numberOfBytesToRead, ref uint numberOfBytesRead);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpCloseHandle(IntPtr handle);
    }
}
