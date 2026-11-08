using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Okna
{
    internal static class Theme
    {
        public static bool DarkMode;

        // Windows Settings-inspired palette (improved light contrast)
        public static Color Bg { get { return DarkMode ? Color.FromArgb(32, 32, 32) : Color.FromArgb(240, 240, 240); } }
        public static Color BgLight { get { return DarkMode ? Color.FromArgb(39, 39, 39) : Color.FromArgb(246, 246, 246); } }
        public static Color Nav { get { return DarkMode ? Color.FromArgb(28, 28, 28) : Color.FromArgb(243, 243, 243); } }
        public static Color Card { get { return DarkMode ? Color.FromArgb(44, 44, 44) : Color.FromArgb(255, 255, 255); } }
        public static Color CardAlt { get { return DarkMode ? Color.FromArgb(56, 56, 56) : Color.FromArgb(249, 249, 249); } }
        public static Color Border { get { return DarkMode ? Color.FromArgb(68, 68, 68) : Color.FromArgb(220, 220, 220); } }
        public static Color BorderSoft { get { return DarkMode ? Color.FromArgb(60, 60, 60) : Color.FromArgb(229, 229, 229); } }
        public static Color Text { get { return DarkMode ? Color.FromArgb(250, 250, 250) : Color.FromArgb(26, 26, 26); } }
        public static Color Muted { get { return DarkMode ? Color.FromArgb(205, 205, 205) : Color.FromArgb(80, 80, 80); } }
        public static Color Faint { get { return DarkMode ? Color.FromArgb(160, 160, 160) : Color.FromArgb(115, 115, 115); } }
        public static Color Accent { get { return DarkMode ? Color.FromArgb(76, 194, 255) : Color.FromArgb(0, 103, 192); } }
        public static Color AccentDark { get { return DarkMode ? Color.FromArgb(60, 165, 225) : Color.FromArgb(0, 90, 158); } }
        public static Color AccentSoft { get { return DarkMode ? Color.FromArgb(35, 58, 74) : Color.FromArgb(224, 238, 249); } }
        public static Color Warm { get { return DarkMode ? Color.FromArgb(255, 210, 80) : Color.FromArgb(157, 93, 0); } }
        public static Color Danger { get { return DarkMode ? Color.FromArgb(255, 153, 164) : Color.FromArgb(196, 43, 28); } }
        public static Color NavSelected { get { return DarkMode ? Color.FromArgb(56, 56, 56) : Color.FromArgb(233, 233, 233); } }
        public static Color NavHover { get { return DarkMode ? Color.FromArgb(48, 48, 48) : Color.FromArgb(237, 237, 237); } }

        public static readonly Font FontBody = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontSmall = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontTiny = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontLabel = SafeFont("Segoe UI", 9.75f, FontStyle.Bold);
        public static readonly Font FontTitle = SafeFont("Segoe UI", 22f, FontStyle.Bold);
        public static readonly Font FontPageTitle = SafeFont("Segoe UI", 20f, FontStyle.Bold);
        public static readonly Font FontCardTitle = SafeFont("Segoe UI", 9.75f, FontStyle.Regular);
        public static readonly Font FontButton = SafeFont("Segoe UI", 9.75f, FontStyle.Regular);
        public static readonly Font FontMini = SafeFont("Segoe UI", 8.5f, FontStyle.Regular);

        private static Font SafeFont(string family, float size, FontStyle style)
        {
            try { return new Font(family, size, style, GraphicsUnit.Point); }
            catch { return new Font(SystemFonts.DefaultFont.FontFamily, size, style, GraphicsUnit.Point); }
        }

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
