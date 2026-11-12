using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AudioEndpointSvc
{
    internal sealed class SessionView
    {
        public DateTimeOffset Start;
        public DateTimeOffset? End;
        public string App;
        public string Exe;
        public string Pfn;
        public int? Pid;
        public string User;
        public string Device;
        public long Seconds;
        public string Note;

        public string Identity
        {
            get
            {
                if (!string.IsNullOrEmpty(Pfn)) return Pfn;
                try { return Path.GetFileName(Exe); }
                catch { return Exe; }
            }
        }

        public string PathText
        {
            get
            {
                if (!string.IsNullOrEmpty(Exe)) return Exe;
                return Pfn != null ? Pfn : "";
            }
        }
    }

    internal sealed class AppChoice
    {
        public string Identity;
        public string Display;

        public override string ToString()
        {
            return Display;
        }
    }

    internal sealed class LogStore
    {
        private readonly Logger _logger;

        public LogStore(Logger logger)
        {
            _logger = logger;
        }

        public List<SessionView> Load()
        {
            List<SessionView> list = new List<SessionView>();
            try
            {
                string[] files = Directory.GetFiles(_logger.LogDirectory, "*.jsonl");
                for (int f = 0; f < files.Length; f++)
                {
                    string[] lines = null;
                    try { lines = File.ReadAllLines(files[f], Encoding.UTF8); }
                    catch { continue; }
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (line == null || line.Length < 2) continue;
                        SessionRecord record;
                        try { record = Json.FromLine<SessionRecord>(line); }
                        catch { continue; }
                        if (record == null || record.Start == null) continue;
                        SessionView view;
                        if (!TryConvert(record, out view)) continue;
                        list.Add(view);
                    }
                }
            }
            catch { }
            list.Sort(delegate (SessionView a, SessionView b) { return a.Start.CompareTo(b.Start); });
            return list;
        }

        public static bool TryConvert(SessionRecord record, out SessionView view)
        {
            view = null;
            DateTimeOffset start;
            if (!DateTimeOffset.TryParse(record.Start, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start)) return false;
            DateTimeOffset? end = null;
            if (!string.IsNullOrEmpty(record.End))
            {
                DateTimeOffset parsedEnd;
                if (DateTimeOffset.TryParse(record.End, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedEnd)) end = parsedEnd;
            }

            view = new SessionView();
            view.Start = start;
            view.End = end;
            view.App = string.IsNullOrEmpty(record.App) ? "未知应用" : record.App;
            view.Exe = record.Exe;
            view.Pfn = record.Pfn;
            view.Pid = record.Pid;
            view.User = record.User;
            view.Device = record.Device;
            view.Seconds = record.Seconds;
            view.Note = record.Note;
            return true;
        }

        public static long UnionSeconds(List<SessionView> sessions)
        {
            List<SessionView> withEnd = new List<SessionView>();
            for (int i = 0; i < sessions.Count; i++)
            {
                if (sessions[i].End.HasValue) withEnd.Add(sessions[i]);
            }
            withEnd.Sort(delegate (SessionView a, SessionView b) { return a.Start.CompareTo(b.Start); });

            long total = 0;
            DateTimeOffset? curStart = null;
            DateTimeOffset? curEnd = null;
            for (int i = 0; i < withEnd.Count; i++)
            {
                DateTimeOffset start = withEnd[i].Start;
                DateTimeOffset end = withEnd[i].End.Value;
                if (end < start) end = start;
                if (!curStart.HasValue)
                {
                    curStart = start;
                    curEnd = end;
                }
                else if (start <= curEnd.Value)
                {
                    if (end > curEnd.Value) curEnd = end;
                }
                else
                {
                    total += (long)Math.Round((curEnd.Value - curStart.Value).TotalSeconds);
                    curStart = start;
                    curEnd = end;
                }
            }
            if (curStart.HasValue) total += (long)Math.Round((curEnd.Value - curStart.Value).TotalSeconds);
            return total;
        }
    }
}
