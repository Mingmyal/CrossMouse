using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CrossMouse
{
    /// <summary>Small palette that reacts to the Windows light / dark setting.</summary>
    internal static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(0x2B, 0x7C, 0xD3);
        public static readonly Color AccentHover = Color.FromArgb(0x3D, 0x8E, 0xE4);

        public static bool Dark
        {
            get
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        if (k == null) return false;
                        object v = k.GetValue("AppsUseLightTheme", 1);
                        return Convert.ToInt32(v) == 0;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static Color Window { get { return Dark ? Color.FromArgb(0x20, 0x20, 0x20) : Color.FromArgb(0xF3, 0xF3, 0xF3); } }
        public static Color Card { get { return Dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.White; } }
        public static Color Field { get { return Dark ? Color.FromArgb(0x38, 0x38, 0x38) : Color.FromArgb(0xFF, 0xFF, 0xFF); } }
        public static Color Text { get { return Dark ? Color.FromArgb(0xF0, 0xF0, 0xF0) : Color.FromArgb(0x1A, 0x1A, 0x1A); } }
        public static Color Muted { get { return Dark ? Color.FromArgb(0xA0, 0xA0, 0xA0) : Color.FromArgb(0x6A, 0x6A, 0x6A); } }
        public static Color Border { get { return Dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xDE, 0xDE, 0xDE); } }
        public static Color Hover { get { return Dark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xEC, 0xEC, 0xEC); } }

        public static Font TitleFont { get { return new Font("Microsoft YaHei UI", 15f, FontStyle.Bold, GraphicsUnit.Point); } }
        public static Font BodyFont { get { return new Font("Microsoft YaHei UI", 9f, GraphicsUnit.Point); } }
        public static Font SmallFont { get { return new Font("Microsoft YaHei UI", 8.25f, GraphicsUnit.Point); } }
        public static Font HeaderFont { get { return new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Point); } }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Applies the palette to a whole control tree.</summary>
        public static void Apply(Control root)
        {
            foreach (Control c in root.Controls)
            {
                ApplyOne(c);
                Apply(c);
            }
        }

        private static void ApplyOne(Control c)
        {
            c.ForeColor = Text;
            if (c is Card || c is HotkeyField || c is Form || c is Panel || c is GroupBox || c is TableLayoutPanel)
            {
                c.BackColor = c is Card ? Card : (c is Form ? Window : Color.Transparent);
            }
            else if (c is Label || c is CheckBox || c is RadioButton || c is LinkLabel)
            {
                c.BackColor = Color.Transparent;
            }
            else if (c is ComboBox)
            {
                c.BackColor = Field;
                c.ForeColor = Text;
            }
            else if (c is Button)
            {
                Button b = (Button)c;
                if (b is AccentButton) return;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Border;
                b.BackColor = Field;
                b.ForeColor = Text;
            }
            if (c is LinkLabel) ((LinkLabel)c).LinkColor = Accent;

            ToolStrip strip = c as ToolStrip;
            if (strip != null)
            {
                strip.Renderer = new TrayRenderer(Dark);
                strip.BackColor = Card;
                strip.ForeColor = Text;
            }
        }
    }

    /// <summary>Card style container: rounded surface with a header line.</summary>
    internal sealed class Card : Panel
    {
        private string _header = "";

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        /// <summary>Scale factor of the monitor the window is on (96 dpi = 1.0).</summary>
        private float ScaleFactor { get { return DeviceDpi / 96f; } }

        private int Px(float value) { return (int)Math.Round(value * ScaleFactor, MidpointRounding.AwayFromZero); }

        public string Header
        {
            get { return _header; }
            set { _header = value == null ? "" : value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Theme.Window : Parent.BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(r, Px(8)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }

            if (_header.Length > 0)
            {
                using (SolidBrush b = new SolidBrush(Theme.Text))
                using (Font f = Theme.HeaderFont)
                {
                    g.DrawString(_header, f, b, new PointF(Px(16), Px(13)));
                }
            }
        }
    }

    /// <summary>The clickable "press a key" box.</summary>
    internal sealed class HotkeyField : Control
    {
        private string _value = "";
        private bool _capturing;
        private bool _hover;
        private bool _active;

        public HotkeyField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        private float ScaleFactor { get { return DeviceDpi / 96f; } }

        private int Px(float value) { return (int)Math.Round(value * ScaleFactor, MidpointRounding.AwayFromZero); }

        public string Value
        {
            get { return _value; }
            set { _value = value == null ? "" : value; Invalidate(); }
        }

        public bool Capturing
        {
            get { return _capturing; }
            set { _capturing = value; Invalidate(); }
        }

        /// <summary>True while this field owns the pressed-key capture.</summary>
        public bool Active
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Theme.Card : Parent.BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool highlight = _active || _capturing;
            using (GraphicsPath path = Theme.Rounded(r, Px(6)))
            {
                using (SolidBrush b = new SolidBrush(highlight || _hover ? Theme.Hover : Theme.Field))
                    g.FillPath(b, path);
                using (Pen p = new Pen(highlight ? Theme.Accent : Theme.Border, highlight ? 2f : 1f))
                    g.DrawPath(p, path);
            }

            string text = _capturing ? "请按下按键…" : _value;
            Color color = _capturing ? Theme.Accent : (Enabled ? Theme.Text : Theme.Muted);
            using (Font f = Theme.BodyFont)
            using (SolidBrush b = new SolidBrush(color))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, f, b, new RectangleF(Px(12), 0, Width - Px(48), Height), sf);
            }

            // Little pill on the right hints "click me".
            Rectangle pill = new Rectangle(Width - Px(34), Height / 2 - Px(10), Px(22), Px(20));
            using (SolidBrush b = new SolidBrush(highlight ? Theme.Accent : Color.Transparent))
            using (Pen p = new Pen(Theme.Border))
            {
                if (highlight) g.FillRectangle(b, pill); else g.DrawRectangle(p, pill);
            }
            using (Font f = new Font("Segoe UI Symbol", 8f))
            using (SolidBrush b = new SolidBrush(highlight ? Color.White : Theme.Muted))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("✎", f, b, pill, sf);
            }
        }
    }

    /// <summary>Flat, theme-aware button used for the primary action.</summary>
    internal sealed class AccentButton : Button
    {
        public AccentButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Accent;
            ForeColor = Color.White;
            Font = Theme.BodyFont;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            BackColor = Theme.AccentHover;
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            BackColor = Theme.Accent;
            base.OnMouseLeave(e);
        }
    }

    /// <summary>Context menu renderer so the tray menu follows the theme.</summary>
    internal sealed class TrayRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool _dark;

        public TrayRenderer(bool dark)
            : base(new TrayColors(dark))
        {
            _dark = dark;
            RoundedEdges = true;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = _dark ? Color.FromArgb(0xF0, 0xF0, 0xF0) : Color.FromArgb(0x1A, 0x1A, 0x1A);
            if (e.Item != null && !e.Item.Enabled) e.TextColor = Color.Gray;
            base.OnRenderItemText(e);
        }

        private sealed class TrayColors : ProfessionalColorTable
        {
            private readonly bool _dark;

            public TrayColors(bool dark) { _dark = dark; }

            public override Color ToolStripDropDownBackground { get { return _dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.White; } }
            public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
            public override Color MenuBorder { get { return _dark ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Color.FromArgb(0xD6, 0xD6, 0xD6); } }
            public override Color MenuItemBorder { get { return _dark ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Color.FromArgb(0xD6, 0xD6, 0xD6); } }
            public override Color MenuItemSelected { get { return _dark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xEA, 0xEA, 0xEA); } }
            public override Color MenuItemSelectedGradientBegin { get { return MenuItemSelected; } }
            public override Color MenuItemSelectedGradientEnd { get { return MenuItemSelected; } }
            public override Color MenuItemPressedGradientBegin { get { return MenuItemSelected; } }
            public override Color MenuItemPressedGradientEnd { get { return MenuItemSelected; } }
            public override Color SeparatorDark { get { return _dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xDE, 0xDE, 0xDE); } }
            public override Color SeparatorLight { get { return SeparatorDark; } }
        }
    }
}
