using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    // 占用提示方式 3：右上角常显的传屏码浮层，样式对齐希沃传屏码截图：
    // 深色圆角、白字“传屏码：XXXXXX”，鼠标穿透。
    internal sealed class ScreenCodeOverlay : Form
    {
        private const int PadX = 22;
        private const int PadY = 14;
        private const int Radius = 10;

        private readonly Label _label;

        public ScreenCodeOverlay(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(45, 45, 45);
            Opacity = 0.94;

            _label = new Label();
            _label.AutoSize = true;
            _label.BackColor = Color.Transparent;
            _label.ForeColor = Color.White;
            _label.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular);
            _label.Text = text;
            Controls.Add(_label);

            LayoutOverlay();
        }

        public void SetText(string text)
        {
            if (_label.Text == text) return;
            _label.Text = text;
            LayoutOverlay();
        }

        public void ShowAtTopRight()
        {
            Reposition();
            if (!Visible) Show();
        }

        private void LayoutOverlay()
        {
            Size textSize = TextRenderer.MeasureText(_label.Text, _label.Font);
            int width = Math.Max(150, textSize.Width + PadX * 2);
            int height = Math.Max(44, textSize.Height + PadY * 2);
            ClientSize = new Size(width, height);
            _label.Location = new Point(PadX, (height - textSize.Height) / 2);

            using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, width, height), Radius))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null) oldRegion.Dispose();
            }
            Reposition();
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

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
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
