using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CamWatch
{
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly ConfigStore _config;
        private readonly Logger _logger;
        private readonly CameraMonitor _monitor;
        private readonly DeviceInventory _devices;
        private readonly Icon _icon;
        private readonly HiddenTaskbar _taskbar;
        private readonly NotifyIcon _notify;
        private readonly Timer _tick;
        private MainForm _mainForm;

        public TrayContext(ConfigStore config, Logger logger, CameraMonitor monitor, DeviceInventory devices)
        {
            _config = config;
            _logger = logger;
            _monitor = monitor;
            _devices = devices;

            _icon = IconLoader.LoadDisguiseIcon();
            _taskbar = new HiddenTaskbar(_config.Value.DisguiseName, _icon);

            _notify = new NotifyIcon();
            _notify.Icon = _icon;
            _notify.Text = _config.Value.DisguiseName;
            _notify.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem item = new ToolStripMenuItem("进入主菜单");
            item.Click += delegate { OpenMainMenu(); };
            menu.Items.Add(item);
            _notify.ContextMenuStrip = menu;

            _tick = new Timer();
            _tick.Interval = 1000;
            _tick.Tick += delegate { Evaluate(); };
            _tick.Start();
        }

        private void Evaluate()
        {
            try
            {
                _taskbar.SetVisible(ShouldShow(_monitor.GetActiveSnapshot()));
            }
            catch { }
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
                using (PasswordForm dialog = new PasswordForm(_config))
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return;
                }
                _mainForm = new MainForm(_config, _logger, _monitor, _devices, delegate { Application.Exit(); });
                _mainForm.FormClosed += delegate { _mainForm = null; };
                _mainForm.Show();
                _mainForm.Activate();
            }
            catch (Exception ex)
            {
                _logger.AppendError("打开主菜单失败: " + ex.Message);
            }
        }

        protected override void ExitThreadCore()
        {
            try { _tick.Stop(); } catch { }
            try { _notify.Visible = false; _notify.Dispose(); } catch { }
            try { _taskbar.Dispose(); } catch { }
            base.ExitThreadCore();
        }
    }
}
