using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Okna
{
    internal sealed partial class JellyEngine : IDisposable
    {
        /// <summary>
        /// Watches the foreground window for the transitions that deserve a
        /// shake: becoming maximized, being restored down, and coming back
        /// from the taskbar. The state is tracked per window, so switching to
        /// a window that was already maximized (Alt+Tab) does not count.
        /// </summary>

        private void WatchWindowState(IntPtr foreground, double now)
        {
            if (foreground == IntPtr.Zero || Native.IsOwnProcessWindow(foreground) ||
                !Native.IsWindowVisible(foreground))
            {
                // The window we were watching may have just been minimized;
                // remember it so its return from the taskbar can be spotted.
                if (_zoomWatch != IntPtr.Zero && Native.IsWindow(_zoomWatch) && Native.IsIconic(_zoomWatch))
                    _minimizedWatch = _zoomWatch;
                _zoomWatch = IntPtr.Zero;
                _shakePending = IntPtr.Zero;
                return;
            }

            if (Native.IsIconic(foreground))
            {
                _minimizedWatch = foreground;
                _zoomWatch = IntPtr.Zero;
                _shakePending = IntPtr.Zero;
                return;
            }

            Native.RECT frame;
            bool hasFrame = Native.GetWindowRect(foreground, out frame);
            bool zoomed = IsMaximizedLike(foreground, hasFrame ? frame : new Native.RECT());

            // Back from the taskbar: the window was hidden, so it could not
            // shake while minimized — it shakes now that it is visible again.
            if (foreground == _minimizedWatch)
            {
                _minimizedWatch = IntPtr.Zero;
                _zoomWatch = foreground;
                _zoomWatchState = zoomed;
                _zoomWatchRect = frame;
                ScheduleShake(foreground, now, frame);
                return;
            }

            if (foreground != _zoomWatch)
            {
                // A plain switch to another window: keep tracking the old one
                // only if it is minimized and may still come back.
                if (_zoomWatch != IntPtr.Zero && Native.IsWindow(_zoomWatch) && Native.IsIconic(_zoomWatch))
                    _minimizedWatch = _zoomWatch;
                else if (_minimizedWatch != IntPtr.Zero &&
                         (!Native.IsWindow(_minimizedWatch) || !Native.IsIconic(_minimizedWatch)))
                    _minimizedWatch = IntPtr.Zero;

                _zoomWatch = foreground;
                _zoomWatchState = zoomed;
                _zoomWatchRect = frame;
                _shakePending = IntPtr.Zero;
                return;
            }

            if (zoomed != _zoomWatchState)
            {
                _zoomWatchState = zoomed;
                Log.Debug("Zoom " + (zoomed ? "max" : "restore") + " " + foreground.ToInt64() +
                    " '" + Native.GetWindowTitle(foreground) + "' rect=" + frame.Left + "," +
                    frame.Top + "," + frame.Width + "x" + frame.Height);
                // The frame from the previous look tells whether the window has
                // already moved: apps that resize themselves (Electron) have,
                // the shell's zoom transition has not yet.
                ScheduleShake(foreground, now, _zoomWatchRect);
            }
            _zoomWatchRect = frame;
        }

        /// <summary>
        /// True when the window looks maximized. Most windows say so through
        /// <see cref="Native.IsZoomed"/>, but Electron/Chromium apps "maximize"
        /// by resizing themselves to the work area: their IsZoomed stays false,
        /// so the frame is compared with the monitor's work area as well.
        /// </summary>
        private static bool IsMaximizedLike(IntPtr hwnd, Native.RECT rect)
        {
            if (Native.IsZoomed(hwnd)) return true;

            Native.RECT work;
            if (!Native.GetWorkArea(hwnd, out work)) return false;

            // A maximized window keeps its invisible resize border outside the
            // work area (about eight pixels a side at 100 % scaling).
            double scale = Math.Max(1.0, Native.GetDpiForWindow(hwnd) / 96.0);
            int tol = (int)Math.Ceiling(12 * scale);
            return Math.Abs(rect.Left - work.Left) <= tol &&
                   Math.Abs(rect.Top - work.Top) <= tol &&
                   Math.Abs(rect.Right - work.Right) <= tol &&
                   Math.Abs(rect.Bottom - work.Bottom) <= tol;
        }

        private static bool SameRect(Native.RECT a, Native.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top &&
                   a.Right == b.Right && a.Bottom == b.Bottom;
        }

        /// <summary>
        /// Queues a shake. It fires as soon as the window frame has changed
        /// since <paramref name="before"/>, and no later than the deadline —
        /// which is short enough that the wobble reads as a reaction to the
        /// click, not as something that happens afterwards.
        /// </summary>
        private void ScheduleShake(IntPtr hwnd, double now, Native.RECT before)
        {
            _shakePendingRect = before;
            _shakePending = hwnd;
            _shakePendingAt = now + 0.30;
            _shakeExpiresAt = now + 1.5;
        }

    }
}
