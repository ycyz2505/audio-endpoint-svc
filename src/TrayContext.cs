using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly ConfigStore _config;
        private readonly Logger _logger;
        private readonly CameraMonitor _monitor;
        private readonly DeviceInventory _devices;
        private readonly Icon _normalIcon;
        private readonly Icon _activeIcon;
        private readonly NotifyIcon _notify;
        private readonly LeftTaskbarIndicator _leftIndicator;
        private readonly Control _dispatch;
        private readonly Timer _tick;
        private readonly string _overlayCode;
        private ScreenCodeOverlay _overlay;
        private HtmlUiForm _mainForm;
        private bool _trayActive;

        public TrayContext(ConfigStore config, Logger logger, CameraMonitor monitor, DeviceInventory devices)
        {
            _config = config;
            _logger = logger;
            _monitor = monitor;
            _devices = devices;
            _overlayCode = GenerateScreenCode();

            _dispatch = new Control();
            _dispatch.CreateControl();

            _normalIcon = IconLoader.LoadDisguiseIcon();
            _activeIcon = IconLoader.CreateOccupiedIcon(_normalIcon);
            _leftIndicator = new LeftTaskbarIndicator(_config.Value.DisguiseName, _normalIcon);

            // 全程只保留这一个托盘图标；占用时按显示方式决定是否换图标。
            _notify = new NotifyIcon();
            _notify.Icon = _normalIcon;
            _notify.Text = _config.Value.DisguiseName;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem item = new ToolStripMenuItem("进入主菜单");
            item.Click += delegate { OpenMainMenu(); };
            menu.Items.Add(item);
            _notify.ContextMenuStrip = menu;
            _notify.Visible = true;

            _tick = new Timer();
            _tick.Interval = 1000;
            _tick.Tick += delegate { Evaluate(); };
            _tick.Start();
        }

        private void Evaluate()
        {
            try
            {
                bool occupied = ShouldShow(_monitor.GetActiveSnapshot());
                string mode = _config.Value.DisplayMode;

                _leftIndicator.SetVisible(occupied && mode == "appbutton");

                bool trayActive = occupied && mode == "tray";
                if (_trayActive != trayActive)
                {
                    _trayActive = trayActive;
                    _notify.Icon = trayActive ? _activeIcon : _normalIcon;
                }

                if (occupied && mode == "overlay") ShowOverlay();
                else if (_overlay != null && !_overlay.IsDisposed && _overlay.Visible) _overlay.Hide();
            }
            catch { }
        }

        private void ShowOverlay()
        {
            string text = OverlayText;
            if (_overlay == null || _overlay.IsDisposed)
            {
                _overlay = new ScreenCodeOverlay(text, _config.Value.OverlayX, delegate(int x)
                {
                    _config.Value.OverlayX = x;
                    _config.Save();
                });
            }
            else
            {
                _overlay.SetText(text);
            }
            _overlay.ShowAtTop();
        }

        private string OverlayText
        {
            get
            {
                string custom = _config.Value.OverlayText;
                if (!string.IsNullOrEmpty(custom)) return custom;
                return "传屏码：" + _overlayCode;
            }
        }

        private static string GenerateScreenCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            byte[] bytes = new byte[6];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            StringBuilder builder = new StringBuilder(6);
            for (int i = 0; i < bytes.Length; i++) builder.Append(chars[bytes[i] % chars.Length]);
            return builder.ToString();
        }

        private bool ShouldShow(List<ActiveInfo> active)
        {
            if (active == null || active.Count == 0) return false;
            string mode = _config.Value.TaskbarMode;
            List<string> apps = _config.Value.TaskbarApps;
            if (mode == "only")
            {
                for (int i = 0; i < active.Count; i++)
                {
                    if (Matches(active[i], apps)) return true;
                }
                return false;
            }
            if (mode == "exclude")
            {
                for (int i = 0; i < active.Count; i++)
                {
                    if (!Matches(active[i], apps)) return true;
                }
                return false;
            }
            return true;
        }

        private static bool Matches(ActiveInfo info, List<string> apps)
        {
            if (apps == null) return false;
            string token = info.Identity;
            for (int i = 0; i < apps.Count; i++)
            {
                if (string.Equals(apps[i], token, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public void OpenMainMenu()
        {
            try
            {
                if (_mainForm != null && !_mainForm.IsDisposed)
                {
                    _mainForm.Show();
                    _mainForm.WindowState = FormWindowState.Normal;
                    _mainForm.Activate();
                    return;
                }
                _mainForm = new HtmlUiForm(_config, _logger, _monitor, _devices, delegate { Application.Exit(); });
                _mainForm.FormClosed += delegate { _mainForm = null; };
                _mainForm.Show();
                _mainForm.Activate();
            }
            catch (Exception ex)
            {
                _logger.AppendError("打开主菜单失败: " + ex.Message);
            }
        }

        public void RequestExitForUpdate()
        {
            try
            {
                if (_dispatch != null && _dispatch.IsHandleCreated)
                {
                    _dispatch.BeginInvoke((MethodInvoker)delegate { Application.Exit(); });
                    return;
                }
            }
            catch { }
            Environment.Exit(0);
        }

        protected override void ExitThreadCore()
        {
            try { _tick.Stop(); } catch { }
            try { if (_overlay != null) { _overlay.Hide(); _overlay.Dispose(); } } catch { }
            try { _leftIndicator.Dispose(); } catch { }
            try { _notify.Visible = false; _notify.Dispose(); } catch { }
            try { _dispatch.Dispose(); } catch { }
            try { if (_activeIcon != null) _activeIcon.Dispose(); } catch { }
            try { if (_normalIcon != null) _normalIcon.Dispose(); } catch { }
            base.ExitThreadCore();
        }
    }
}
