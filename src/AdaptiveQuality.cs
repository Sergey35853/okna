using System;

namespace Okna
{
    /// <summary>
    /// Frame-time governor for the bend overlay. When frames take too long,
    /// the overlay builds taller warp strips (fewer of them), which lowers
    /// the per-frame cost without changing the shape; when frames are fast
    /// again it returns to pixel-perfect strips after a few quiet seconds.
    /// Level 0 is the full quality, 2 is the cheapest.
    /// </summary>
    internal static class AdaptiveQuality
    {
        public const int Best = 0;
        public const int Reduced = 1;
        public const int Lowest = 2;

        private const double SlowMs = 24.0;
        private const double FastMs = 12.0;
        private const double SlowHold = 0.8;
        private const double FastHold = 3.0;
        private const int MaxStrip = 12;

        private static int _level;
        private static double _average;
        private static double _slowSince;
        private static double _fastSince;
        private static double _lastChange;

        public static int Level
        {
            get { return _level; }
        }

        public static double AverageMs
        {
            get { return _average; }
        }

        public static void Reset()
        {
            _level = Best;
            _average = 0;
            _slowSince = 0;
            _fastSince = 0;
            _lastChange = 0;
        }

        /// <summary>
        /// Feeds one frame duration in milliseconds. The average is smoothed
        /// so a single hitch does not drop the quality, and recovery is
        /// deliberately slower than the fall: it is better to stay a little
        /// coarse than to oscillate between levels.
        /// </summary>
        public static void Sample(double frameMs, double now)
        {
            if (frameMs <= 0 || frameMs > 1000) return;
            _average = _average <= 0 ? frameMs : _average * 0.9 + frameMs * 0.1;

            if (_average > SlowMs)
            {
                _fastSince = 0;
                if (_slowSince == 0) _slowSince = now;
                if (_level < Lowest && now - _slowSince >= SlowHold && now - _lastChange >= SlowHold)
                {
                    _level++;
                    _lastChange = now;
                    _slowSince = 0;
                }
            }
            else if (_average < FastMs)
            {
                _slowSince = 0;
                if (_fastSince == 0) _fastSince = now;
                if (_level > Best && now - _fastSince >= FastHold && now - _lastChange >= FastHold)
                {
                    _level--;
                    _lastChange = now;
                    _fastSince = 0;
                }
            }
            else
            {
                _slowSince = 0;
                _fastSince = 0;
            }
        }

        /// <summary>
        /// Height of one warp strip for a quality level. Kept pure so the
        /// headless checks can exercise every level without a desktop.
        /// </summary>
        public static int StripHeight(int baseHeight, int level)
        {
            if (baseHeight < 1) baseHeight = 1;
            if (level <= Best) return baseHeight;
            int multiplier = level == Reduced ? 2 : 4;
            int height = baseHeight * multiplier;
            return height > MaxStrip ? MaxStrip : height;
        }
    }
}
