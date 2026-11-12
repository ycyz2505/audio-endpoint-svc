using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    internal sealed class HiddenTaskbar : NativeWindow, IDisposable
    {
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int WM_CLOSE = 0x0010;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_CLOSE = 0xF060;
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        private readonly System.Drawing.Icon _icon;
        private bool _visible;

        public HiddenTaskbar(string title, System.Drawing.Icon icon)
        {
            _icon = icon;

            CreateParams cp = new CreateParams();
            cp.Caption = title;
            cp.X = -32000;
            cp.Y = -32000;
            cp.Width = 1;
            cp.Height = 1;
            cp.Style = WS_OVERLAPPEDWINDOW;
            cp.ExStyle = WS_EX_APPWINDOW | WS_EX_NOACTIVATE;
            cp.Parent = IntPtr.Zero;
            CreateHandle(cp);

            if (_icon != null && Handle != IntPtr.Zero)
            {
                SendMessage(Handle, WM_SETICON, (IntPtr)ICON_SMALL, _icon.Handle);
                SendMessage(Handle, WM_SETICON, (IntPtr)ICON_BIG, _icon.Handle);
            }
        }

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (Handle == IntPtr.Zero) return;
            ShowWindow(Handle, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLOSE) return;
            if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_CLOSE) return;
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try { DestroyHandle(); }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
