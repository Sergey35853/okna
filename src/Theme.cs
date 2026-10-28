using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Okna
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(11, 17, 23);
        public static readonly Color BgLight = Color.FromArgb(16, 26, 35);
        public static readonly Color Card = Color.FromArgb(17, 28, 36);
        public static readonly Color CardAlt = Color.FromArgb(23, 37, 47);
        public static readonly Color Border = Color.FromArgb(33, 51, 63);
        public static readonly Color BorderSoft = Color.FromArgb(27, 42, 53);
        public static readonly Color Text = Color.FromArgb(232, 242, 243);
        public static readonly Color Muted = Color.FromArgb(139, 160, 171);
        public static readonly Color Faint = Color.FromArgb(96, 118, 130);
        public static readonly Color Accent = Color.FromArgb(93, 228, 199);
        public static readonly Color AccentDark = Color.FromArgb(33, 126, 110);
        public static readonly Color AccentSoft = Color.FromArgb(34, 64, 63);
        public static readonly Color Warm = Color.FromArgb(242, 184, 75);
        public static readonly Color Danger = Color.FromArgb(235, 111, 112);

        public static readonly Font FontBody = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontTiny = new Font("Segoe UI", 7.75f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontLabel = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontTitle = new Font("Segoe UI", 21f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontCardTitle = new Font("Segoe UI", 11.5f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontButton = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontMini = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Brush brush, RectangleF rect, float radius)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
                g.FillPath(brush, path);
        }

        public static void DrawRounded(Graphics g, Pen pen, RectangleF rect, float radius)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
                g.DrawPath(pen, path);
        }

        public static Color Blend(Color a, Color b, float amount)
        {
            if (amount < 0f) amount = 0f;
            if (amount > 1f) amount = 1f;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * amount),
                (int)(a.R + (b.R - a.R) * amount),
                (int)(a.G + (b.G - a.G) * amount),
                (int)(a.B + (b.B - a.B) * amount));
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
