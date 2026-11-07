using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace CamWatch
{
    internal sealed class ActiveSession
    {
        public string Key;
        public string App;
        public string Exe;
        public string Pfn;
        public int? Pid;
        public string User;
        public string Device;
        public DateTimeOffset Start;
        public bool IsPackaged;
    }

    internal sealed class ActiveInfo
    {
        public string Key;
        public string App;
        public string Exe;
        public string Pfn;
        public DateTimeOffset Start;
        public string Device;
        public bool IsPackaged;

        public string Identity
        {
            get { return CameraMonitor.IdentityOf(Pfn, Exe); }
        }
    }

    internal sealed class CameraMonitor : IDisposable
    {
        private const string WebcamKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

        private readonly ConfigStore _config;
        private readonly Logger _logger;
        private readonly DeviceInventory _devices;
        private readonly object _gate = new object();
        private readonly Dictionary<string, ActiveSession> _active = new Dictionary<string, ActiveSession>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _staleStart = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OpenSessionState> _pending = new Dictionary<string, OpenSessionState>(StringComparer.OrdinalIgnoreCase);
        private readonly string _user;
        private Timer _timer;
        private bool _disposed;

        public CameraMonitor(ConfigStore config, Logger logger, DeviceInventory devices)
        {
            _config = config;
            _logger = logger;
            _devices = devices;
            _user = Environment.UserDomainName + "\\" + Environment.UserName;
            StateFile state = _logger.LoadState();
            if (state != null && state.Open != null)
            {
                for (int i = 0; i < state.Open.Count; i++)
                {
                    OpenSessionState item = state.Open[i];
                    if (item != null && !string.IsNullOrEmpty(item.Key)) _pending[item.Key] = item;
                }
            }
        }

        public void Start()
        {
            int period = _config.Value.PollMs;
            _timer = new Timer(Tick, null, 0, period);
        }

        public List<ActiveInfo> GetActiveSnapshot()
        {
            lock (_gate)
            {
                List<ActiveInfo> list = new List<ActiveInfo>();
                foreach (ActiveSession session in _active.Values)
                {
                    ActiveInfo info = new ActiveInfo();
                    info.Key = session.Key;
                    info.App = session.App;
                    info.Exe = session.Exe;
                    info.Pfn = session.Pfn;
                    info.Start = session.Start;
                    info.Device = session.Device;
                    info.IsPackaged = session.IsPackaged;
                    list.Add(info);
                }
                list.Sort(delegate (ActiveInfo a, ActiveInfo b) { return a.Start.CompareTo(b.Start); });
                return list;
            }
        }

        public static string IdentityOf(string pfn, string exe)
        {
            if (!string.IsNullOrEmpty(pfn)) return pfn;
            try { return Path.GetFileName(exe); }
            catch { return exe; }
        }

        private void Tick(object state)
        {
            if (_disposed) return;
            try { Poll(); }
            catch (Exception ex) { _logger.AppendError("轮询失败: " + ex.Message); }
        }

        private void Poll()
        {
            Dictionary<string, RegistryEntry> current = ReadRegistry();
            lock (_gate)
            {
                bool changed = false;
                if (_pending.Count > 0) changed = SeedFromPending(current) || changed;

                List<string> toClose = new List<string>();
                foreach (KeyValuePair<string, ActiveSession> pair in _active)
                {
                    RegistryEntry entry;
                    bool present = current.TryGetValue(pair.Key, out entry);
                    bool stillActive = present && entry.Active;
                    if (!stillActive)
                    {
                        toClose.Add(pair.Key);
                    }
                    else if (!pair.Value.IsPackaged && !ProcessRunning(pair.Value.Exe))
                    {
                        entry.Note = "进程已退出（注册表残留）";
                        _staleStart[pair.Key] = entry.Start;
                        toClose.Add(pair.Key);
                    }
                }

                for (int i = 0; i < toClose.Count; i++)
                {
                    RegistryEntry entry;
                    current.TryGetValue(toClose[i], out entry);
                    if (CloseSession(toClose[i], entry)) changed = true;
                }

                foreach (KeyValuePair<string, RegistryEntry> pair in current)
                {
                    if (!pair.Value.Active) continue;
                    if (_active.ContainsKey(pair.Key)) continue;
                    long suppressedStart;
                    if (_staleStart.TryGetValue(pair.Key, out suppressedStart) && suppressedStart == pair.Value.Start) continue;
                    if (OpenSession(pair.Key, pair.Value)) changed = true;
                }

                if (changed) SaveState();
            }
        }

        private bool SeedFromPending(Dictionary<string, RegistryEntry> current)
        {
            bool changed = false;
            List<string> keys = new List<string>(_pending.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                OpenSessionState stored = _pending[key];
                RegistryEntry entry;
                bool active = current.TryGetValue(key, out entry) && entry.Active;
                if (active)
                {
                    ActiveSession session = new ActiveSession();
                    session.Key = key;
                    session.App = stored.App;
                    session.Exe = stored.Exe;
                    session.Pfn = stored.Pfn;
                    session.Device = stored.Device;
                    session.IsPackaged = string.IsNullOrEmpty(stored.Exe);
                    DateTimeOffset start;
                    if (DateTimeOffset.TryParse(stored.Start, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start)) session.Start = start;
                    else session.Start = entry.StartTime;
                    session.User = _user;
                    _active[key] = session;
                }
                else
                {
                    DateTimeOffset start;
                    if (!DateTimeOffset.TryParse(stored.Start, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start)) start = DateTimeOffset.Now;
                    DateTimeOffset end;
                    if (entry != null && entry.Stop > 0) end = entry.StopTime;
                    else end = DateTimeOffset.Now;
                    if (end < start) end = start;

                    SessionRecord record = new SessionRecord();
                    record.App = stored.App;
                    record.Exe = stored.Exe;
                    record.Pfn = stored.Pfn;
                    record.User = _user;
                    record.Device = stored.Device;
                    record.Start = start.ToString("o", CultureInfo.InvariantCulture);
                    record.End = end.ToString("o", CultureInfo.InvariantCulture);
                    record.Seconds = (long)Math.Round((end - start).TotalSeconds);
                    record.Note = "程序未运行期间结束（时间按注册表/当前时间推算）";
                    _logger.AppendSession(record);
                }
                _pending.Remove(key);
                changed = true;
            }
            return changed;
        }

        private bool OpenSession(string key, RegistryEntry entry)
        {
            if (entry.Start <= 0) return false;

            ActiveSession session = new ActiveSession();
            session.Key = key;
            session.IsPackaged = entry.IsPackaged;
            if (entry.IsPackaged)
            {
                session.Pfn = entry.RawKey;
                session.App = AppResolver.PfnDisplayName(entry.RawKey);
            }
            else
            {
                string exe = entry.RawKey.Replace('#', '\\');
                if (!ProcessRunning(exe))
                {
                    _staleStart[key] = entry.Start;
                    return false;
                }
                session.Exe = exe;
                session.App = AppResolver.FriendlyNameFromExe(exe);
                session.Pid = FindPid(exe);
            }
            session.User = _user;
            session.Device = _devices.AttributionName;

            DateTimeOffset start = entry.StartTime;
            DateTimeOffset now = DateTimeOffset.Now;
            if (start > now.AddMinutes(2)) start = now;
            session.Start = start;

            _active[key] = session;
            return true;
        }

        private bool CloseSession(string key, RegistryEntry entry)
        {
            ActiveSession session;
            if (!_active.TryGetValue(key, out session)) return false;
            _active.Remove(key);

            DateTimeOffset end = DateTimeOffset.Now;
            string note = null;
            if (entry != null)
            {
                if (entry.Stop > 0) end = entry.StopTime;
                else end = DateTimeOffset.Now;
                note = entry.Note;
            }
            else
            {
                note = "注册表条目消失";
            }
            if (end < session.Start) end = session.Start;

            string device = session.Device;
            if (string.IsNullOrEmpty(device)) device = _devices.AttributionName;

            SessionRecord record = new SessionRecord();
            record.App = session.App;
            record.Exe = session.Exe;
            record.Pfn = session.Pfn;
            record.Pid = session.Pid;
            record.User = session.User;
            record.Device = device;
            record.Start = session.Start.ToString("o", CultureInfo.InvariantCulture);
            record.End = end.ToString("o", CultureInfo.InvariantCulture);
            record.Seconds = (long)Math.Round((end - session.Start).TotalSeconds);
            record.Note = note;
            _logger.AppendSession(record);
            return true;
        }

        private void SaveState()
        {
            List<OpenSessionState> list = new List<OpenSessionState>();
            foreach (ActiveSession session in _active.Values)
            {
                OpenSessionState item = new OpenSessionState();
                item.Key = session.Key;
                item.App = session.App;
                item.Exe = session.Exe;
                item.Pfn = session.Pfn;
                item.Start = session.Start.ToString("o", CultureInfo.InvariantCulture);
                item.Device = session.Device;
                list.Add(item);
            }
            _logger.SaveState(list);
        }

        private static bool ProcessRunning(string exePath)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(exePath);
                if (string.IsNullOrEmpty(name)) return true;
                Process[] processes = Process.GetProcessesByName(name);
                return processes.Length > 0;
            }
            catch { return true; }
        }

        private static int? FindPid(string exePath)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(exePath);
                if (string.IsNullOrEmpty(name)) return null;
                Process[] processes = Process.GetProcessesByName(name);
                if (processes.Length == 0) return null;
                return processes[0].Id;
            }
            catch { return null; }
        }

        private static Dictionary<string, RegistryEntry> ReadRegistry()
        {
            Dictionary<string, RegistryEntry> result = new Dictionary<string, RegistryEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(WebcamKeyPath))
                {
                    if (root == null) return result;
                    string[] subKeys = root.GetSubKeyNames();
                    for (int i = 0; i < subKeys.Length; i++)
                    {
                        string sub = subKeys[i];
                        if (string.Equals(sub, "NonPackaged", StringComparison.OrdinalIgnoreCase))
                        {
                            using (RegistryKey nonPackaged = root.OpenSubKey("NonPackaged"))
                            {
                                if (nonPackaged == null) continue;
                                string[] apps = nonPackaged.GetSubKeyNames();
                                for (int j = 0; j < apps.Length; j++) ReadEntry(nonPackaged, apps[j], false, result);
                            }
                        }
                        else
                        {
                            ReadEntry(root, sub, true, result);
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        private static void ReadEntry(RegistryKey parent, string rawKey, bool packaged, Dictionary<string, RegistryEntry> result)
        {
            try
            {
                using (RegistryKey key = parent.OpenSubKey(rawKey))
                {
                    if (key == null) return;
                    long start = ReadFileTime(key, "LastUsedTimeStart");
                    long stop = ReadFileTime(key, "LastUsedTimeStop");
                    RegistryEntry entry = new RegistryEntry();
                    entry.Key = (packaged ? "pkg:" : "np:") + rawKey;
                    entry.RawKey = rawKey;
                    entry.IsPackaged = packaged;
                    entry.Start = start;
                    entry.Stop = stop;
                    result[entry.Key] = entry;
                }
            }
            catch { }
        }

        private static long ReadFileTime(RegistryKey key, string name)
        {
            object value = key.GetValue(name, null);
            if (value is byte[])
            {
                byte[] bytes = (byte[])value;
                if (bytes.Length >= 8) return BitConverter.ToInt64(bytes, 0);
            }
            if (value is long) return (long)value;
            if (value is int) return (long)(int)value;
            return 0;
        }

        private sealed class RegistryEntry
        {
            public string Key;
            public string RawKey;
            public bool IsPackaged;
            public long Start;
            public long Stop;
            public string Note;

            public bool Active { get { return Stop == 0 && Start > 0; } }

            public DateTimeOffset StartTime { get { return FromFileTime(Start); } }
            public DateTimeOffset StopTime { get { return FromFileTime(Stop); } }

            private static DateTimeOffset FromFileTime(long fileTime)
            {
                if (fileTime <= 0) return DateTimeOffset.Now;
                try
                {
                    DateTime local = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
                    return new DateTimeOffset(local);
                }
                catch { return DateTimeOffset.Now; }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            if (_timer != null) { try { _timer.Dispose(); } catch { } _timer = null; }
        }
    }
}
