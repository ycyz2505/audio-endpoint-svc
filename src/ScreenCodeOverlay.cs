using System;
using System.Drawing;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    // 占用提示方式 3：右上角常显的传屏码样式浮层（当前为占位样式，待效果图后调整）。
    internal sealed class ScreenCodeOverlay : Form
    {
        private readonly Label _label;

        public ScreenCodeOverlay(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.FromArgb(32, 32, 32);
            Padding = new Padding(16, 10, 16, 10);

            _label = new Label();
            _label.AutoSize = true;
            _label.ForeColor = Color.White;
            _label.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            _label.Margin = new Padding(0);
            _label.Text = text;
            Controls.Add(_label);
        }

        public void SetText(string text)
        {
            _label.Text = text;
            Reposition();
        }

        public void ShowAtTopRight()
        {
            Reposition();
            if (!Visible) Show();
        }

        private void Reposition()
        {
            Screen screen = Screen.PrimaryScreen;
            if (screen == null) return;
            Rectangle area = screen.WorkingArea;
            Location = new Point(area.Right - Width - 18, area.Top + 18);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Reposition();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT（鼠标穿透）
                return cp;
            }
        }
    }
}
