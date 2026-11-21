using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CrossMouse
{
    /// <summary>Tray presence: owns the guard, the hooks and the settings window.</summary>
    internal sealed class TrayApp : IDisposable
    {
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _enabledItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly Timer _uiTimer;
        private SettingsForm _form;
        private Icon _iconLocked;
        private Icon _iconUnlocked;
        private bool _disposed;

        public Settings Settings { get; private set; }
        public Guard Guard { get; private set; }
        public HotkeyHooks Hooks { get; private set; }
        public Icon FormIcon { get; private set; }

        public TrayApp(Settings settings)
        {
            Settings = settings;
            // Persist the effective configuration on first run so the registry always
            // reflects what the app is actually doing.
            Settings.Save();

            FormIcon = IconArt.CreateIcon(32, false);
            _iconLocked = IconArt.CreateIcon(16, false);
            _iconUnlocked = IconArt.CreateIcon(16, true);

            _menu = new ContextMenuStrip();
            _menu.Renderer = new TrayRenderer(Theme.Dark);
            _menu.ShowImageMargin = false;
            _menu.Font = Theme.BodyFont;

            _statusItem = new ToolStripMenuItem("正在初始化…");
            _statusItem.Enabled = false;
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());

            _enabledItem = new ToolStripMenuItem("启用鼠标锁定");
            _enabledItem.CheckOnClick = true;
            _enabledItem.Checked = Settings.Enabled;
            _enabledItem.Click += delegate
            {
                Settings.Enabled = _enabledItem.Checked;
                Settings.Save();
                Reload();
                if (_form != null) _form.RefreshMonitors();
            };
            _menu.Items.Add(_enabledItem);

            _startupItem = new ToolStripMenuItem("开机自动启动");
            _startupItem.CheckOnClick = true;
            _startupItem.Checked = Startup.IsEnabled();
            _startupItem.Click += delegate
            {
                Startup.Set(_startupItem.Checked);
                Settings.RunAtStartup = _startupItem.Checked;
            };
            _menu.Items.Add(_startupItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem settingsItem = new ToolStripMenuItem("设置…");
            settingsItem.Click += delegate { ShowSettings(); };
            _menu.Items.Add(settingsItem);

            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { Exit(); };
            _menu.Items.Add(quit);

            _tray = new NotifyIcon();
            _tray.Icon = _iconLocked;
            _tray.Text = "CrossMouse";
            _tray.ContextMenuStrip = _menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowSettings(); };

            Guard = new Guard(Settings);

            Hooks = new HotkeyHooks();
            Hooks.StateChanged += delegate
            {
                try { Guard.Maintain(false); }
                catch (Exception) { }
                UpdateTray();
            };
            Hooks.Captured += delegate(HotkeyKind kind, int code)
            {
                if (_form != null) _form.OnCaptured(kind, code);
            };
            bool installed = Hooks.Install();

            // Safety net: if the low level hooks are unavailable (they can be blocked
            // by security software), poll instead so the feature still works.
            _uiTimer = new Timer();
            _uiTimer.Interval = installed ? 400 : 15;
            _uiTimer.Tick += delegate
            {
                try { Guard.Maintain(!installed); }
                catch (Exception) { }
                UpdateTray();
                Watchdog.EnsureRunning();
            };
            _uiTimer.Start();

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            // The watcher must be up before the first clip is installed: a hard kill
            // would otherwise leave the pointer trapped on one monitor.
            Watchdog.Start();

            Guard.Maintain(true);
            UpdateTray();
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            if (_disposed) return;
            Guard.RefreshMonitors();
            if (_form != null && !_form.IsDisposed) _form.RefreshMonitors();
            UpdateTray();
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_disposed) return;
            if (e.Category == UserPreferenceCategory.Desktop) UpdateTray();
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (_disposed) return;
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff)
            {
                Guard.Suspend();
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
            {
                Guard.Resume();
            }
        }

        /// <summary>Re-apply everything after a setting changed.</summary>
        public void Reload()
        {
            Settings.Save();
            Guard.Maintain(true);
            UpdateTray();
        }

        private void UpdateTray()
        {
            bool unlocked = Guard.IsUnlocked;
            Icon wanted = unlocked ? _iconUnlocked : _iconLocked;
            if (_tray.Icon != wanted) _tray.Icon = wanted;

            string state;
            if (!Settings.Enabled) state = "已暂停";
            else if (unlocked) state = "已临时解锁";
            else if (Guard.IsEngaged) state = "已锁定";
            else state = "未能锁定";

            string key = KeyNames.Name(Settings.Kind, Settings.Code);
            string tip = "CrossMouse · " + state + "\n按住 " + key + " 可跨屏";
            if (_tray.Text != tip) _tray.Text = tip;

            string monitor = Guard.LockedMonitor != null ? Guard.LockedMonitor.ShortName : "—";
            string statusText = !Settings.Enabled
                ? "鼠标锁定已暂停"
                : (unlocked ? "按住 " + key + " · 已解锁" : "锁定中：" + monitor);
            if (_statusItem.Text != statusText) _statusItem.Text = statusText;
            if (_enabledItem.Checked != Settings.Enabled) _enabledItem.Checked = Settings.Enabled;
            if (_startupItem.Checked != Startup.IsEnabled()) _startupItem.Checked = Startup.IsEnabled();

            if (_form != null && !_form.IsDisposed && _form.Visible)
            {
                _form.UpdateStatus(Guard.IsEngaged, unlocked, Guard.LockedMonitor);
            }
        }

        public void ShowSettings()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new SettingsForm(this);
            }
            _form.RefreshMonitors();
            _form.UpdateStatus(Guard.IsEngaged, Guard.IsUnlocked, Guard.LockedMonitor);
            if (!_form.Visible) _form.Show();
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
        }

        public void Exit()
        {
            Dispose();
            Application.Exit();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            if (_uiTimer != null) _uiTimer.Stop();
            if (Hooks != null) Hooks.Dispose();
            if (Guard != null)
            {
                Guard.Dispose();
                // Release the clip before telling the watchdog to stand down.
                Native.ClipCursor(IntPtr.Zero);
            }
            Watchdog.Stop();

            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();

            if (_form != null && !_form.IsDisposed) _form.Dispose();
        }
    }
}
