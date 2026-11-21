using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CrossMouse
{
    internal static class Program
    {
        private const string MutexName = "Local\\CrossMouse.SingleInstance";
        private const string WindowTitle = "CrossMouse 设置";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private const string Usage =
            "CrossMouse - 防止鼠标指针在按住快捷键之前跨越屏幕\n\n" +
            "用法:\n" +
            "  CrossMouse.exe              启动并显示设置窗口\n" +
            "  CrossMouse.exe --tray       静默启动，只驻留托盘（开机自启使用）\n" +
            "  CrossMouse.exe --quiet      同上\n" +
            "  CrossMouse.exe --icon <目录>  导出图标文件后退出（构建用）\n" +
            "  CrossMouse.exe --help       显示本帮助\n";

        [STAThread]
        private static void Main(string[] args)
        {
            // Special mode: the watchdog is a bare process that only waits and heals.
            if (args.Length >= 2 && string.Equals(args[0], "--watchdog", StringComparison.OrdinalIgnoreCase))
            {
                int parentPid;
                if (int.TryParse(args[1], out parentPid)) Environment.Exit(Watchdog.Run(parentPid));
                return;
            }

            bool silent = false;
            foreach (string raw in args)
            {
                string a = (raw ?? "").Trim().ToLowerInvariant();
                if (a == "--help" || a == "-h" || a == "/?")
                {
                    MessageBox(IntPtr.Zero, Usage, "CrossMouse", 0x40);
                    return;
                }
                if (a == "--tray" || a == "--quiet" || a == "--silent") silent = true;
            }

            // Build helper: render the icon set without starting the UI.
            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").ToLowerInvariant();
                if (a == "--icon" && i + 1 < args.Length)
                {
                    string dir = args[i + 1];
                    Directory.CreateDirectory(dir);
                    string ico = Path.Combine(dir, "CrossMouse.ico");
                    IconArt.WriteIco(ico, new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 });
                    using (Bitmap big = IconArt.Render(512, false))
                    {
                        big.Save(Path.Combine(dir, "icon-512.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Console.WriteLine(ico);
                    return;
                }
            }

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    ShowExistingInstance();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Report(e.Exception);
                };

                Settings settings = Settings.Load();
                using (TrayApp app = new TrayApp(settings))
                {
                    if (!silent)
                    {
                        app.ShowSettings();
                    }
                    Application.Run();
                }
                GC.KeepAlive(mutex);            }
        }

        private static void ShowExistingInstance()
        {
            IntPtr hwnd = FindWindow(null, WindowTitle);
            if (hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
            }
            else
            {
                MessageBox(IntPtr.Zero,
                    "CrossMouse 已在运行，请在任务栏通知区域（托盘）中双击图标打开设置。",
                    "CrossMouse", 0x40);
            }
        }

        private static void Report(Exception ex)
        {
            try
            {
                string log = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CrossMouse");
                Directory.CreateDirectory(log);
                File.AppendAllText(Path.Combine(log, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }
    }
}
