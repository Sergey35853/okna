using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Okna
{
    /// <summary>
    /// Keeps one snapshot bitmap for the next close or minimize animation.
    /// The pictures are large (a 4K window is 33 MB), and the close and
    /// minimize watchers replace or drop them often, so reusing the buffer
    /// removes a visible allocation spike right before the animation starts.
    /// A bitmap handed to an overlay belongs to that overlay and is never
    /// returned here; only pictures that are dropped or replaced come back.
    /// </summary>
    internal static class ShotPool
    {
        private static readonly object _lock = new object();
        private static Bitmap _spare;

        public static Bitmap Rent(int width, int height, PixelFormat format)
        {
            Bitmap shot = null;
            lock (_lock)
            {
                if (_spare != null && _spare.Width == width && _spare.Height == height &&
                    _spare.PixelFormat == format)
                {
                    shot = _spare;
                    _spare = null;
                }
            }
            if (shot == null) shot = new Bitmap(width, height, format);
            return shot;
        }

        public static void Return(Bitmap shot)
        {
            if (shot == null) return;
            lock (_lock)
            {
                if (_spare == null)
                {
                    _spare = shot;
                    return;
                }
            }
            try { shot.Dispose(); } catch { }
        }

        public static void Clear()
        {
            Bitmap spare;
            lock (_lock)
            {
                spare = _spare;
                _spare = null;
            }
            if (spare == null) return;
            try { spare.Dispose(); } catch { }
        }
    }
}
