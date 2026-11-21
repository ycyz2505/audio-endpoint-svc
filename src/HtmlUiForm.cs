using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace AudioEndpointSvc
{
    // HTML 前端容器：使用系统自带 WebBrowser（IE11 内核），无需安装额外运行时。
    internal sealed class HtmlUiForm : Form
    {
        private readonly WebBrowser _browser;
        private readonly UiBridge _bridge;

        public HtmlUiForm(ConfigStore config, Logger logger, CameraMonitor monitor, DeviceInventory devices, Action onExit)
        {
            Text = config.Value.DisguiseName;
            Icon = IconLoader.LoadDisguiseIcon();
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1120, 720);
            MinimumSize = new Size(900, 600);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.White;

            _bridge = new UiBridge(this, config, logger, monitor, devices, onExit);

            _browser = new WebBrowser();
            _browser.Dock = DockStyle.Fill;
            _browser.ScriptErrorsSuppressed = true;
            _browser.AllowWebBrowserDrop = false;
            _browser.IsWebBrowserContextMenuEnabled = false;
            _browser.ObjectForScripting = _bridge;
            _browser.DocumentText = LoadHtml();
            Controls.Add(_browser);
        }

        public string ShowCsvSaveDialog()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV 文件 (*.csv)|*.csv";
                dialog.FileName = "摄像头使用记录_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private static string LoadHtml()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("AudioEndpointSvc.ui.html"))
                {
                    if (stream == null) return "<html><body style=\"font-family:Microsoft YaHei UI;padding:24px\">界面资源缺失，请重新构建。</body></html>";
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                return "<html><body style=\"font-family:Microsoft YaHei UI;padding:24px\">界面加载失败：" + ex.Message + "</body></html>";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            try
            {
                if (_browser != null && _browser.Document != null)
                    _browser.Document.InvokeScript("showDecoy");
            }
            catch { }
        }
    }
}
