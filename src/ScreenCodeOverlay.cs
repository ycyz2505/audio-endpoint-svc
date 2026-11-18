using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    // 占用提示方式 3：右上角常显的传屏码浮层，样式对齐希沃传屏码截图：
    // 深蓝灰圆角、浅灰白字“传屏码：XXXXXX”，右下角折角，鼠标穿透。
    internal sealed class ScreenCodeOverlay : Form
    {
        private const int PadX = 16;
        private const int PadY = 4;
        private const int Radius = 8;
        private static readonly Color BoxColor = Color.FromArgb(48, 57, 66);
        private static readonly Color TextColor = Color.FromArgb(200, 201, 202);
        private static readonly Color FoldColor = Color.FromArgb(199, 199, 200);

        private readonly Label _label;

        public ScreenCodeOverlay(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = BoxColor;

            _label = new Label();
            _label.AutoSize = true;
            _label.BackColor = Color.Transparent;
            _label.ForeColor = TextColor;
            _label.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Regular);
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
            float scale = DeviceDpi / 96f;
            Size textSize = TextRenderer.MeasureText(_label.Text, _label.Font);
            int padX = (int)Math.Round(PadX * scale);
            int padY = (int)Math.Round(PadY * scale);
            int width = Math.Max(textSize.Width + padX * 2, (int)Math.Round(186 * scale));
            int height = Math.Max(textSize.Height + padY * 2, (int)Math.Round(35 * scale));
            ClientSize = new Size(width, height);
            _label.Location = new Point(padX, (height - textSize.Height) / 2);

            using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, width, height), (int)Math.Round(Radius * scale)))
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

        // 右下角折角：一个约 6x8 像素的浅灰直角三角形，距右/下边 1-2 像素。
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = DeviceDpi / 96f;
            float right = Width - 2 * scale;
            float bottom = Height - 1 * scale;
            float left = right - 6 * scale;
            float top = bottom - 8 * scale;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(FoldColor))
            {
                e.Graphics.FillPolygon(brush, new PointF[]
                {
                    new PointF(left, bottom),
                    new PointF(right, bottom),
                    new PointF(right, top)
                });
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
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
