using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace AudioEndpointSvc
{
    [DataContract]
    internal sealed class UiConfig
    {
        [DataMember(Name = "displayMode")] public string DisplayMode;
        [DataMember(Name = "taskbarMode")] public string TaskbarMode;
        [DataMember(Name = "taskbarApps")] public List<string> TaskbarApps;
        [DataMember(Name = "retentionDays")] public int RetentionDays;
        [DataMember(Name = "overlayText")] public string OverlayText;
    }

    [DataContract]
    internal sealed class UiSession
    {
        [DataMember(Name = "startMs")] public long StartMs;
        [DataMember(Name = "endMs")] public long EndMs;
        [DataMember(Name = "app")] public string App;
        [DataMember(Name = "identity")] public string Identity;
        [DataMember(Name = "path")] public string Path;
        [DataMember(Name = "device")] public string Device;
        [DataMember(Name = "user")] public string User;
        [DataMember(Name = "seconds")] public long Seconds;
        [DataMember(Name = "note")] public string Note;
    }

    [DataContract]
    internal sealed class UiActive
    {
        [DataMember(Name = "app")] public string App;
        [DataMember(Name = "identity")] public string Identity;
        [DataMember(Name = "device")] public string Device;
        [DataMember(Name = "seconds")] public long Seconds;
    }

    [DataContract]
    internal sealed class UiLive
    {
        [DataMember(Name = "active")] public List<UiActive> Active;
    }

    [DataContract]
    internal sealed class UiBootstrap
    {
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "hasPassword")] public bool HasPassword;
        [DataMember(Name = "config")] public UiConfig Config;
        [DataMember(Name = "sessions")] public List<UiSession> Sessions;
        [DataMember(Name = "devices")] public List<string> Devices;
        [DataMember(Name = "active")] public List<UiActive> Active;
        [DataMember(Name = "user")] public string User;
        [DataMember(Name = "disguiseName")] public string DisguiseName;
    }

    [DataContract]
    internal sealed class UiFilter
    {
        [DataMember(Name = "fromMs")] public long FromMs;
        [DataMember(Name = "toMs")] public long ToMs;
        [DataMember(Name = "app")] public string App;
        [DataMember(Name = "device")] public string Device;
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public sealed class UiBridge
    {
        private readonly HtmlUiForm _form;
        private readonly ConfigStore _config;
        private readonly Logger _logger;
        private readonly CameraMonitor _monitor;
        private readonly DeviceInventory _devices;
        private readonly LogStore _store;
        private readonly Action _onExit;

        internal UiBridge(HtmlUiForm form, ConfigStore config, Logger logger, CameraMonitor monitor, DeviceInventory devices, Action onExit)
        {
            _form = form;
            _config = config;
            _logger = logger;
            _monitor = monitor;
            _devices = devices;
            _onExit = onExit;
            _store = new LogStore(logger);
        }

        public string GetBootstrap()
        {
            UiBootstrap data = new UiBootstrap();
            data.Version = UpdateChecker.CurrentVersion;
            data.HasPassword = _config.HasPassword;
            data.Config = SnapshotConfig();
            data.User = Environment.UserDomainName + "\\" + Environment.UserName;
            data.DisguiseName = _config.Value.DisguiseName;
            data.Sessions = LoadSessions();
            data.Devices = LoadDevices(data.Sessions);
            data.Active = ActiveSessions();
            return Json.ToLine(data);
        }

        public string GetLive()
        {
            UiLive live = new UiLive();
            live.Active = ActiveSessions();
            return Json.ToLine(live);
        }

        public string SaveSettings(string json)
        {
            UiConfig cfg;
            try { cfg = Json.FromLine<UiConfig>(json); }
            catch { return "设置格式无效"; }
            if (cfg == null) return "设置格式无效";

            string display = cfg.DisplayMode;
            if (display != "appbutton" && display != "overlay") display = "tray";
            string mode = cfg.TaskbarMode;
            if (mode != "only" && mode != "exclude") mode = "any";

            List<string> apps = new List<string>();
            if (cfg.TaskbarApps != null)
            {
                for (int i = 0; i < cfg.TaskbarApps.Count; i++)
                {
                    string app = cfg.TaskbarApps[i];
                    if (app == null) continue;
                    app = app.Trim();
                    if (app.Length > 0 && !apps.Contains(app)) apps.Add(app);
                }
            }

            int retention = cfg.RetentionDays;
            if (retention < 1 || retention > 3650) retention = 90;

            _config.Value.DisplayMode = display;
            _config.Value.TaskbarMode = mode;
            _config.Value.TaskbarApps = apps;
            _config.Value.RetentionDays = retention;
            if (cfg.OverlayText != null) _config.Value.OverlayText = cfg.OverlayText.Trim();
            _config.Save();
            _logger.CleanupOldLogs();
            return "ok";
        }

        public string VerifyPassword(string password)
        {
            if (!_config.HasPassword) return "setup";
            return _config.CheckPassword(password) ? "ok" : "bad";
        }

        public string SetPassword(string current, string next)
        {
            if (!_config.HasPassword)
            {
                if (string.IsNullOrEmpty(next) || next.Length < 4) return "新口令至少 4 位";
                _config.SetPassword(next);
                return "ok";
            }
            if (!_config.CheckPassword(current)) return "当前口令不正确";
            if (string.IsNullOrEmpty(next) || next.Length < 4) return "新口令至少 4 位";
            _config.SetPassword(next);
            return "ok";
        }

        public string ExportCsv(string filterJson)
        {
            UiFilter filter;
            try { filter = Json.FromLine<UiFilter>(filterJson); }
            catch { return "筛选格式无效"; }
            if (filter == null) return "筛选格式无效";

            List<SessionView> sessions = _store.Load();
            List<SessionView> selected = new List<SessionView>();
            for (int i = 0; i < sessions.Count; i++)
            {
                SessionView session = sessions[i];
                long startMs = session.Start.ToUnixTimeMilliseconds();
                if (filter.FromMs > 0 && startMs < filter.FromMs) continue;
                if (filter.ToMs > 0 && startMs > filter.ToMs) continue;
                if (!string.IsNullOrEmpty(filter.Device) && !string.Equals(session.Device, filter.Device, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(filter.App) && !string.Equals(session.Identity, filter.App, StringComparison.OrdinalIgnoreCase)) continue;
                selected.Add(session);
            }

            string path = _form.ShowCsvSaveDialog();
            if (path == null) return "cancel";

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("开始,结束,时长秒,程序,设备,账户,路径,备注");
            for (int i = 0; i < selected.Count; i++)
            {
                SessionView session = selected[i];
                builder.Append(Csv(session.Start.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',');
                builder.Append(Csv(session.End.HasValue ? session.End.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : "")).Append(',');
                builder.Append(session.Seconds).Append(',');
                builder.Append(Csv(session.App)).Append(',');
                builder.Append(Csv(session.Device)).Append(',');
                builder.Append(Csv(session.User)).Append(',');
                builder.Append(Csv(session.PathText)).Append(',');
                builder.Append(Csv(session.Note)).AppendLine();
            }

            try
            {
                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
                return "ok|" + path;
            }
            catch (Exception ex)
            {
                return "导出失败：" + ex.Message;
            }
        }

        public string ClearLogs()
        {
            try
            {
                string[] files = Directory.GetFiles(_logger.LogDirectory, "*.jsonl");
                for (int i = 0; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); }
                    catch { }
                }
                return "ok";
            }
            catch (Exception ex)
            {
                return "清理失败：" + ex.Message;
            }
        }

        public void CloseMenu()
        {
            if (_form != null) _form.Hide();
        }

        public void ExitApp()
        {
            if (_onExit != null) _onExit();
        }

        public void OpenLogFolder()
        {
            try { Process.Start(_logger.LogDirectory); }
            catch { }
        }

        private UiConfig SnapshotConfig()
        {
            UiConfig cfg = new UiConfig();
            cfg.DisplayMode = _config.Value.DisplayMode;
            cfg.TaskbarMode = _config.Value.TaskbarMode;
            cfg.TaskbarApps = new List<string>(_config.Value.TaskbarApps != null ? _config.Value.TaskbarApps : new List<string>());
            cfg.RetentionDays = _config.Value.RetentionDays;
            cfg.OverlayText = _config.Value.OverlayText;
            return cfg;
        }

        private List<UiSession> LoadSessions()
        {
            List<SessionView> sessions = _store.Load();
            int start = sessions.Count > 20000 ? sessions.Count - 20000 : 0;
            List<UiSession> result = new List<UiSession>();
            for (int i = start; i < sessions.Count; i++)
            {
                SessionView session = sessions[i];
                UiSession item = new UiSession();
                item.StartMs = session.Start.ToUnixTimeMilliseconds();
                item.EndMs = session.End.HasValue ? session.End.Value.ToUnixTimeMilliseconds() : 0;
                item.App = session.App;
                item.Identity = session.Identity;
                item.Path = session.PathText;
                item.Device = session.Device;
                item.User = session.User;
                item.Seconds = session.Seconds;
                item.Note = session.Note;
                result.Add(item);
            }
            return result;
        }

        private List<string> LoadDevices(List<UiSession> sessions)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < sessions.Count; i++)
            {
                string name = sessions[i].Device;
                if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
            }
            List<DeviceEntry> inventory = _devices.Snapshot();
            for (int i = 0; i < inventory.Count; i++)
            {
                string name = inventory[i].Name;
                if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
            }
            names.Sort(StringComparer.CurrentCulture);
            return names;
        }

        private List<UiActive> ActiveSessions()
        {
            List<ActiveInfo> active = _monitor.GetActiveSnapshot();
            List<UiActive> result = new List<UiActive>();
            DateTimeOffset now = DateTimeOffset.Now;
            for (int i = 0; i < active.Count; i++)
            {
                UiActive item = new UiActive();
                item.App = active[i].App;
                item.Identity = active[i].Identity;
                item.Device = active[i].Device;
                TimeSpan elapsed = now - active[i].Start;
                item.Seconds = (long)Math.Max(0, elapsed.TotalSeconds);
                result.Add(item);
            }
            return result;
        }

        private static string Csv(string value)
        {
            if (value == null) return "";
            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
