using System;
using System.Drawing;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    internal sealed class PasswordForm : Form
    {
        private readonly ConfigStore _config;
        private readonly bool _setup;
        private readonly TextBox _first;
        private readonly TextBox _second;
        private readonly Label _message;

        public PasswordForm(ConfigStore config)
        {
            _config = config;
            _setup = !config.HasPassword;

            Text = "设备使用记录";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.White;

            TableLayoutPanel root = new TableLayoutPanel();
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.Padding = new Padding(18, 16, 18, 14);
            root.ColumnCount = 1;
            root.RowCount = _setup ? 7 : 5;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            Controls.Add(root);

            Label title = MakeLabel(_setup ? "首次使用，请设置管理口令" : "请输入管理口令", true);
            root.Controls.Add(title);

            root.Controls.Add(MakeLabel(_setup ? "新口令（至少 4 位）" : "口令", false));
            _first = MakeTextBox();
            root.Controls.Add(_first);

            if (_setup)
            {
                root.Controls.Add(MakeLabel("再次输入", false));
                _second = MakeTextBox();
                root.Controls.Add(_second);
            }

            _message = MakeLabel(" ", false);
            _message.ForeColor = Color.Firebrick;
            _message.MinimumSize = new Size(0, 22);
            root.Controls.Add(_message);

            FlowLayoutPanel actions = MakeActions();
            Button cancel = MakeButton("取消");
            cancel.DialogResult = DialogResult.Cancel;
            Button ok = MakeButton("确定");
            ok.Click += delegate { Confirm(); };
            actions.Controls.Add(cancel);
            actions.Controls.Add(ok);
            root.Controls.Add(actions);
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += delegate { _first.Focus(); };
        }

        private static Label MakeLabel(string text, bool title)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Dock = DockStyle.Fill;
            label.Margin = title ? new Padding(0, 0, 0, 12) : new Padding(0, 5, 0, 4);
            if (title) label.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            return label;
        }

        private static TextBox MakeTextBox()
        {
            TextBox text = new TextBox();
            text.UseSystemPasswordChar = true;
            text.Dock = DockStyle.Fill;
            text.Margin = new Padding(0, 0, 0, 6);
            return text;
        }

        private static FlowLayoutPanel MakeActions()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.AutoSize = true;
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.RightToLeft;
            panel.WrapContents = false;
            panel.Margin = new Padding(0, 5, 0, 0);
            return panel;
        }

        private static Button MakeButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(82, 30);
            button.Margin = new Padding(4, 3, 0, 3);
            return button;
        }

        private void Confirm()
        {
            string password = _first.Text;
            if (_setup)
            {
                if (password.Length < 4)
                {
                    _message.Text = "口令至少 4 位";
                    return;
                }
                if (password != _second.Text)
                {
                    _message.Text = "两次输入不一致";
                    return;
                }
                _config.SetPassword(password);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                if (_config.CheckPassword(password))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    _message.Text = "口令不正确";
                    _first.SelectAll();
                    _first.Focus();
                }
            }
        }
    }
}
