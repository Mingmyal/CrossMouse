using System;
using System.Runtime.InteropServices;

namespace CrossMouse
{
    /// <summary>
    /// Low level keyboard / mouse hooks. They let the app react to the hotkey
    /// within a millisecond instead of waiting for the polling timer, and they
    /// also double as the "press a key to choose it" capture used by the settings UI.
    /// </summary>
    internal sealed class HotkeyHooks : IDisposable
    {
        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;
        private Native.LowLevelProc _kbProc;
        private Native.LowLevelProc _mouseProc;
        private DateTime _lastMove = DateTime.MinValue;
        private bool _disposed;

        /// <summary>Raised when the held state of the watch key may have changed.</summary>
        public event Action StateChanged;

        /// <summary>While true, the next key / mouse button press is reported instead of acted on.</summary>
        public event Action<HotkeyKind, int> Captured;

        public bool CaptureMode;

        public bool Installed { get { return _kbHook != IntPtr.Zero && _mouseHook != IntPtr.Zero; } }

        public bool Install()
        {
            if (_disposed) return false;
            IntPtr module = Native.GetModuleHandle(null);
            if (_kbHook == IntPtr.Zero)
            {
                _kbProc = KeyboardProc;
                _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, module, 0);
            }
            if (_mouseHook == IntPtr.Zero)
            {
                _mouseProc = MouseProc;
                _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, module, 0);
            }
            return Installed;
        }

        private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= Native.HC_ACTION)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                {
                    int vk;
                    try { vk = (int)Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).vkCode; }
                    catch (Exception) { vk = 0; }

                    if (CaptureMode && vk != 0)
                    {
                        Action<HotkeyKind, int> captured = Captured;
                        if (captured != null) captured(HotkeyKind.Keyboard, vk);
                        return (IntPtr)1; // swallow while capturing
                    }

                    RaiseChanged();
                }
                else if (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP)
                {
                    RaiseChanged();
                }
            }
            return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= Native.HC_ACTION)
            {
                int msg = wParam.ToInt32();
                if (CaptureMode)
                {
                    int code = 0;
                    if (msg == Native.WM_MBUTTONDOWN) code = Native.VK_MBUTTON;
                    else if (msg == Native.WM_XBUTTONDOWN)
                    {
                        MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        int which = (int)((data.mouseData >> 16) & 0xFFFF);
                        code = which == 2 ? Native.VK_XBUTTON2 : Native.VK_XBUTTON1;
                    }
                    if (code != 0)
                    {
                        Action<HotkeyKind, int> captured = Captured;
                        if (captured != null) captured(HotkeyKind.Mouse, code);
                        return (IntPtr)1;
                    }
                }

                DateTime now = DateTime.UtcNow;
                if (msg == Native.WM_MOUSEMOVE)
                {
                    // Mouse moves fire hundreds of times per second; the guard only
                    // needs a nudge when the hotkey is held or the clip needs a check.
                    if ((now - _lastMove).TotalMilliseconds < 30) return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                    _lastMove = now;
                }
                RaiseChanged();
            }
            return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void RaiseChanged()
        {
            Action handler = StateChanged;
            if (handler == null) return;
            try { handler(); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            _disposed = true;
            if (_kbHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_kbHook);
                _kbHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }
    }
}
