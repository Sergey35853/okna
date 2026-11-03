using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Okna
{
    /// <summary>
    /// Tray icons that tell at a glance whether the effects are working: a
    /// green-free normal icon, an amber dot for any pause (manual, automatic,
    /// battery, reduce motion) and a blue dot for an excluded program.
    /// </summary>
    internal static class TrayIcons
    {
        private static readonly object Sync = new object();
        private static Icon _base;
        private static Icon _normal;
        private static Icon _paused;
        private static Icon _excluded;
        private static Icon _off;

        public static Icon For(Icon baseIcon, bool enabled, string reason)
        {
            lock (Sync)
            {
                if (!ReferenceEquals(_base, baseIcon))
                {
                    DisposeCache();
                    _base = baseIcon;
                }

                int state = !enabled ? 3
                    : (reason == null ? 0 : (reason.StartsWith("Исключение", StringComparison.Ordinal) ? 2 : 1));

                switch (state)
                {
                    case 1:
                        if (_paused == null) _paused = Make(baseIcon, Color.FromArgb(232, 163, 61));
                        return _paused ?? baseIcon;
                    case 2:
                        if (_excluded == null) _excluded = Make(baseIcon, Color.FromArgb(74, 158, 255));
                        return _excluded ?? baseIcon;
                    case 3:
                        if (_off == null) _off = Make(baseIcon, Color.FromArgb(122, 138, 150));
                        return _off ?? baseIcon;
                    default:
                        if (_normal == null) _normal = Clone(baseIcon);
                        return _normal ?? baseIcon;
                }
            }
        }

        private static Icon Clone(Icon icon)
        {
            try { return icon == null ? null : (Icon)icon.Clone(); }
            catch { return icon; }
        }

        private static Icon Make(Icon baseIcon, Color dot)
        {
            try
            {
                Bitmap bitmap = new Bitmap(32, 32);
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    if (baseIcon != null)
                    {
                        using (Icon sized = new Icon(baseIcon, 32, 32))
                            g.DrawIcon(sized, new Rectangle(0, 0, 32, 32));
                    }

                    // A small badge in the lower-right corner; a white ring
                    // keeps it readable over dark and light taskbars.
                    using (SolidBrush ring = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                        g.FillEllipse(ring, 17, 17, 15, 15);
                    using (SolidBrush fill = new SolidBrush(dot))
                        g.FillEllipse(fill, 19, 19, 11, 11);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                        return (Icon)temporary.Clone();
                }
                finally
                {
                    Native.DestroyIcon(handle);
                    bitmap.Dispose();
                }
            }
            catch
            {
                return null;
            }
        }

        private static void DisposeCache()
        {
            if (_normal != null) { _normal.Dispose(); _normal = null; }
            if (_paused != null) { _paused.Dispose(); _paused = null; }
            if (_excluded != null) { _excluded.Dispose(); _excluded = null; }
            if (_off != null) { _off.Dispose(); _off = null; }
        }
    }
}
