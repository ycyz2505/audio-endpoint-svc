using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Realtek 高清晰音频管理器")]
[assembly: AssemblyDescription("Realtek 高清晰音频管理器")]
[assembly: AssemblyProduct("Realtek 高清晰音频管理器")]
[assembly: AssemblyVersion("0.4.0.0")]
[assembly: AssemblyFileVersion("0.4.0.0")]

namespace AudioEndpointSvc
{
    internal static class Program
    {
        private const string MutexName = "Local\\audio_endpoint_svc_single_instance_v1";

        [STAThread]
        private static void Main()
        {
            bool createdNew = false;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew) return;

                CoInitializeEx(IntPtr.Zero, 2);
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    EnableIe11Emulation();

                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    ConfigStore config = new ConfigStore(baseDir);
                    Logger logger = new Logger(baseDir, config);
                    logger.CleanupOldLogs();

                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                    {
                        logger.AppendError("界面异常: " + e.Exception.Message);
                    };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                    {
                        Exception ex = e.ExceptionObject as Exception;
                        logger.AppendError("未处理异常: " + (ex != null ? ex.ToString() : "unknown"));
                    };

                    DeviceInventory devices = new DeviceInventory(logger);
                    CameraMonitor monitor = new CameraMonitor(config, logger, devices);
                    TrayContext context = new TrayContext(config, logger, monitor, devices);

                    monitor.Start();
                    devices.Start();
                    UpdateChecker.Start(logger, context.RequestExitForUpdate);

                    Application.Run(context);
                }
                finally
                {
                    CoUninitialize();
                }
            }
        }

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, int coInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        // 让内置 WebBrowser 以 IE11 模式渲染 HTML 界面。
        private static void EnableIe11Emulation()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    string exeName = Path.GetFileName(Application.ExecutablePath);
                    if (!string.IsNullOrEmpty(exeName)) key.SetValue(exeName, 11001, RegistryValueKind.DWord);
                }
            }
            catch { }
        }
    }
}
