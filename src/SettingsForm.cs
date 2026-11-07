using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CamWatch
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
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(460, 490);
            BackColor = Color.White;

            GroupBox ruleGroup = new GroupBox();
            ruleGroup.Text = "任务栏图标显示规则（只影响图标，不影响日志记录）";
            ruleGroup.Location = new Point(12, 10);
            ruleGroup.Size = new Size(436, 340);
            Controls.Add(ruleGroup);

            _modeAny = new RadioButton();
            _modeAny.Text = "任何应用占用摄像头都显示";
            _modeAny.Location = new Point(14, 24);
            _modeAny.AutoSize = true;
            ruleGroup.Controls.Add(_modeAny);

            _modeOnly = new RadioButton();
            _modeOnly.Text = "仅名单内应用占用时显示";
            _modeOnly.Location = new Point(14, 50);
            _modeOnly.AutoSize = true;
            ruleGroup.Controls.Add(_modeOnly);

            _modeExclude = new RadioButton();
            _modeExclude.Text = "忽略名单内应用的占用（其他应用照常显示）";
            _modeExclude.Location = new Point(14, 76);
            _modeExclude.AutoSize = true;
            ruleGroup.Controls.Add(_modeExclude);

            Label hint = new Label();
            hint.Text = "名单按可执行文件名（如 WeChat.exe）或 UWP 包族名匹配：";
            hint.Location = new Point(14, 104);
            hint.AutoSize = true;
            ruleGroup.Controls.Add(hint);

            _apps = new CheckedListBox();
            _apps.Location = new Point(14, 126);
            _apps.Size = new Size(408, 150);
            _apps.CheckOnClick = true;
            _apps.IntegralHeight = false;
            ruleGroup.Controls.Add(_apps);

            Label manualLabel = new Label();
            manualLabel.Text = "手动添加（可执行文件名或 UWP 包族名）：";
            manualLabel.Location = new Point(14, 282);
            manualLabel.AutoSize = true;
            ruleGroup.Controls.Add(manualLabel);

            _manual = new TextBox();
            _manual.Location = new Point(14, 302);
            _manual.Width = 296;
            ruleGroup.Controls.Add(_manual);

            Button addManual = new Button();
            addManual.Text = "添加";
            addManual.Location = new Point(320, 300);
            addManual.Width = 84;
            addManual.Click += delegate { AddManualEntry(); };
            ruleGroup.Controls.Add(addManual);

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

            Label retentionLabel = new Label();
            retentionLabel.Text = "日志保留天数：";
            retentionLabel.Location = new Point(12, 352);
            retentionLabel.AutoSize = true;
            Controls.Add(retentionLabel);

            _retention = new NumericUpDown();
            _retention.Minimum = 1;
            _retention.Maximum = 3650;
            _retention.Value = config.Value.RetentionDays;
            _retention.Location = new Point(110, 350);
            _retention.Width = 80;
            Controls.Add(_retention);

            Button change = new Button();
            change.Text = "修改口令";
            change.Location = new Point(12, 386);
            change.Width = 90;
            change.Click += delegate
            {
                using (ChangePasswordForm dialog = new ChangePasswordForm(config)) dialog.ShowDialog(this);
            };
            Controls.Add(change);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(292, 448);
            ok.Width = 75;
            ok.Click += delegate { SaveAndClose(); };
            Controls.Add(ok);
            AcceptButton = ok;

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(373, 448);
            cancel.Width = 75;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;

            string mode = config.Value.TaskbarMode;
            _modeOnly.Checked = mode == "only";
            _modeExclude.Checked = mode == "exclude";
            _modeAny.Checked = !_modeOnly.Checked && !_modeExclude.Checked;
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
