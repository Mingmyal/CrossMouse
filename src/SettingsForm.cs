using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CrossMouse
{
    /// <summary>
    /// Configuration window. The layout is computed from a single scale factor so it
    /// stays identical at 100 %, 125 % and 150 % display scaling.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private const int CardGap = 12;

        private readonly TrayApp _app;
        private float _scale = 1f;
        private bool _built;

        private HotkeyField _hotkey;
        private Label _hotkeyHint;
        private LinkLabel _reset;
        private RadioButton _scopeCurrent;
        private RadioButton _scopeFixed;
        private ComboBox _monitorBox;
        private Label _monitorHint;
        private CheckBox _enabledBox;
        private CheckBox _startupBox;
        private Label _status;
        private Card _hotkeyCard;
        private Card _scopeCard;
        private Button _closeButton;
        private AccentButton _hideButton;
        private Timer _captureWatch;

        private bool _capturing;
        private bool _loading;

        public SettingsForm(TrayApp app)
        {
            _app = app;

            Text = "CrossMouse 设置";
            Font = Theme.BodyFont;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            MaximizeBox = false;
            MinimizeBox = true;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = _app != null ? _app.FormIcon : null;

            _captureWatch = new Timer();
            _captureWatch.Interval = 120;
            _captureWatch.Tick += delegate
            {
                if (_capturing && !ContainsFocus) EndCapture(false);
            };

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplayChanged;
        }

        private int Px(float value)
        {
            return (int)Math.Round(value * _scale, MidpointRounding.AwayFromZero);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_built) return;
            _built = true;
            _scale = DeviceDpi / 96f;
            BuildUi();
            LoadFromSettings();
            RefreshMonitors();
        }

        private void BuildUi()
        {
            SuspendLayout();

            const int width = 700;
            const int padL = 22, padT = 18, padB = 14;
            const int titleH = 74, hotkeyH = 118, scopeH = 152, optionsH = 124, statusH = 26, footerH = 44;

            int contentW = Px(width - padL * 2);
            int y = Px(padT);

            // ------------------------------------------------------------- title
            Label title = new Label();
            title.Text = "CrossMouse";
            title.Font = Theme.TitleFont;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Location = new Point(Px(padL) + Px(2), y);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "按住快捷键时鼠标可跨越屏幕，松开即锁回，避免误滑到副屏。";
            subtitle.Font = Theme.SmallFont;
            subtitle.ForeColor = Theme.Muted;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(Px(padL) + Px(4), y + Px(42));
            Controls.Add(subtitle);
            y += Px(titleH);

            // ------------------------------------------------------------ hotkey
            _hotkeyCard = new Card();
            _hotkeyCard.Header = "快捷键";
            _hotkeyCard.Bounds = new Rectangle(Px(padL), y, contentW, Px(hotkeyH));
            Controls.Add(_hotkeyCard);
            y += Px(hotkeyH) + Px(CardGap);

            _hotkey = new HotkeyField();
            _hotkey.Location = new Point(Px(16), Px(42));
            _hotkey.Size = new Size(contentW - Px(32), Px(36));
            _hotkey.Click += delegate { BeginCapture(); };
            _hotkeyCard.Controls.Add(_hotkey);

            _hotkeyHint = new Label();
            _hotkeyHint.Text = "点击上方方框，然后按下你想使用的按键（支持键盘与鼠标中键 / 侧键）。";
            _hotkeyHint.Font = Theme.SmallFont;
            _hotkeyHint.ForeColor = Theme.Muted;
            _hotkeyHint.AutoSize = false;
            _hotkeyHint.AutoEllipsis = true;
            _hotkeyHint.Location = new Point(Px(16), Px(88));
            _hotkeyHint.Size = new Size(contentW - Px(32), Px(18));
            _hotkeyCard.Controls.Add(_hotkeyHint);

            _reset = new LinkLabel();
            _reset.Text = "恢复默认";
            _reset.Font = Theme.SmallFont;
            _reset.AutoSize = false;
            _reset.TextAlign = ContentAlignment.MiddleRight;
            _reset.LinkBehavior = LinkBehavior.HoverUnderline;
            _reset.Bounds = new Rectangle(contentW - Px(16) - Px(80), Px(86), Px(80), Px(20));
            _reset.LinkClicked += delegate
            {
                _app.Settings.Kind = HotkeyKind.Keyboard;
                _app.Settings.Code = 0xA3;
                _app.Settings.Save();
                LoadFromSettings();
                _app.Reload();
            };
            _hotkeyCard.Controls.Add(_reset);
            _reset.BringToFront();

            // ------------------------------------------------------------- scope
            _scopeCard = new Card();
            _scopeCard.Header = "锁定范围";
            _scopeCard.Bounds = new Rectangle(Px(padL), y, contentW, Px(scopeH));
            Controls.Add(_scopeCard);
            y += Px(scopeH) + Px(CardGap);

            _scopeCurrent = new RadioButton();
            _scopeCurrent.Text = "跟随鼠标：在当前所在的显示器上锁定（推荐）";
            _scopeCurrent.AutoSize = true;
            _scopeCurrent.Location = new Point(Px(14), Px(40));
            _scopeCurrent.CheckedChanged += ScopeChanged;
            _scopeCard.Controls.Add(_scopeCurrent);

            _scopeFixed = new RadioButton();
            _scopeFixed.Text = "固定显示器：始终锁定在指定的那块屏幕";
            _scopeFixed.AutoSize = true;
            _scopeFixed.Location = new Point(Px(14), Px(70));
            _scopeFixed.CheckedChanged += ScopeChanged;
            _scopeCard.Controls.Add(_scopeFixed);

            _monitorBox = new ComboBox();
            _monitorBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _monitorBox.FlatStyle = FlatStyle.Flat;
            _monitorBox.Location = new Point(Px(36), Px(98));
            _monitorBox.Size = new Size(Px(300), Px(26));
            _monitorBox.SelectedIndexChanged += MonitorSelected;
            _scopeCard.Controls.Add(_monitorBox);

            _monitorHint = new Label();
            _monitorHint.Text = "检测到 0 块显示器";
            _monitorHint.Font = Theme.SmallFont;
            _monitorHint.ForeColor = Theme.Muted;
            _monitorHint.AutoSize = true;
            _monitorHint.Location = new Point(Px(348), Px(103));
            _scopeCard.Controls.Add(_monitorHint);

            // ----------------------------------------------------------- options
            Card optionsCard = new Card();
            optionsCard.Header = "选项";
            optionsCard.Bounds = new Rectangle(Px(padL), y, contentW, Px(optionsH));
            Controls.Add(optionsCard);
            y += Px(optionsH) + Px(CardGap);

            _enabledBox = new CheckBox();
            _enabledBox.Text = "启用鼠标锁定";
            _enabledBox.AutoSize = true;
            _enabledBox.Location = new Point(Px(14), Px(40));
            _enabledBox.CheckedChanged += delegate
            {
                if (_loading) return;
                _app.Settings.Enabled = _enabledBox.Checked;
                _app.Settings.Save();
                _app.Reload();
            };
            optionsCard.Controls.Add(_enabledBox);

            _startupBox = new CheckBox();
            _startupBox.Text = "开机时自动启动";
            _startupBox.AutoSize = true;
            _startupBox.Location = new Point(Px(14), Px(70));
            _startupBox.CheckedChanged += delegate
            {
                if (_loading) return;
                Startup.Set(_startupBox.Checked);
                _app.Settings.RunAtStartup = _startupBox.Checked;
            };
            optionsCard.Controls.Add(_startupBox);

            Label tip = new Label();
            tip.Text = "关闭窗口后程序仍在托盘中运行。";
            tip.Font = Theme.SmallFont;
            tip.ForeColor = Theme.Muted;
            tip.AutoSize = true;
            tip.Location = new Point(Px(14), Px(96));
            optionsCard.Controls.Add(tip);

            // ------------------------------------------------------------ status
            _status = new Label();
            _status.Font = Theme.SmallFont;
            _status.ForeColor = Theme.Muted;
            _status.AutoSize = false;
            _status.AutoEllipsis = true;
            _status.TextAlign = ContentAlignment.BottomLeft;
            _status.Bounds = new Rectangle(Px(padL), y, contentW, Px(statusH));
            Controls.Add(_status);
            y += Px(statusH);

            // ------------------------------------------------------------ footer
            _hideButton = new AccentButton();
            _hideButton.Text = "隐藏到托盘";
            _hideButton.Size = new Size(Px(120), Px(32));
            _hideButton.Location = new Point(Px(width - padL) - Px(120), y);
            _hideButton.Click += delegate { Hide(); };
            Controls.Add(_hideButton);

            _closeButton = new Button();
            _closeButton.Text = "关闭";
            _closeButton.Size = new Size(Px(96), Px(32));
            _closeButton.Location = new Point(Px(width - padL) - Px(120) - Px(10) - Px(96), y);
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.FlatAppearance.BorderColor = Theme.Border;
            _closeButton.Click += delegate { Close(); };
            Controls.Add(_closeButton);

            y += Px(footerH) + Px(padB);

            ClientSize = new Size(Px(width), y);

            Theme.Apply(this);
            BackColor = Theme.Window;
            ResumeLayout();
        }

        private void LoadFromSettings()
        {
            _loading = true;
            Settings s = _app.Settings;
            _hotkey.Value = KeyNames.Name(s.Kind, s.Code);
            _scopeCurrent.Checked = s.Scope == LockScope.CurrentMonitor;
            _scopeFixed.Checked = s.Scope == LockScope.SpecificMonitor;
            _enabledBox.Checked = s.Enabled;
            _startupBox.Checked = Startup.IsEnabled();
            _monitorBox.Enabled = s.Scope == LockScope.SpecificMonitor;
            _loading = false;
        }

        public void RefreshMonitors()
        {
            if (!_built) return;
            List<MonitorInfo> monitors = MonitorInfo.All();
            string wanted = _app.Settings.MonitorDevice;

            _loading = true;
            try
            {
                _monitorBox.Items.Clear();
                foreach (MonitorInfo m in monitors) _monitorBox.Items.Add(new MonitorItem(m));
                if (_monitorBox.Items.Count > 0)
                {
                    int index = 0;
                    for (int i = 0; i < _monitorBox.Items.Count; i++)
                    {
                        if (((MonitorItem)_monitorBox.Items[i]).Info.Device == wanted) { index = i; break; }
                    }
                    _monitorBox.SelectedIndex = index;
                }
                _monitorBox.Enabled = _scopeFixed.Checked;
                _monitorHint.Text = monitors.Count == 0
                    ? "未检测到显示器"
                    : "共 " + monitors.Count + " 块显示器";
            }
            finally
            {
                _loading = false;
            }
        }

        private void ScopeChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            Settings s = _app.Settings;
            s.Scope = _scopeFixed.Checked ? LockScope.SpecificMonitor : LockScope.CurrentMonitor;
            if (s.Scope == LockScope.SpecificMonitor && _monitorBox.SelectedItem != null)
            {
                s.MonitorDevice = ((MonitorItem)_monitorBox.SelectedItem).Info.Device;
            }
            _monitorBox.Enabled = s.Scope == LockScope.SpecificMonitor;
            s.Save();
            _app.Reload();
        }

        private void MonitorSelected(object sender, EventArgs e)
        {
            if (_loading) return;
            MonitorItem item = _monitorBox.SelectedItem as MonitorItem;
            if (item == null) return;
            _app.Settings.MonitorDevice = item.Info.Device;
            _app.Settings.Save();
            if (_app.Settings.Scope == LockScope.SpecificMonitor) _app.Reload();
        }

        // ------------------------------------------------------------- capture
        private void BeginCapture()
        {
            _capturing = true;
            _hotkey.Capturing = true;
            _hotkey.Active = true;
            _app.Hooks.CaptureMode = true;
            _hotkeyHint.Text = "正在等待按键…（Esc 取消）";
            _hotkeyHint.ForeColor = Theme.Accent;
            _captureWatch.Start();
            Focus();
        }

        public void EndCapture(bool accept)
        {
            if (!_capturing) return;
            _capturing = false;
            _hotkey.Capturing = false;
            _hotkey.Active = false;
            _app.Hooks.CaptureMode = false;
            _hotkeyHint.Text = "点击上方方框，然后按下你想使用的按键（支持键盘与鼠标中键 / 侧键）。";
            _hotkeyHint.ForeColor = Theme.Muted;
            _captureWatch.Stop();
            if (!accept) _hotkey.Value = KeyNames.Name(_app.Settings.Kind, _app.Settings.Code);
        }

        /// <summary>Called from the low level hook when a key is pressed while capturing.</summary>
        public void OnCaptured(HotkeyKind kind, int code)
        {
            if (!_capturing) return;
            if (kind == HotkeyKind.Keyboard && code == Native.VK_ESCAPE)
            {
                EndCapture(false);
                return;
            }
            Settings s = _app.Settings;
            s.Kind = kind;
            s.Code = code;
            s.Save();
            _hotkey.Value = KeyNames.Name(kind, code);
            EndCapture(true);
            _app.Reload();
        }

        public bool IsCapturing { get { return _capturing; } }

        public void UpdateStatus(bool engaged, bool unlocked, MonitorInfo monitor)
        {
            if (IsDisposed || _status == null) return;
            string text;
            if (!_app.Settings.Enabled)
            {
                text = "状态：已暂停（鼠标可自由跨越所有屏幕）";
            }
            else if (unlocked)
            {
                text = "状态：已临时解锁，松开快捷键后自动锁回";
            }
            else if (engaged && monitor != null)
            {
                text = "状态：已锁定在 " + monitor.ShortName
                     + "（按住 " + KeyNames.Name(_app.Settings.Kind, _app.Settings.Code) + " 可临时解锁）";
            }
            else
            {
                text = "状态：正在初始化…";
            }
            if (_status.Text != text) _status.Text = text;
        }

        private void SystemEvents_DisplayChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            try { RefreshMonitors(); }
            catch (Exception) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                EndCapture(false);
                Hide();
                return;
            }
            EndCapture(false);
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplayChanged;
                if (_captureWatch != null) _captureWatch.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class MonitorItem
        {
            public readonly MonitorInfo Info;

            public MonitorItem(MonitorInfo info) { Info = info; }

            public override string ToString() { return Info.DisplayName; }
        }
    }
}
