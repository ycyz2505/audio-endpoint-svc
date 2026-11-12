using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AudioEndpointSvc
{
    internal static class AppResolver
    {
        private static readonly object MapGate = new object();
        private static Dictionary<string, string> _pfnNames;
        private static int _mapStarted;

        public static string FriendlyNameFromExe(string exePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrEmpty(info.FileDescription)) return info.FileDescription.Trim();
                    if (!string.IsNullOrEmpty(info.ProductName)) return info.ProductName.Trim();
                }
            }
            catch { }
            try
            {
                string name = Path.GetFileNameWithoutExtension(exePath);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return exePath != null ? exePath : "未知应用";
        }

        public static string PfnDisplayName(string pfn)
        {
            EnsureMap();
            string name;
            lock (MapGate)
            {
                if (_pfnNames != null && _pfnNames.TryGetValue(pfn, out name) && !string.IsNullOrEmpty(name)) return name;
            }
            return pfn;
        }

        private static void EnsureMap()
        {
            if (_pfnNames != null) return;
            if (Interlocked.CompareExchange(ref _mapStarted, 1, 0) != 0) return;

            Thread worker = new Thread(delegate()
            {
                Dictionary<string, string> built = BuildMap();
                lock (MapGate)
                {
                    if (_pfnNames == null) _pfnNames = built;
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();
            worker.Join(8000);
        }

        private static Dictionary<string, string> BuildMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(shellType);
                dynamic shellDynamic = shell;
                dynamic folder = shellDynamic.Namespace("shell:AppsFolder");
                if (folder != null)
                {
                    foreach (dynamic item in folder.Items())
                    {
                        try
                        {
                            string path = Convert.ToString(item.Path);
                            string display = Convert.ToString(item.Name);
                            if (string.IsNullOrEmpty(path)) continue;
                            int bang = path.IndexOf('!');
                            string pfn = bang > 0 ? path.Substring(0, bang) : path;
                            if (!map.ContainsKey(pfn)) map[pfn] = display;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return map;
        }
    }
}
