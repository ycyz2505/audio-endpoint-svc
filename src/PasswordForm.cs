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
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(340, _setup ? 200 : 160);
            BackColor = Color.White;

            Label title = new Label();
            title.Text = _setup ? "首次使用，请设置管理口令" : "请输入管理口令";
            title.AutoSize = true;
            title.Location = new Point(18, 16);
            Controls.Add(title);

            Label firstLabel = new Label();
            firstLabel.Text = _setup ? "新口令（至少 4 位）" : "口令";
            firstLabel.AutoSize = true;
            firstLabel.Location = new Point(18, 46);
            Controls.Add(firstLabel);

            _first = new TextBox();
            _first.UseSystemPasswordChar = true;
            _first.Location = new Point(18, 68);
            _first.Width = 300;
            Controls.Add(_first);

            if (_setup)
            {
                Label secondLabel = new Label();
                secondLabel.Text = "再次输入";
                secondLabel.AutoSize = true;
                secondLabel.Location = new Point(18, 98);
                Controls.Add(secondLabel);

                _second = new TextBox();
                _second.UseSystemPasswordChar = true;
                _second.Location = new Point(18, 120);
                _second.Width = 300;
                Controls.Add(_second);
            }

            _message = new Label();
            _message.ForeColor = Color.Firebrick;
            _message.AutoSize = true;
            _message.Location = new Point(18, _setup ? 148 : 98);
            Controls.Add(_message);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(150, _setup ? 168 : 120);
            ok.Width = 80;
            ok.Click += delegate { Confirm(); };
            Controls.Add(ok);
            AcceptButton = ok;

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(238, _setup ? 168 : 120);
            cancel.Width = 80;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;

            Shown += delegate { _first.Focus(); };
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
