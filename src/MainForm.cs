using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CamWatch
{
    internal sealed class MainForm : Form
    {
        private readonly ConfigStore _config;
        private readonly Logger _logger;
        private readonly CameraMonitor _monitor;
        private readonly DeviceInventory _devices;
        private readonly Action _onExit;
        private readonly LogStore _store;

        private ComboBox _rangeCombo;
        private ComboBox _appCombo;
        private ComboBox _deviceCombo;
        private Label _liveLabel;
        private Label _statsLabel;
        private DataGridView _sessionsGrid;
        private DataGridView _summaryGrid;
        private Timer _liveTimer;

        private List<SessionView> _sessions = new List<SessionView>();
        private List<SessionView> _filtered = new List<SessionView>();
        private bool _allowClose;
        private bool _building;

        private sealed class ComboItem
        {
            public string Id;
            public string Text;

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class GroupStat
        {
            public string Identity;
            public string Display;
            public int Count;
            public long Total;
            public long Max;
            public DateTimeOffset LastStart;
        }

        public MainForm(ConfigStore config, Logger logger, CameraMonitor monitor, DeviceInventory devices, Action onExit)
        {
            _config = config;
            _logger = logger;
            _monitor = monitor;
            _devices = devices;
            _onExit = onExit;
            _store = new LogStore(logger);

            Text = "设备使用记录";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 640);
            MinimumSize = new Size(880, 520);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.White;

            BuildUi();
            LoadData();

            _liveTimer = new Timer();
            _liveTimer.Interval = 5000;
            _liveTimer.Tick += delegate { UpdateLive(); };
            _liveTimer.Start();
        }

        private void BuildUi()
        {
            _building = true;
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 106;
            Controls.Add(top);

            Label rangeLabel = new Label();
            rangeLabel.Text = "范围";
            rangeLabel.AutoSize = true;
            rangeLabel.Location = new Point(16, 14);
            top.Controls.Add(rangeLabel);

            _rangeCombo = new ComboBox();
            _rangeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _rangeCombo.Location = new Point(56, 10);
            _rangeCombo.Width = 100;
            _rangeCombo.Items.Add("今天");
            _rangeCombo.Items.Add("近 7 天");
            _rangeCombo.Items.Add("近 30 天");
            _rangeCombo.Items.Add("全部");
            _rangeCombo.SelectedIndex = 0;
            _rangeCombo.SelectedIndexChanged += delegate { if (!_building) ApplyFilter(); };
            top.Controls.Add(_rangeCombo);

            Label appLabel = new Label();
            appLabel.Text = "程序";
            appLabel.AutoSize = true;
            appLabel.Location = new Point(172, 14);
            top.Controls.Add(appLabel);

            _appCombo = new ComboBox();
            _appCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _appCombo.Location = new Point(212, 10);
            _appCombo.Width = 220;
            _appCombo.SelectedIndexChanged += delegate { if (!_building) ApplyFilter(); };
            top.Controls.Add(_appCombo);

            Label deviceLabel = new Label();
            deviceLabel.Text = "设备";
            deviceLabel.AutoSize = true;
            deviceLabel.Location = new Point(448, 14);
            top.Controls.Add(deviceLabel);

            _deviceCombo = new ComboBox();
            _deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _deviceCombo.Location = new Point(488, 10);
            _deviceCombo.Width = 180;
            _deviceCombo.SelectedIndexChanged += delegate { if (!_building) ApplyFilter(); };
            top.Controls.Add(_deviceCombo);

            Button refresh = new Button();
            refresh.Text = "刷新";
            refresh.Location = new Point(688, 8);
            refresh.Width = 70;
            refresh.Click += delegate { LoadData(); };
            top.Controls.Add(refresh);

            Button settings = new Button();
            settings.Text = "设置";
            settings.Location = new Point(766, 8);
            settings.Width = 70;
            settings.Click += delegate { OpenSettings(); };
            top.Controls.Add(settings);

            _liveLabel = new Label();
            _liveLabel.AutoSize = true;
            _liveLabel.Location = new Point(16, 48);
            _liveLabel.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            top.Controls.Add(_liveLabel);

            _statsLabel = new Label();
            _statsLabel.AutoSize = true;
            _statsLabel.Location = new Point(16, 74);
            top.Controls.Add(_statsLabel);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            Controls.Add(tabs);

            TabPage sessionsPage = new TabPage("会话明细");
            sessionsPage.BackColor = Color.White;
            tabs.TabPages.Add(sessionsPage);

            _sessionsGrid = new DataGridView();
            _sessionsGrid.Dock = DockStyle.Fill;
            _sessionsGrid.ReadOnly = true;
            _sessionsGrid.AllowUserToAddRows = false;
            _sessionsGrid.AllowUserToDeleteRows = false;
            _sessionsGrid.AllowUserToResizeRows = false;
            _sessionsGrid.RowHeadersVisible = false;
            _sessionsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _sessionsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _sessionsGrid.MultiSelect = false;
            _sessionsGrid.BorderStyle = BorderStyle.None;
            _sessionsGrid.BackgroundColor = Color.White;
            AddTextColumn(_sessionsGrid, "date", "日期", 80);
            AddTextColumn(_sessionsGrid, "start", "开始", 70);
            AddTextColumn(_sessionsGrid, "end", "结束", 70);
            AddTextColumn(_sessionsGrid, "duration", "时长", 70);
            AddTextColumn(_sessionsGrid, "app", "程序", 120);
            AddTextColumn(_sessionsGrid, "device", "设备", 110);
            AddTextColumn(_sessionsGrid, "user", "账户", 90);
            AddTextColumn(_sessionsGrid, "path", "路径", 200);
            sessionsPage.Controls.Add(_sessionsGrid);

            TabPage summaryPage = new TabPage("按程序汇总");
            summaryPage.BackColor = Color.White;
            tabs.TabPages.Add(summaryPage);

            _summaryGrid = new DataGridView();
            _summaryGrid.Dock = DockStyle.Fill;
            _summaryGrid.ReadOnly = true;
            _summaryGrid.AllowUserToAddRows = false;
            _summaryGrid.AllowUserToDeleteRows = false;
            _summaryGrid.AllowUserToResizeRows = false;
            _summaryGrid.RowHeadersVisible = false;
            _summaryGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _summaryGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _summaryGrid.MultiSelect = false;
            _summaryGrid.BorderStyle = BorderStyle.None;
            _summaryGrid.BackgroundColor = Color.White;
            AddTextColumn(_summaryGrid, "app", "程序", 240);
            AddTextColumn(_summaryGrid, "count", "次数", 70);
            AddTextColumn(_summaryGrid, "total", "总时长", 110);
            AddTextColumn(_summaryGrid, "avg", "平均每次", 100);
            AddTextColumn(_summaryGrid, "max", "最长一次", 100);
            AddTextColumn(_summaryGrid, "last", "最近一次", 150);
            _summaryGrid.CellClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                object tag = _summaryGrid.Rows[e.RowIndex].Tag;
                if (tag is string) SelectApp((string)tag);
            };
            summaryPage.Controls.Add(_summaryGrid);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 46;
            Controls.Add(bottom);

            Button export = new Button();
            export.Text = "导出 CSV";
            export.Location = new Point(590, 10);
            export.Width = 92;
            export.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            export.Click += delegate { ExportCsv(); };
            bottom.Controls.Add(export);

            Button clear = new Button();
            clear.Text = "清理日志";
            clear.Location = new Point(690, 10);
            clear.Width = 92;
            clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.Click += delegate { ClearLogs(); };
            bottom.Controls.Add(clear);

            Button hide = new Button();
            hide.Text = "关闭";
            hide.Location = new Point(790, 10);
            hide.Width = 70;
            hide.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hide.Click += delegate { Hide(); };
            bottom.Controls.Add(hide);

            Button exit = new Button();
            exit.Text = "退出程序";
            exit.Location = new Point(868, 10);
            exit.Width = 92;
            exit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            exit.Click += delegate { ExitProgram(); };
            bottom.Controls.Add(exit);

            // Bottom panel must sit above the tab control fill area.
            bottom.BringToFront();
            top.BringToFront();
            _building = false;
        }

        private static void AddTextColumn(DataGridView grid, string name, string header, int weight)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = header;
            column.ReadOnly = true;
            column.FillWeight = weight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(column);
        }

        private void LoadData()
        {
            _sessions = _store.Load();
            _building = true;
            try
            {
                BuildAppCombo();
                BuildDeviceCombo();
            }
            finally
            {
                _building = false;
            }
            ApplyFilter();
            UpdateLive();
        }

        private void BuildAppCombo()
        {
            string selected = SelectedId(_appCombo);
            Dictionary<string, string> displays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _sessions.Count; i++)
            {
                SessionView session = _sessions[i];
                if (string.IsNullOrEmpty(session.Identity)) continue;
                displays[session.Identity] = session.App;
            }

            List<string> identities = new List<string>(displays.Keys);
            identities.Sort(delegate(string a, string b)
            {
                return string.Compare(displays[a] + a, displays[b] + b, StringComparison.CurrentCulture);
            });

            _appCombo.Items.Clear();
            ComboItem all = new ComboItem();
            all.Id = null;
            all.Text = "全部程序";
            _appCombo.Items.Add(all);
            for (int i = 0; i < identities.Count; i++)
            {
                ComboItem item = new ComboItem();
                item.Id = identities[i];
                item.Text = displays[identities[i]] + "  (" + identities[i] + ")";
                _appCombo.Items.Add(item);
            }
            SelectId(_appCombo, selected);
        }

        private void BuildDeviceCombo()
        {
            string selected = SelectedId(_deviceCombo);
            List<string> names = new List<string>();
            for (int i = 0; i < _sessions.Count; i++)
            {
                string device = _sessions[i].Device;
                if (string.IsNullOrEmpty(device)) continue;
                bool exists = false;
                for (int j = 0; j < names.Count; j++)
                {
                    if (string.Equals(names[j], device, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                }
                if (!exists) names.Add(device);
            }
            names.Sort(StringComparer.CurrentCulture);

            _deviceCombo.Items.Clear();
            ComboItem all = new ComboItem();
            all.Id = null;
            all.Text = "全部设备";
            _deviceCombo.Items.Add(all);
            for (int i = 0; i < names.Count; i++)
            {
                ComboItem item = new ComboItem();
                item.Id = names[i];
                item.Text = names[i];
                _deviceCombo.Items.Add(item);
            }
            SelectId(_deviceCombo, selected);
        }

        private static string SelectedId(ComboBox combo)
        {
            ComboItem item = combo != null ? combo.SelectedItem as ComboItem : null;
            return item != null ? item.Id : null;
        }

        private static void SelectId(ComboBox combo, string id)
        {
            if (combo.Items.Count == 0) return;
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    ComboItem item = combo.Items[i] as ComboItem;
                    if (item != null && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        combo.SelectedIndex = i;
                        return;
                    }
                }
            }
            combo.SelectedIndex = 0;
        }

        private void ApplyFilter()
        {
            DateTimeOffset? rangeStart = null;
            int rangeIndex = _rangeCombo.SelectedIndex;
            DateTime today = DateTime.Now.Date;
            if (rangeIndex == 0) rangeStart = new DateTimeOffset(today);
            else if (rangeIndex == 1) rangeStart = new DateTimeOffset(today.AddDays(-6));
            else if (rangeIndex == 2) rangeStart = new DateTimeOffset(today.AddDays(-29));

            string appId = SelectedId(_appCombo);
            string deviceId = SelectedId(_deviceCombo);

            List<SessionView> baseList = new List<SessionView>();
            for (int i = 0; i < _sessions.Count; i++)
            {
                SessionView session = _sessions[i];
                if (rangeStart.HasValue && session.Start < rangeStart.Value) continue;
                if (!string.IsNullOrEmpty(deviceId) && !string.Equals(session.Device, deviceId, StringComparison.OrdinalIgnoreCase)) continue;
                baseList.Add(session);
            }

            List<SessionView> filtered = new List<SessionView>();
            for (int i = 0; i < baseList.Count; i++)
            {
                if (string.IsNullOrEmpty(appId) || string.Equals(baseList[i].Identity, appId, StringComparison.OrdinalIgnoreCase)) filtered.Add(baseList[i]);
            }
            _filtered = filtered;

            long unionSeconds = LogStore.UnionSeconds(baseList);
            long filteredSeconds = 0;
            for (int i = 0; i < filtered.Count; i++) filteredSeconds += filtered[i].Seconds;
            _statsLabel.Text = "摄像头占用（并集）：" + FormatDuration(unionSeconds)
                + "    当前筛选：" + filtered.Count + " 次 / 按程序累计 " + FormatDuration(filteredSeconds);

            _sessionsGrid.Rows.Clear();
            for (int i = filtered.Count - 1; i >= 0; i--)
            {
                SessionView session = filtered[i];
                _sessionsGrid.Rows.Add(
                    session.Start.LocalDateTime.ToString("yyyy-MM-dd"),
                    session.Start.LocalDateTime.ToString("HH:mm:ss"),
                    session.End.HasValue ? session.End.Value.LocalDateTime.ToString("HH:mm:ss") : "",
                    FormatDuration(session.Seconds),
                    session.App,
                    session.Device,
                    session.User,
                    session.PathText);
            }

            _summaryGrid.Rows.Clear();
            Dictionary<string, GroupStat> groups = new Dictionary<string, GroupStat>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < baseList.Count; i++)
            {
                SessionView session = baseList[i];
                string identity = string.IsNullOrEmpty(session.Identity) ? "未知" : session.Identity;
                GroupStat stat;
                if (!groups.TryGetValue(identity, out stat))
                {
                    stat = new GroupStat();
                    stat.Identity = identity;
                    stat.Display = session.App;
                    groups[identity] = stat;
                }
                stat.Count++;
                stat.Total += session.Seconds;
                if (session.Seconds > stat.Max) stat.Max = session.Seconds;
                if (session.Start > stat.LastStart || stat.Count == 1) stat.LastStart = session.Start;
                stat.Display = session.App;
            }
            List<GroupStat> stats = new List<GroupStat>(groups.Values);
            stats.Sort(delegate(GroupStat a, GroupStat b) { return b.Total.CompareTo(a.Total); });
            for (int i = 0; i < stats.Count; i++)
            {
                GroupStat stat = stats[i];
                int index = _summaryGrid.Rows.Add(
                    stat.Display + "  (" + stat.Identity + ")",
                    stat.Count.ToString(),
                    FormatDuration(stat.Total),
                    FormatDuration(stat.Count > 0 ? stat.Total / stat.Count : 0),
                    FormatDuration(stat.Max),
                    stat.LastStart.LocalDateTime.ToString("yyyy-MM-dd HH:mm"));
                _summaryGrid.Rows[index].Tag = stat.Identity;
            }
        }

        private void SelectApp(string identity)
        {
            _building = true;
            try
            {
                SelectId(_appCombo, identity);
            }
            finally
            {
                _building = false;
            }
            ApplyFilter();
        }

        private void UpdateLive()
        {
            try
            {
                List<ActiveInfo> active = _monitor.GetActiveSnapshot();
                if (active.Count == 0)
                {
                    _liveLabel.Text = "当前：无摄像头占用";
                }
                else
                {
                    StringBuilder builder = new StringBuilder();
                    builder.Append("当前：");
                    DateTimeOffset now = DateTimeOffset.Now;
                    for (int i = 0; i < active.Count; i++)
                    {
                        if (i > 0) builder.Append("；");
                        builder.Append(active[i].App);
                        TimeSpan elapsed = now - active[i].Start;
                        if (elapsed.TotalSeconds > 0) builder.Append("（已用 ").Append(FormatDuration((long)elapsed.TotalSeconds)).Append("）");
                    }
                    _liveLabel.Text = builder.ToString();
                }

                string mode = "全部应用";
                if (_config.Value.TaskbarMode == "only") mode = "仅名单内应用";
                else if (_config.Value.TaskbarMode == "exclude") mode = "忽略名单内应用";
                List<DeviceEntry> devices = _devices.Snapshot();
                string deviceText;
                if (devices.Count == 0) deviceText = "未检测到摄像头";
                else if (devices.Count == 1) deviceText = devices[0].Name;
                else deviceText = devices.Count + " 个摄像头（设备归因未知）";

                _statsLabel.Text = _statsLabel.Text.Split(new string[] { "    任务栏图标" }, StringSplitOptions.None)[0]
                    + "    任务栏图标：" + mode + "    设备：" + deviceText;
            }
            catch { }
        }

        private void OpenSettings()
        {
            List<AppChoice> known = BuildKnownApps();
            using (SettingsForm dialog = new SettingsForm(_config, known))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _logger.CleanupOldLogs();
                    LoadData();
                }
                else
                {
                    UpdateLive();
                }
            }
        }

        private List<AppChoice> BuildKnownApps()
        {
            Dictionary<string, AppChoice> map = new Dictionary<string, AppChoice>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _sessions.Count; i++)
            {
                SessionView session = _sessions[i];
                if (string.IsNullOrEmpty(session.Identity)) continue;
                AppChoice choice = new AppChoice();
                choice.Identity = session.Identity;
                choice.Display = session.App + "  (" + session.Identity + ")";
                map[session.Identity] = choice;
            }
            List<ActiveInfo> active = _monitor.GetActiveSnapshot();
            for (int i = 0; i < active.Count; i++)
            {
                if (string.IsNullOrEmpty(active[i].Identity)) continue;
                AppChoice choice = new AppChoice();
                choice.Identity = active[i].Identity;
                choice.Display = active[i].App + "  (" + active[i].Identity + ")";
                map[active[i].Identity] = choice;
            }
            if (_config.Value.TaskbarApps != null)
            {
                for (int i = 0; i < _config.Value.TaskbarApps.Count; i++)
                {
                    string identity = _config.Value.TaskbarApps[i];
                    if (string.IsNullOrEmpty(identity) || map.ContainsKey(identity)) continue;
                    AppChoice choice = new AppChoice();
                    choice.Identity = identity;
                    choice.Display = identity;
                    map[identity] = choice;
                }
            }
            List<AppChoice> list = new List<AppChoice>(map.Values);
            list.Sort(delegate(AppChoice a, AppChoice b) { return string.Compare(a.Display, b.Display, StringComparison.CurrentCulture); });
            return list;
        }

        private void ExportCsv()
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "CSV 文件 (*.csv)|*.csv";
            dialog.FileName = "摄像头使用记录_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("开始,结束,时长秒,程序,设备,账户,路径,备注");
            for (int i = 0; i < _filtered.Count; i++)
            {
                SessionView session = _filtered[i];
                builder.Append(CsvField(session.Start.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',');
                builder.Append(CsvField(session.End.HasValue ? session.End.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : "")).Append(',');
                builder.Append(session.Seconds).Append(',');
                builder.Append(CsvField(session.App)).Append(',');
                builder.Append(CsvField(session.Device)).Append(',');
                builder.Append(CsvField(session.User)).Append(',');
                builder.Append(CsvField(session.PathText)).Append(',');
                builder.Append(CsvField(session.Note)).AppendLine();
            }
            try
            {
                File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
                MessageBox.Show(this, "已导出 " + _filtered.Count + " 条记录。", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string CsvField(string value)
        {
            if (value == null) return "";
            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private void ClearLogs()
        {
            if (MessageBox.Show(this, "确定清空全部使用日志？此操作不可恢复。", "清理日志", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                string[] files = Directory.GetFiles(_logger.LogDirectory, "*.jsonl");
                for (int i = 0; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
                LoadData();
                MessageBox.Show(this, "日志已清空。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "清理失败：" + ex.Message, "清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExitProgram()
        {
            if (MessageBox.Show(this, "确定退出监控程序？退出后将不再记录摄像头占用。", "退出", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _allowClose = true;
            _onExit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        private static string FormatDuration(long seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds < 60) return seconds + " 秒";
            long minutes = seconds / 60;
            long hours = minutes / 60;
            long days = hours / 24;
            if (days > 0) return days + " 天 " + (hours % 24) + " 小时 " + (minutes % 60) + " 分";
            if (hours > 0) return hours + " 小时 " + (minutes % 60) + " 分";
            return minutes + " 分钟";
        }
    }
}
