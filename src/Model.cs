using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace CrossMouse
{
    internal enum HotkeyKind
    {
        Keyboard = 0,
        Mouse = 1
    }

    internal enum LockScope
    {
        CurrentMonitor = 0,
        SpecificMonitor = 1
    }

    /// <summary>One physical monitor, coordinates are physical pixels.</summary>
    internal sealed class MonitorInfo
    {
        public IntPtr Handle;
        public string Device = "";
        public RECT Bounds;
        public bool Primary;

        public int Width { get { return Bounds.Width; } }
        public int Height { get { return Bounds.Height; } }

        public string ShortName
        {
            get
            {
                int n = 0;
                string d = Device == null ? "" : Device;
                string digits = "";
                for (int i = d.Length - 1; i >= 0; i--)
                {
                    if (char.IsDigit(d[i])) digits = d[i] + digits;
                    else break;
                }
                if (digits.Length > 0) int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                return n > 0 ? "显示器 " + n.ToString(CultureInfo.InvariantCulture) : "显示器";
            }
        }

        public string DisplayName
        {
            get
            {
                return ShortName
                    + (Primary ? "（主屏）" : "")
                    + "  " + Width.ToString(CultureInfo.InvariantCulture)
                    + " × " + Height.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static List<MonitorInfo> All()
        {
            List<MonitorInfo> list = new List<MonitorInfo>();
            Native.MonitorEnumProc proc = delegate(IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                MONITORINFOEX mi = new MONITORINFOEX();
                mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFOEX));
                if (Native.GetMonitorInfo(hMon, ref mi))
                {
                    MonitorInfo info = new MonitorInfo();
                    info.Handle = hMon;
                    info.Device = mi.szDevice;
                    info.Bounds = mi.rcMonitor;
                    info.Primary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
                    list.Add(info);
                }
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            list.Sort(delegate(MonitorInfo a, MonitorInfo b) { return a.Bounds.Left.CompareTo(b.Bounds.Left); });
            return list;
        }

        public static MonitorInfo FromPoint(System.Drawing.Point pt)
        {
            POINT p;
            p.X = pt.X;
            p.Y = pt.Y;
            IntPtr h = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
            foreach (MonitorInfo m in All())
            {
                if (m.Handle == h) return m;
            }
            List<MonitorInfo> all = All();
            return all.Count > 0 ? all[0] : null;
        }

        public static RECT VirtualScreen()
        {
            RECT r;
            r.Left = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            r.Top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            r.Right = r.Left + Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            r.Bottom = r.Top + Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            return r;
        }
    }

    /// <summary>Persisted user preferences (HKCU\Software\CrossMouse).</summary>
    internal sealed class Settings
    {
        private const string KeyPath = @"Software\CrossMouse";

        public HotkeyKind Kind = HotkeyKind.Keyboard;
        public int Code = 0xA3; // VK_RCONTROL
        public LockScope Scope = LockScope.CurrentMonitor;
        public string MonitorDevice = "";
        public bool Enabled = true;
        public bool RunAtStartup;

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                {
                    if (k == null) return s;
                    s.Kind = ReadInt(k, "Kind", (int)s.Kind) == 1 ? HotkeyKind.Mouse : HotkeyKind.Keyboard;
                    s.Code = ReadInt(k, "Code", s.Code);
                    s.Scope = ReadInt(k, "Scope", (int)s.Scope) == 1 ? LockScope.SpecificMonitor : LockScope.CurrentMonitor;
                    s.MonitorDevice = ReadString(k, "Monitor", s.MonitorDevice);
                    s.Enabled = ReadInt(k, "Enabled", 1) != 0;
                }
            }
            catch (Exception)
            {
                // A broken registry entry should never prevent the app from starting.
            }
            s.RunAtStartup = Startup.IsEnabled();
            return s;
        }

        public void Save()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (k == null) return;
                    k.SetValue("Kind", (int)Kind, RegistryValueKind.DWord);
                    k.SetValue("Code", Code, RegistryValueKind.DWord);
                    k.SetValue("Scope", (int)Scope, RegistryValueKind.DWord);
                    k.SetValue("Monitor", MonitorDevice ?? "", RegistryValueKind.String);
                    k.SetValue("Enabled", Enabled ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception)
            {
            }
        }

        private static int ReadInt(RegistryKey k, string name, int fallback)
        {
            object v = k.GetValue(name, null);
            if (v == null) return fallback;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        private static string ReadString(RegistryKey k, string name, string fallback)
        {
            object v = k.GetValue(name, null);
            return v == null ? fallback : Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>HKCU ...\Run entry so the app comes back after logon.</summary>
    internal static class Startup
    {
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CrossMouse";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunPath, false))
                {
                    if (k == null) return false;
                    string v = k.GetValue(ValueName, null) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Set(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunPath))
                {
                    if (k == null) return;
                    if (on)
                    {
                        string exe = System.Windows.Forms.Application.ExecutablePath;
                        k.SetValue(ValueName, "\"" + exe + "\" --tray", RegistryValueKind.String);
                    }
                    else
                    {
                        k.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>Turns virtual-key codes into something a human can read.</summary>
    internal static class KeyNames
    {
        public static string Name(HotkeyKind kind, int code)
        {
            if (kind == HotkeyKind.Mouse) return MouseName(code);

            switch (code)
            {
                case 0x08: return "退格";
                case 0x09: return "Tab";
                case 0x0D: return "回车";
                case 0x13: return "Pause";
                case 0x14: return "大写锁定";
                case 0x1B: return "Esc";
                case 0x20: return "空格";
                case 0x21: return "Page Up";
                case 0x22: return "Page Down";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "←";
                case 0x26: return "↑";
                case 0x27: return "→";
                case 0x28: return "↓";
                case 0x2C: return "Print Screen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x5B: return "左 Win";
                case 0x5C: return "右 Win";
                case 0x5D: return "菜单键";
                case 0x90: return "Num Lock";
                case 0x91: return "Scroll Lock";
                case 0xA0: return "左 Shift";
                case 0xA1: return "右 Shift";
                case 0xA2: return "左 Ctrl";
                case 0xA3: return "右 Ctrl";
                case 0xA4: return "左 Alt";
                case 0xA5: return "右 Alt";
                case 0x10: return "Shift";
                case 0x11: return "Ctrl";
                case 0x12: return "Alt";
                case 0x6A: return "小键盘 *";
                case 0x6B: return "小键盘 +";
                case 0x6D: return "小键盘 -";
                case 0x6E: return "小键盘 .";
                case 0x6F: return "小键盘 /";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
            }

            if (code >= 0x30 && code <= 0x39) return ((char)code).ToString();
            if (code >= 0x41 && code <= 0x5A) return ((char)code).ToString();
            if (code >= 0x60 && code <= 0x69) return "小键盘 " + (code - 0x60).ToString(CultureInfo.InvariantCulture);
            if (code >= 0x70 && code <= 0x87) return "F" + (code - 0x6F).ToString(CultureInfo.InvariantCulture);

            return "按键 " + code.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static string MouseName(int code)
        {
            switch (code)
            {
                case Native.VK_MBUTTON: return "鼠标中键";
                case Native.VK_XBUTTON1: return "鼠标侧键 4";
                case Native.VK_XBUTTON2: return "鼠标侧键 5";
                default: return "鼠标键 " + code.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
