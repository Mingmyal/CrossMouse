using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CrossMouse
{
    /// <summary>
    /// Keeps the system cursor confinement from outliving the app.
    ///
    /// ClipCursor installs a clip that belongs to the desktop, not to the process that
    /// set it: if CrossMouse is killed (Task Manager, a debugger, a crash inside a native
    /// hook) the clip stays in force and the pointer is trapped on one screen with no way
    /// out. A watchdog process waits for this process to exit and then clears the clip.
    ///
    /// The watchdog runs from a *separate copy* of the executable with a different file
    /// name, so that "End task" style bulk kills targeting CrossMouse.exe cannot take the
    /// guard down along with the app it is guarding.
    /// </summary>
    internal static class Watchdog
    {
        private const int SYNCHRONIZE = 0x00100000;
        private const uint INFINITE = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr lpRect);

        private static Process _child;

        /// <summary>Path of the watchdog's own copy of the executable.</summary>
        private static string HelperPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CrossMouse");
                return Path.Combine(dir, "CrossMouseWatchdog.exe");
            }
        }

        /// <summary>Starts the watcher for the current process.</summary>
        public static void Start()
        {
            try
            {
                string self = System.Windows.Forms.Application.ExecutablePath;
                string helper = HelperPath;

                Directory.CreateDirectory(Path.GetDirectoryName(helper));
                bool needCopy = true;
                FileInfo target = new FileInfo(helper);
                FileInfo source = new FileInfo(self);
                needCopy = !target.Exists || target.Length != source.Length || target.LastWriteTimeUtc != source.LastWriteTimeUtc;
                if (needCopy && !string.Equals(Path.GetFullPath(self), Path.GetFullPath(helper), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(self, helper, true);
                    File.SetLastWriteTimeUtc(helper, source.LastWriteTimeUtc);
                }

                string exe = File.Exists(helper) ? helper : self;
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = "--watchdog " + Process.GetCurrentProcess().Id;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
                _child = Process.Start(psi);
            }
            catch (Exception)
            {
                _child = null;
            }
        }

        /// <summary>True when the watcher is alive; restarted by the tray timer when not.</summary>
        public static bool IsRunning
        {
            get
            {
                try { return _child != null && !_child.HasExited; }
                catch (Exception) { return false; }
            }
        }

        public static void EnsureRunning()
        {
            if (!IsRunning) Start();
        }

        /// <summary>Stops the watcher once the clip has been cleared normally.</summary>
        public static void Stop()
        {
            try
            {
                if (_child != null && !_child.HasExited)
                {
                    _child.Kill();
                    _child.WaitForExit(1000);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                try { if (_child != null) _child.Dispose(); }
                catch (Exception) { }
                _child = null;
            }
        }

        /// <summary>
        /// Watchdog entry point. Blocks until the guarded process is gone, then clears
        /// whatever clip it may have left behind.
        /// </summary>
        public static int Run(int parentPid)
        {
            IntPtr parent = IntPtr.Zero;
            try
            {
                parent = OpenProcess(SYNCHRONIZE, false, parentPid);
                if (parent == IntPtr.Zero) return 0; // parent already gone
                WaitForSingleObject(parent, INFINITE);
            }
            catch (Exception)
            {
            }
            finally
            {
                if (parent != IntPtr.Zero)
                {
                    try { CloseHandle(parent); }
                    catch (Exception) { }
                }
            }

            // Let the exiting process release the clip itself first.
            Thread.Sleep(150);
            try { ClipCursor(IntPtr.Zero); }
            catch (Exception) { }
            return 0;
        }
    }
}
