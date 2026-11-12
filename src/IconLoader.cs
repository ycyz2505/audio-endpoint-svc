using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace AudioEndpointSvc
{
    internal static class IconLoader
    {
        public static Icon LoadDisguiseIcon()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Realtek\Audio\HDA\RtkNGUI64.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Realtek\Audio\HDA\RtkNGUI.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Realtek\Audio\HDA\RtkNGUI64.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Realtek\Audio\HDA\RtkNGUI.exe")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                IntPtr handle = TryExtractIcon(candidates[i], 0);
                if (handle != IntPtr.Zero) return FromHandle(handle);
            }

            string sndVol = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SndVolSSO.dll");
            IntPtr h = TryExtractIcon(sndVol, 0);
            if (h != IntPtr.Zero) return FromHandle(h);

            return DrawFallback();
        }

        private static IntPtr TryExtractIcon(string path, int index)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return IntPtr.Zero;
                IntPtr[] large = new IntPtr[1];
                IntPtr[] small = new IntPtr[1];
                uint count = ExtractIconEx(path, index, large, small, 1);
                if (count > 0 && small[0] != IntPtr.Zero)
                {
                    if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
                    return small[0];
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        private static Icon FromHandle(IntPtr handle)
        {
            using (Icon temp = Icon.FromHandle(handle))
            {
                Icon clone = (Icon)temp.Clone();
                DestroyIcon(handle);
                return clone;
            }
        }

        private static Icon DrawFallback()
        {
            Bitmap bitmap = new Bitmap(16, 16);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(235, 235, 235)))
                    using (Pen pen = new Pen(Color.FromArgb(235, 235, 235), 1.2f))
                    {
                        graphics.FillPolygon(brush, new Point[]
                        {
                            new Point(2, 6), new Point(5, 6), new Point(9, 2), new Point(9, 14), new Point(5, 10), new Point(2, 10)
                        });
                        graphics.DrawArc(pen, 8, 4, 6, 8, -60, 120);
                        graphics.DrawArc(pen, 6, 2, 10, 12, -60, 120);
                    }
                }
                IntPtr handle = bitmap.GetHicon();
                using (Icon temp = Icon.FromHandle(handle))
                {
                    Icon clone = (Icon)temp.Clone();
                    DestroyIcon(handle);
                    return clone;
                }
            }
            finally
            {
                bitmap.Dispose();
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string fileName, int iconIndex, IntPtr[] largeIcons, IntPtr[] smallIcons, uint iconCount);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);
    }
}
