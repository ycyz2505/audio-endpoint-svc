using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CamWatch
{
    internal sealed class HiddenTaskbar : NativeWindow, IDisposable
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const int WS_VISIBLE = 0x10000000;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int WM_CLOSE = 0x0010;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_CLOSE = 0xF060;
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        private static readonly Guid CLSID_TaskbarList = new Guid("56FDF344-FD6D-11D0-958A-006097C9A090");
        private static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        [ComImport]
        [Guid("56FDF342-FD6D-11D0-958A-006097C9A090")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
        }

        private readonly string _title;
        private readonly System.Drawing.Icon _icon;
        private ITaskbarList _taskbar;
        private bool _viaAddTab;
        private bool _visible;

        public HiddenTaskbar(string title, System.Drawing.Icon icon)
        {
            _title = title;
            _icon = icon;
            Create();
        }

        private void Create()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = _title;
            cp.X = -32000;
            cp.Y = -32000;
            cp.Width = 1;
            cp.Height = 1;
            cp.Style = WS_OVERLAPPEDWINDOW | WS_VISIBLE;
            cp.ExStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            cp.Parent = IntPtr.Zero;
            CreateHandle(cp);
            ApplyIcon();

            _viaAddTab = TryInitTaskbarList();
            if (!_viaAddTab) RecreateAsAppWindow();
        }

        private void ApplyIcon()
        {
            if (_icon != null && Handle != IntPtr.Zero)
            {
                SendMessage(Handle, WM_SETICON, (IntPtr)ICON_SMALL, _icon.Handle);
                SendMessage(Handle, WM_SETICON, (IntPtr)ICON_BIG, _icon.Handle);
            }
        }

        private bool TryInitTaskbarList()
        {
            try
            {
                _taskbar = (ITaskbarList)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_TaskbarList));
                _taskbar.HrInit();
                return true;
            }
            catch { return false; }
        }

        private void TryAddTab()
        {
            try
            {
                if (_taskbar != null && Handle != IntPtr.Zero) _taskbar.AddTab(Handle);
            }
            catch { }
        }

        private void TryDeleteTab()
        {
            try
            {
                if (_taskbar != null && Handle != IntPtr.Zero) _taskbar.DeleteTab(Handle);
            }
            catch { }
        }

        private void RecreateAsAppWindow()
        {
            try { DestroyHandle(); } catch { }
            CreateParams cp = new CreateParams();
            cp.Caption = _title;
            cp.X = -32000;
            cp.Y = -32000;
            cp.Width = 1;
            cp.Height = 1;
            cp.Style = WS_OVERLAPPEDWINDOW;
            cp.ExStyle = WS_EX_APPWINDOW | WS_EX_NOACTIVATE;
            cp.Parent = IntPtr.Zero;
            CreateHandle(cp);
            ApplyIcon();
        }

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (visible)
            {
                ShowWindow(Handle, SW_SHOWNOACTIVATE);
                if (_viaAddTab) TryAddTab();
            }
            else
            {
                if (_viaAddTab) TryDeleteTab();
                ShowWindow(Handle, SW_HIDE);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLOSE) return;
            if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_CLOSE) return;
            if (m.Msg == WM_TASKBARCREATED)
            {
                if (_visible && _viaAddTab) TryAddTab();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try { if (_viaAddTab) TryDeleteTab(); }
            catch { }
            try { DestroyHandle(); }
            catch { }
            _taskbar = null;
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);
    }
}
