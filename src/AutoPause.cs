using System;

namespace Okna
{
    /// <summary>
    /// Decides when the effects should step aside on their own: a full-screen
    /// game or video, a remote desktop session, or battery saver mode. The
    /// master switch in the settings stays untouched; this is a temporary
    /// pause that ends by itself.
    /// </summary>
    internal sealed class AutoPause
    {
        private string _reason;
        private double _nextCheck;

        /// <summary>Human readable reason, or null when nothing is in the way.</summary>
        public string Reason
        {
            get { return _reason; }
        }

        public bool Paused
        {
            get { return _reason != null; }
        }

        public void Update(IntPtr foreground, double now)
        {
            if (now < _nextCheck) return;
            _nextCheck = now + 0.4;

            string reason = null;
            if (Native.IsRemoteSession())
                reason = "Удалённый рабочий стол";
            else if (Native.IsFullscreenWindow(foreground))
                reason = "Полноэкранное приложение";
            else if (Native.IsBatterySaverOn())
                reason = "Экономия энергии";

            if (reason == _reason) return;
            _reason = reason;
            if (reason == null)
                Log.Debug("Auto pause off");
            else
                Log.Debug("Auto pause: " + reason);
        }
    }
}
