using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace CamWatch
{
    [DataContract]
    internal sealed class SessionRecord
    {
        [DataMember(Name = "type")] public string Type = "session";
        [DataMember(Name = "v")] public int Version = 1;
        [DataMember(Name = "app", EmitDefaultValue = false)] public string App;
        [DataMember(Name = "exe", EmitDefaultValue = false)] public string Exe;
        [DataMember(Name = "pfn", EmitDefaultValue = false)] public string Pfn;
        [DataMember(Name = "pid", EmitDefaultValue = false)] public int? Pid;
        [DataMember(Name = "user", EmitDefaultValue = false)] public string User;
        [DataMember(Name = "device", EmitDefaultValue = false)] public string Device;
        [DataMember(Name = "start")] public string Start;
        [DataMember(Name = "end", EmitDefaultValue = false)] public string End;
        [DataMember(Name = "seconds")] public long Seconds;
        [DataMember(Name = "note", EmitDefaultValue = false)] public string Note;
    }

    [DataContract]
    internal sealed class OpenSessionState
    {
        [DataMember(Name = "key")] public string Key;
        [DataMember(Name = "app")] public string App;
        [DataMember(Name = "exe", EmitDefaultValue = false)] public string Exe;
        [DataMember(Name = "pfn", EmitDefaultValue = false)] public string Pfn;
        [DataMember(Name = "start")] public string Start;
        [DataMember(Name = "device", EmitDefaultValue = false)] public string Device;
    }

    [DataContract]
    internal sealed class StateFile
    {
        [DataMember(Name = "open")] public List<OpenSessionState> Open;
    }

    [DataContract]
    internal sealed class DeviceEntry
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "id", EmitDefaultValue = false)] public string Id;
    }

    [DataContract]
    internal sealed class DeviceSnapshot
    {
        [DataMember(Name = "updated")] public string Updated;
        [DataMember(Name = "devices")] public List<DeviceEntry> Devices;
    }

    internal sealed class Logger
    {
        private readonly string _baseDir;
        private readonly string _logDir;
        private readonly ConfigStore _config;
        private readonly object _gate = new object();

        public Logger(string baseDir, ConfigStore config)
        {
            _baseDir = baseDir;
            _config = config;
            _logDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(_logDir);
        }

        public string LogDirectory { get { return _logDir; } }

        public void AppendSession(SessionRecord record)
        {
            try
            {
                string day = DayOf(record.Start);
                string path = Path.Combine(_logDir, day + ".jsonl");
                lock (_gate)
                {
                    using (FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(Json.ToLine<SessionRecord>(record));
                        writer.Write("\r\n");
                    }
                }
            }
            catch { }
        }

        public void AppendError(string message)
        {
            try
            {
                string path = Path.Combine(_logDir, "errors.log");
                lock (_gate)
                {
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public void CleanupOldLogs()
        {
            try
            {
                int days = _config.Value.RetentionDays;
                DateTime cutoff = DateTime.Now.Date.AddDays(-days);
                string[] files = Directory.GetFiles(_logDir, "*.jsonl");
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(files[i]);
                    DateTime day;
                    if (DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                    {
                        if (day < cutoff)
                        {
                            try { File.Delete(files[i]); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        public StateFile LoadState()
        {
            try
            {
                StateFile file = Json.LoadFile<StateFile>(Path.Combine(_baseDir, "state.json"));
                return file != null ? file : new StateFile();
            }
            catch { return new StateFile(); }
        }

        public void SaveState(List<OpenSessionState> openSessions)
        {
            try
            {
                StateFile file = new StateFile();
                file.Open = openSessions != null ? openSessions : new List<OpenSessionState>();
                Json.SaveFile<StateFile>(Path.Combine(_baseDir, "state.json"), file);
            }
            catch { }
        }

        public void SaveDeviceSnapshot(List<DeviceEntry> devices)
        {
            try
            {
                DeviceSnapshot snapshot = new DeviceSnapshot();
                snapshot.Updated = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
                snapshot.Devices = devices != null ? devices : new List<DeviceEntry>();
                Json.SaveFile<DeviceSnapshot>(Path.Combine(_logDir, "devices.json"), snapshot);
            }
            catch { }
        }

        public static string DayOf(string iso)
        {
            try
            {
                DateTimeOffset dto = DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                return dto.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch
            {
                return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }
    }
}
