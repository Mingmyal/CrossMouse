using System;
using System.Collections.Generic;

namespace CrossMouse
{
    /// <summary>
    /// Owns the cursor confinement: keeps the pointer on one monitor and lets it
    /// travel across screens only while the user holds the configured key.
    /// </summary>
    internal sealed class Guard : IDisposable
    {
        private readonly Settings _settings;
        private readonly object _sync = new object();

        private bool _engaged;          // we currently own the system clip
        private bool _unlocked;         // user is holding the key -> free to cross
        private RECT _appliedRect;
        private MonitorInfo _appliedMonitor;
        private DateTime _lastApply = DateTime.MinValue;
        private DateTime _lastMaintain = DateTime.MinValue;
        private bool _inMaintain;
        private bool _suspended;
        private IntPtr _lastMonitorHandle;
        private bool _disposed;

        public Guard(Settings settings)
        {
            _settings = settings;
        }

        public bool IsUnlocked { get { lock (_sync) return _unlocked; } }
        public bool IsEngaged { get { lock (_sync) return _engaged; } }
        public MonitorInfo LockedMonitor { get { lock (_sync) return _appliedMonitor; } }

        /// <summary>True while the hotkey is held down.</summary>
        public bool HotkeyDown
        {
            get
            {
                lock (_sync)
                {
                    if (_settings.Code <= 0) return false;
                    return Native.KeyIsDown(_settings.Code);
                }
            }
        }

        /// <summary>
        /// Re-evaluate lock state. Cheap enough to be called from the hooks and a timer.
        /// </summary>
        public void Maintain(bool force)
        {
            lock (_sync)
            {
                if (_disposed || _inMaintain) return;
                _inMaintain = true;
                try
                {
                    DateTime now = DateTime.UtcNow;
                    if (!force && (now - _lastMaintain).TotalMilliseconds < 20) return;
                    _lastMaintain = now;

                    if (!_settings.Enabled || _suspended)
                    {
                        if (_engaged || _unlocked)
                        {
                            Release();
                            _unlocked = false;
                        }
                        return;
                    }

                    bool down = _settings.Code > 0 && Native.KeyIsDown(_settings.Code);

                    if (down)
                    {
                        if (!_unlocked) Unlock();
                        return;
                    }

                    if (_unlocked) _unlocked = false;

                    if (!_engaged)
                    {
                        Lock();
                        return;
                    }

                    // Another program (or the shell) may have replaced our clip.
                    if ((now - _lastApply).TotalMilliseconds > 250)
                    {
                        RECT current;
                        if (!Native.GetClipCursor(out current) || !current.SameAs(_appliedRect))
                        {
                            Lock();
                        }
                    }
                }
                finally
                {
                    _inMaintain = false;
                }
            }
        }

        /// <summary>Drop the clip so the pointer can travel to any screen.</summary>
        public void Unlock()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _unlocked = true;
                _engaged = false;
                _appliedMonitor = null;
                Native.ClipCursor(IntPtr.Zero);
            }
        }

        /// <summary>Confine the pointer to the target monitor.</summary>
        public void Lock()
        {
            lock (_sync)
            {
                if (_disposed || !_settings.Enabled || _suspended) return;

                MonitorInfo target = ChooseMonitor();
                if (target == null) return;

                POINT p;
                Native.GetCursorPos(out p);
                if (p.X < target.Bounds.Left || p.X >= target.Bounds.Right ||
                    p.Y < target.Bounds.Top || p.Y >= target.Bounds.Bottom)
                {
                    // Keep the pointer inside the clip before installing it, otherwise
                    // Windows yanks it to the nearest edge and the jump looks like a bug.
                    int x = Clamp(p.X, target.Bounds.Left, target.Bounds.Right - 1);
                    int y = Clamp(p.Y, target.Bounds.Top, target.Bounds.Bottom - 1);
                    Native.SetCursorPos(x, y);
                }

                RECT rect = target.Bounds;
                Native.ClipCursor(ref rect);

                RECT applied;
                if (Native.GetClipCursor(out applied) && applied.SameAs(rect))
                {
                    _engaged = true;
                    _appliedRect = rect;
                    _appliedMonitor = target;
                    _lastApply = DateTime.UtcNow;
                    _lastMonitorHandle = target.Handle;
                }
                else
                {
                    _engaged = false;
                    _appliedMonitor = null;
                }
            }
        }

        /// <summary>Called when monitors are added, removed or rearranged.</summary>
        public void RefreshMonitors()
        {
            lock (_sync)
            {
                List<MonitorInfo> all = MonitorInfo.All();
                _appliedMonitor = null;
                _engaged = false;
                if (_unlocked || all.Count == 0) return;
                // The old rectangle may now cover a different screen (or nothing at all),
                // so always re-derive the target from the current layout.
                Lock();
            }
        }

        public void Suspend()
        {
            lock (_sync)
            {
                _suspended = true;
                Release();
                _unlocked = false;
            }
        }

        public void Resume()
        {
            lock (_sync)
            {
                _suspended = false;
                _engaged = false;
                _unlocked = false;
            }
            Maintain(true);
        }

        private void Release()
        {
            if (_engaged)
            {
                Native.ClipCursor(IntPtr.Zero);
            }
            _engaged = false;
            _appliedMonitor = null;
        }

        private MonitorInfo ChooseMonitor()
        {
            MonitorInfo current = CursorMonitor();
            if (_settings.Scope == LockScope.SpecificMonitor && !string.IsNullOrEmpty(_settings.MonitorDevice))
            {
                foreach (MonitorInfo m in MonitorInfo.All())
                {
                    if (m.Device == _settings.MonitorDevice) return m;
                }
            }
            if (current != null) return current;

            List<MonitorInfo> all = MonitorInfo.All();
            if (all.Count > 0) return all[0];
            return null;
        }

        private static MonitorInfo CursorMonitor()
        {
            POINT p;
            if (!Native.GetCursorPos(out p)) return null;
            System.Drawing.Point pt = new System.Drawing.Point(p.X, p.Y);
            return MonitorInfo.FromPoint(pt);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                Release();
                Native.ClipCursor(IntPtr.Zero);
            }
        }
    }
}
