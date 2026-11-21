using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace CrossMouse
{
    /// <summary>
    /// Draws the application icon. Two screens, a barrier between them and an
    /// arrow that may cross it - the icon also has an "unlocked" variant used by
    /// the tray while the hotkey is held.
    /// </summary>
    internal static class IconArt
    {
        public static Bitmap Render(int size, bool unlocked)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float s = size / 32f;
                RectangleF all = new RectangleF(0.5f * s, 0.5f * s, size - 1f * s, size - 1f * s);

                // Rounded plate.
                using (GraphicsPath plate = Round(all, 7f * s))
                {
                    using (LinearGradientBrush bg = new LinearGradientBrush(
                        new RectangleF(0, 0, size, size),
                        unlocked ? Color.FromArgb(0x2F, 0xB1, 0x6B) : Color.FromArgb(0x2B, 0x7C, 0xD3),
                        unlocked ? Color.FromArgb(0x1F, 0x8C, 0x52) : Color.FromArgb(0x1B, 0x5C, 0xA8),
                        LinearGradientMode.ForwardDiagonal))
                    {
                        g.FillPath(bg, plate);
                    }

                    // Two screens with a visible gap between them.
                    float top = 9.5f * s, h = 13f * s, w = 10.3f * s;
                    RectangleF left = new RectangleF(2.6f * s, top, w, h);
                    RectangleF right = new RectangleF(19.1f * s, top, w, h);
                    using (Pen p = new Pen(Color.FromArgb(240, 255, 255, 255), 1.6f * s))
                    {
                        using (GraphicsPath lp = Round(left, 2.6f * s)) g.DrawPath(p, lp);
                        using (GraphicsPath rp = Round(right, 2.6f * s)) g.DrawPath(p, rp);
                    }

                    // While locked, a dashed barrier seals the boundary between screens.
                    if (!unlocked)
                    {
                        using (Pen barrier = new Pen(Color.FromArgb(215, 255, 255, 255), 1.25f * s))
                        {
                            barrier.DashStyle = DashStyle.Dash;
                            barrier.DashPattern = new float[] { 1.15f, 1.15f };
                            barrier.StartCap = LineCap.Round;
                            barrier.EndCap = LineCap.Round;
                            g.DrawLine(barrier, 16f * s, top - 0.6f * s, 16f * s, top + h + 0.6f * s);
                        }
                    }

                    // The pointer travelling through the gap.
                    using (SolidBrush arrow = new SolidBrush(Color.White))
                    {
                        float y = 16f * s;
                        PointF[] tri = new PointF[]
                        {
                            new PointF(18.3f * s, y),
                            new PointF(13.7f * s, y - 2.9f * s),
                            new PointF(13.7f * s, y + 2.9f * s)
                        };
                        g.FillPolygon(arrow, tri);
                    }
                }
            }
            return bmp;
        }

        private static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Icon CreateIcon(int size, bool unlocked)
        {
            using (Bitmap bmp = Render(size, unlocked))
            {
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>Writes a multi-resolution .ico built from the same drawing code.</summary>
        public static void WriteIco(string path, int[] sizes)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                int count = sizes.Length;
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)count);

                byte[][] images = new byte[count][];
                for (int i = 0; i < count; i++)
                {
                    images[i] = PngBytes(sizes[i]);
                }

                int offset = 6 + 16 * count;
                for (int i = 0; i < count; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                for (int i = 0; i < count; i++) w.Write(images[i]);
            }
        }

        private static byte[] PngBytes(int size)
        {
            using (Bitmap bmp = Render(size, false))
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }
}
