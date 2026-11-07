using System;
using System.Drawing;
using System.Windows.Forms;

namespace CamWatch
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
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(340, 250);
            BackColor = Color.White;

            Label currentLabel = new Label();
            currentLabel.Text = "当前口令";
            currentLabel.AutoSize = true;
            currentLabel.Location = new Point(18, 16);
            Controls.Add(currentLabel);

            _current = new TextBox();
            _current.UseSystemPasswordChar = true;
            _current.Location = new Point(18, 38);
            _current.Width = 300;
            Controls.Add(_current);

            Label nextLabel = new Label();
            nextLabel.Text = "新口令（至少 4 位）";
            nextLabel.AutoSize = true;
            nextLabel.Location = new Point(18, 70);
            Controls.Add(nextLabel);

            _next = new TextBox();
            _next.UseSystemPasswordChar = true;
            _next.Location = new Point(18, 92);
            _next.Width = 300;
            Controls.Add(_next);

            Label confirmLabel = new Label();
            confirmLabel.Text = "再次输入新口令";
            confirmLabel.AutoSize = true;
            confirmLabel.Location = new Point(18, 124);
            Controls.Add(confirmLabel);

            _confirm = new TextBox();
            _confirm.UseSystemPasswordChar = true;
            _confirm.Location = new Point(18, 146);
            _confirm.Width = 300;
            Controls.Add(_confirm);

            _message = new Label();
            _message.ForeColor = Color.Firebrick;
            _message.AutoSize = true;
            _message.Location = new Point(18, 178);
            Controls.Add(_message);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(150, 208);
            ok.Width = 80;
            ok.Click += delegate { Confirm(); };
            Controls.Add(ok);
            AcceptButton = ok;

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(238, 208);
            cancel.Width = 80;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;

            Shown += delegate { _current.Focus(); };
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
