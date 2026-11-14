using System;
using System.Drawing;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    internal sealed class ChangePasswordForm : Form
    {
        private readonly ConfigStore _config;
        private readonly TextBox _current;
        private readonly TextBox _next;
        private readonly TextBox _confirm;
        private readonly Label _message;

        public ChangePasswordForm(ConfigStore config)
        {
            _config = config;

            Text = "修改口令";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
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
            root.RowCount = 8;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            Controls.Add(root);

            root.Controls.Add(MakeLabel("当前口令"));
            _current = MakeTextBox();
            root.Controls.Add(_current);
            root.Controls.Add(MakeLabel("新口令（至少 4 位）"));
            _next = MakeTextBox();
            root.Controls.Add(_next);
            root.Controls.Add(MakeLabel("再次输入新口令"));
            _confirm = MakeTextBox();
            root.Controls.Add(_confirm);

            _message = MakeLabel(" ");
            _message.ForeColor = Color.Firebrick;
            _message.MinimumSize = new Size(0, 22);
            root.Controls.Add(_message);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.Margin = new Padding(0, 5, 0, 0);
            Button cancel = MakeButton("取消");
            cancel.DialogResult = DialogResult.Cancel;
            Button ok = MakeButton("确定");
            ok.Click += delegate { Confirm(); };
            actions.Controls.Add(cancel);
            actions.Controls.Add(ok);
            root.Controls.Add(actions);
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += delegate { _current.Focus(); };
        }

        private static Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Dock = DockStyle.Fill;
            label.Margin = new Padding(0, 5, 0, 4);
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
            if (!_config.CheckPassword(_current.Text))
            {
                _message.Text = "当前口令不正确";
                _current.SelectAll();
                _current.Focus();
                return;
            }
            if (_next.Text.Length < 4)
            {
                _message.Text = "新口令至少 4 位";
                return;
            }
            if (_next.Text != _confirm.Text)
            {
                _message.Text = "两次输入不一致";
                return;
            }
            _config.SetPassword(_next.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
