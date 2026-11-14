using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    internal sealed class SettingsForm : Form
    {
        private readonly ConfigStore _config;
        private readonly RadioButton _modeAny;
        private readonly RadioButton _modeOnly;
        private readonly RadioButton _modeExclude;
        private readonly CheckedListBox _apps;
        private readonly TextBox _manual;
        private readonly NumericUpDown _retention;

        public SettingsForm(ConfigStore config, List<AppChoice> knownApps)
        {
            _config = config;

            Text = "设置";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(560, 610);
            MinimumSize = new Size(520, 520);
            BackColor = Color.White;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            GroupBox ruleGroup = new GroupBox();
            ruleGroup.Text = "占用指示图标显示规则（只影响图标，不影响日志记录）";
            ruleGroup.Dock = DockStyle.Fill;
            ruleGroup.Padding = new Padding(12, 8, 12, 12);
            root.Controls.Add(ruleGroup, 0, 0);

            TableLayoutPanel rules = new TableLayoutPanel();
            rules.Dock = DockStyle.Fill;
            rules.ColumnCount = 1;
            rules.RowCount = 7;
            rules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rules.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rules.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rules.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rules.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rules.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rules.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rules.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ruleGroup.Controls.Add(rules);

            _modeAny = MakeRadio("任何应用占用摄像头都显示");
            _modeOnly = MakeRadio("仅名单内应用占用时显示");
            _modeExclude = MakeRadio("忽略名单内应用的占用（其他应用照常显示）");
            rules.Controls.Add(_modeAny, 0, 0);
            rules.Controls.Add(_modeOnly, 0, 1);
            rules.Controls.Add(_modeExclude, 0, 2);

            Label hint = new Label();
            hint.Text = "名单按可执行文件名（如 WeChat.exe）或 UWP 包族名匹配：";
            hint.AutoSize = true;
            hint.Dock = DockStyle.Fill;
            hint.Margin = new Padding(3, 10, 3, 5);
            rules.Controls.Add(hint, 0, 3);

            _apps = new CheckedListBox();
            _apps.Dock = DockStyle.Fill;
            _apps.CheckOnClick = true;
            _apps.IntegralHeight = false;
            _apps.MinimumSize = new Size(0, 120);
            rules.Controls.Add(_apps, 0, 4);

            Label manualLabel = new Label();
            manualLabel.Text = "手动添加（可执行文件名或 UWP 包族名）：";
            manualLabel.AutoSize = true;
            manualLabel.Dock = DockStyle.Fill;
            manualLabel.Margin = new Padding(3, 10, 3, 4);
            rules.Controls.Add(manualLabel, 0, 5);

            TableLayoutPanel manualRow = new TableLayoutPanel();
            manualRow.Dock = DockStyle.Fill;
            manualRow.AutoSize = true;
            manualRow.ColumnCount = 2;
            manualRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            manualRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _manual = new TextBox();
            _manual.Dock = DockStyle.Fill;
            _manual.Margin = new Padding(0, 3, 8, 3);
            Button addManual = MakeButton("添加");
            addManual.Click += delegate { AddManualEntry(); };
            manualRow.Controls.Add(_manual, 0, 0);
            manualRow.Controls.Add(addManual, 1, 0);
            rules.Controls.Add(manualRow, 0, 6);

            FlowLayoutPanel options = new FlowLayoutPanel();
            options.Dock = DockStyle.Fill;
            options.AutoSize = true;
            options.WrapContents = true;
            options.FlowDirection = FlowDirection.LeftToRight;
            options.Padding = new Padding(0, 10, 0, 8);
            Label retentionLabel = new Label();
            retentionLabel.Text = "日志保留天数：";
            retentionLabel.AutoSize = true;
            retentionLabel.Margin = new Padding(3, 7, 4, 3);
            _retention = new NumericUpDown();
            _retention.Minimum = 1;
            _retention.Maximum = 3650;
            _retention.Value = config.Value.RetentionDays;
            _retention.Width = 90;
            _retention.Margin = new Padding(0, 3, 16, 3);
            Button change = MakeButton("修改口令");
            change.Click += delegate
            {
                using (ChangePasswordForm dialog = new ChangePasswordForm(config)) dialog.ShowDialog(this);
            };
            options.Controls.Add(retentionLabel);
            options.Controls.Add(_retention);
            options.Controls.Add(change);
            root.Controls.Add(options, 0, 1);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.AutoSize = true;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            Button cancel = MakeButton("取消");
            cancel.DialogResult = DialogResult.Cancel;
            Button ok = MakeButton("确定");
            ok.Click += delegate { SaveAndClose(); };
            actions.Controls.Add(cancel);
            actions.Controls.Add(ok);
            root.Controls.Add(actions, 0, 2);
            AcceptButton = ok;
            CancelButton = cancel;

            if (knownApps != null)
            {
                for (int i = 0; i < knownApps.Count; i++)
                {
                    AppChoice choice = knownApps[i];
                    bool isChecked = false;
                    if (config.Value.TaskbarApps != null)
                    {
                        for (int j = 0; j < config.Value.TaskbarApps.Count; j++)
                        {
                            if (string.Equals(config.Value.TaskbarApps[j], choice.Identity, StringComparison.OrdinalIgnoreCase))
                            {
                                isChecked = true;
                                break;
                            }
                        }
                    }
                    _apps.Items.Add(choice, isChecked);
                }
            }

            string mode = config.Value.TaskbarMode;
            _modeOnly.Checked = mode == "only";
            _modeExclude.Checked = mode == "exclude";
            _modeAny.Checked = !_modeOnly.Checked && !_modeExclude.Checked;
        }

        private static RadioButton MakeRadio(string text)
        {
            RadioButton radio = new RadioButton();
            radio.Text = text;
            radio.AutoSize = true;
            radio.Dock = DockStyle.Fill;
            radio.Margin = new Padding(3, 4, 3, 4);
            return radio;
        }

        private static Button MakeButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(88, 30);
            button.Margin = new Padding(4, 3, 4, 3);
            return button;
        }

        private void AddManualEntry()
        {
            string text = _manual.Text.Trim();
            if (text.Length == 0) return;
            for (int i = 0; i < _apps.Items.Count; i++)
            {
                AppChoice existing = _apps.Items[i] as AppChoice;
                if (existing != null && string.Equals(existing.Identity, text, StringComparison.OrdinalIgnoreCase))
                {
                    _apps.SetItemChecked(i, true);
                    _manual.Text = "";
                    return;
                }
            }
            AppChoice choice = new AppChoice();
            choice.Identity = text;
            choice.Display = text;
            int index = _apps.Items.Add(choice, true);
            _apps.SetItemChecked(index, true);
            _manual.Text = "";
        }

        private void SaveAndClose()
        {
            string mode = "any";
            if (_modeOnly.Checked) mode = "only";
            else if (_modeExclude.Checked) mode = "exclude";
            _config.Value.TaskbarMode = mode;

            List<string> apps = new List<string>();
            for (int i = 0; i < _apps.CheckedItems.Count; i++)
            {
                AppChoice choice = _apps.CheckedItems[i] as AppChoice;
                if (choice != null && !string.IsNullOrEmpty(choice.Identity)) apps.Add(choice.Identity);
            }
            _config.Value.TaskbarApps = apps;
            _config.Value.RetentionDays = (int)_retention.Value;
            _config.Save();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
