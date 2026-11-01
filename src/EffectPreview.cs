using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Okna
{
    /// <summary>
    /// Plays one of the close effects over the little window at the top of
    /// the settings form, so the choice can be seen without closing a real
    /// program. 1 = shards, 2 = fire, 3 = rolling into the taskbar.
    /// </summary>
    internal static class EffectPreview
    {
        public static void Play(Control anchor, int effect)
        {
            if (anchor == null || !anchor.IsHandleCreated) return;
            Rectangle r = anchor.RectangleToScreen(anchor.ClientRectangle);
            if (r.Width < 80 || r.Height < 80) return;

            Bitmap shot = DrawWindow(r.Width, r.Height);
            Native.RECT frame = new Native.RECT();
            frame.Left = r.Left;
            frame.Top = r.Top;
            frame.Right = r.Right;
            frame.Bottom = r.Bottom;

            if (effect == 2)
            {
                BurnOverlay.Play(shot, frame);
            }
            else if (effect == 3)
            {
                RollOverlay.Play(shot, frame, IntPtr.Zero);
            }
            else
            {
                Native.POINT impact = new Native.POINT();
                impact.X = r.Left + r.Width / 2;
                impact.Y = r.Top + r.Height / 2;
                ShatterOverlay.Play(shot, frame, impact);
            }
        }

        private static Bitmap DrawWindow(int w, int h)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Rectangle body = new Rectangle(0, 0, w - 1, h - 1);
                using (GraphicsPath path = Rounded(body, 10))
                {
                    using (LinearGradientBrush fill = new LinearGradientBrush(body,
                        Color.FromArgb(250, 249, 246), Color.FromArgb(233, 233, 229), 90f))
                    {
                        g.FillPath(fill, path);
                    }
                    using (Pen border = new Pen(Color.FromArgb(60, 90, 100, 105), 1f))
                        g.DrawPath(border, path);

                    g.SetClip(path);
                    using (SolidBrush bar = new SolidBrush(Color.FromArgb(238, 237, 233)))
                        g.FillRectangle(bar, 0, 0, w, 34);
                    g.ResetClip();
                }

                using (SolidBrush dot = new SolidBrush(Color.FromArgb(255, 95, 86)))
                    g.FillEllipse(dot, 14, 12, 10, 10);
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(255, 189, 46)))
                    g.FillEllipse(dot, 30, 12, 10, 10);
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(39, 201, 63)))
                    g.FillEllipse(dot, 46, 12, 10, 10);

                using (Font title = new Font(Theme.FontBody.FontFamily, 8.5f))
                using (SolidBrush ink = new SolidBrush(Color.FromArgb(90, 96, 100)))
                    g.DrawString("Окно", title, ink, new PointF(66, 9));

                float size = Math.Max(11f, Math.Min(20f, h / 9f));
                using (Font big = new Font(Theme.FontBody.FontFamily, size))
                using (SolidBrush ink = new SolidBrush(Color.FromArgb(46, 52, 56)))
                using (StringFormat center = new StringFormat())
                {
                    center.Alignment = StringAlignment.Center;
                    center.LineAlignment = StringAlignment.Center;
                    RectangleF area = new RectangleF(0, 34, w, h - 34);
                    g.DrawString("Железное окно", big, ink, area, center);
                }
            }
            return b;
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
