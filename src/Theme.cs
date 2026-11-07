using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Okna
{
    internal static class Theme
    {
        public static bool DarkMode;

        // Windows Settings-inspired palette
        public static Color Bg { get { return DarkMode ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); } }
        public static Color BgLight { get { return DarkMode ? Color.FromArgb(39, 39, 39) : Color.FromArgb(249, 249, 249); } }
        public static Color Nav { get { return DarkMode ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); } }
        public static Color Card { get { return DarkMode ? Color.FromArgb(45, 45, 45) : Color.FromArgb(251, 251, 251); } }
        public static Color CardAlt { get { return DarkMode ? Color.FromArgb(55, 55, 55) : Color.FromArgb(246, 246, 246); } }
        public static Color Border { get { return DarkMode ? Color.FromArgb(60, 60, 60) : Color.FromArgb(229, 229, 229); } }
        public static Color BorderSoft { get { return DarkMode ? Color.FromArgb(55, 55, 55) : Color.FromArgb(234, 234, 234); } }
        public static Color Text { get { return DarkMode ? Color.FromArgb(255, 255, 255) : Color.FromArgb(28, 28, 28); } }
        public static Color Muted { get { return DarkMode ? Color.FromArgb(200, 200, 200) : Color.FromArgb(96, 96, 96); } }
        public static Color Faint { get { return DarkMode ? Color.FromArgb(155, 155, 155) : Color.FromArgb(130, 130, 130); } }
        public static Color Accent { get { return DarkMode ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 103, 192); } }
        public static Color AccentDark { get { return DarkMode ? Color.FromArgb(76, 160, 210) : Color.FromArgb(0, 90, 158); } }
        public static Color AccentSoft { get { return DarkMode ? Color.FromArgb(45, 65, 80) : Color.FromArgb(224, 238, 249); } }
        public static Color Warm { get { return DarkMode ? Color.FromArgb(252, 225, 0) : Color.FromArgb(157, 93, 0); } }
        public static Color Danger { get { return DarkMode ? Color.FromArgb(255, 153, 164) : Color.FromArgb(196, 43, 28); } }
        public static Color NavSelected { get { return DarkMode ? Color.FromArgb(55, 55, 55) : Color.FromArgb(234, 234, 234); } }
        public static Color NavHover { get { return DarkMode ? Color.FromArgb(48, 48, 48) : Color.FromArgb(238, 238, 238); } }

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
