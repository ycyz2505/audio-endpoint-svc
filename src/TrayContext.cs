using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly ConfigStore _config;
        private readonly Logger _logger;
        private readonly CameraMonitor _monitor;
        private readonly DeviceInventory _devices;
        private readonly Icon _icon;
        private readonly NotifyIcon _occupancyIcon;
        private readonly NotifyIcon _entryIcon;
        private readonly Control _dispatch;
        private readonly Timer _tick;
        private MainForm _mainForm;

        public TrayContext(ConfigStore config, Logger logger, CameraMonitor monitor, DeviceInventory devices)
        {
            _config = config;
            _logger = logger;
            _monitor = monitor;
            _devices = devices;

            _dispatch = new Control();
            _dispatch.CreateControl();

            _icon = IconLoader.LoadDisguiseIcon();

            // 常驻的功能入口图标。
            _entryIcon = new NotifyIcon();
            _entryIcon.Icon = _icon;
            _entryIcon.Text = _config.Value.DisguiseName;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem item = new ToolStripMenuItem("进入主菜单");
            item.Click += delegate { OpenMainMenu(); };
            menu.Items.Add(item);
            _entryIcon.ContextMenuStrip = menu;
            _entryIcon.Visible = true;

            // 独立的占用指示图标：位于任务栏右侧通知区域，仅命中显示规则时出现。
            // 不挂菜单、不响应点击，避免它和常驻入口混为同一个功能入口。
            _occupancyIcon = new NotifyIcon();
            _occupancyIcon.Icon = _icon;
            _occupancyIcon.Text = _config.Value.DisguiseName;
            _occupancyIcon.Visible = false;

            _tick = new Timer();
            _tick.Interval = 1000;
            _tick.Tick += delegate { Evaluate(); };
            _tick.Start();
        }

        private void Evaluate()
        {
            try
            {
                bool visible = ShouldShow(_monitor.GetActiveSnapshot());
                if (_occupancyIcon.Visible != visible) _occupancyIcon.Visible = visible;
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
            try { _occupancyIcon.Visible = false; _occupancyIcon.Dispose(); } catch { }
            try { _entryIcon.Visible = false; _entryIcon.Dispose(); } catch { }
            try { _dispatch.Dispose(); } catch { }
            try { _icon.Dispose(); } catch { }
            base.ExitThreadCore();
        }
    }
}
