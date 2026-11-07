using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("设备使用记录")]
[assembly: AssemblyDescription("摄像头占用记录")]
[assembly: AssemblyProduct("设备使用记录")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace CamWatch
{
    internal static class Program
    {
        private const string MutexName = "Local\\camwatch_single_instance_v1";

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
    }
}
