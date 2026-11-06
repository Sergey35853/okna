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
        /// The maximized/restored window is replaced for a moment by a warped
        /// copy of itself: the copy sways like a slab of jelly instead of the
        /// whole window being shoved around rigidly. The real window is hidden
        /// (alpha 1) for the duration, so the copy's deformed silhouette is
        /// what the user sees. Anything the overlay cannot capture falls back
        /// to the old physical shake.
        /// </summary>

        private void StartJelly(IntPtr explicitTarget)
        {
            StartJelly(explicitTarget, false);
        }

        private void StartJelly(IntPtr explicitTarget, bool userRequest)
        {
            // Economy mode never takes a live screen grab: the wobble is a
            // one-shot capture, and that is exactly the cost it avoids.
            if (_settings.Economy) return;

            IntPtr fg = explicitTarget;
            bool explicitOwn = fg != IntPtr.Zero;
            if (!explicitOwn)
            {
                fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero || Native.IsOwnProcessWindow(fg))
                    fg = _lastExternal;
            }
            if (fg == IntPtr.Zero) return;
            if (!explicitOwn && Native.IsOwnProcessWindow(fg)) return;
            if (!Native.IsWindow(fg) || !Native.IsWindowVisible(fg) || Native.IsIconic(fg)) return;

            // A program profile may switch the shake off. An explicit request
            // from the tray or the settings window still wins.
            if (!userRequest)
            {
                int profileEffect;
                bool profileBend;
                bool profileShake;
                bool profileRoll;
                string exe = Native.GetWindowProcessName(fg);
                if (_settings.GetAppProfile(exe, out profileEffect, out profileBend,
                        out profileShake, out profileRoll) && !profileShake)
                {
                    Log.Debug("Jelly skipped: shake is off for " + (exe ?? "this window"));
                    return;
                }
            }
            ClearResizeWatch();

            Native.RECT rect;
            if (!Native.GetWindowRect(fg, out rect)) return;
            if (rect.Width < 80 || rect.Height < 40) return;

            // Some windows (the Windows 11 Notepad among them) take over a
            // second to render themselves into a bitmap. Their wobble would
            // arrive after the window has already settled, which reads as a
            // glitch rather than a reaction; once such a class is known, the
            // wobble is skipped for it and the window keeps its own animation.
            string cls = Native.GetWindowClass(fg);
            bool slow;
            lock (_slowLock) slow = cls.Length > 0 && _slowCapture.Contains(cls);
            if (slow)
            {
                Log.Debug("Jelly skipped: '" + cls + "' captures slowly");
                return;
            }

            // The window rectangle changes before the window has arrived: the
            // shell animates it into place and the app repaints at its new
            // size. Wait until the picture stops changing, so the copy shows
            // the finished window instead of a half-way or previous frame.
            if (!WaitUntilSettled(fg, rect))
            {
                Log.Debug("Jelly skipped: window still changing");
                return;
            }
            if (!Native.IsWindow(fg) || !Native.GetWindowRect(fg, out rect)) return;
            if (rect.Width < 80 || rect.Height < 40) return;

            // One frozen frame is enough for a one-shot wobble: the window is
            // not going to change while it jiggles. A window that cannot be
            // captured in time is skipped: a wobble that arrives late looks
            // like a glitch of its own.
            bool failed;
            if (!CaptureOnce(fg, rect, cls, out failed))
            {
                if (failed)
                {
                    Log.Debug("Jelly capture failed; using the physical shake");
                    StartShake(explicitTarget);
                }
                else
                {
                    Log.Debug("Jelly skipped: no frame in time");
                }
                return;
            }

            _target = fg;
            _shakeRect = rect;
            _shakeZoomed = Native.IsZoomed(fg);
            // The copy is drawn exactly where the window already is. Keep the
            // engine's notion of the target position in sync: the shutdown path
            // restores the overlay at (_x, _y), and stale zeroes would teleport
            // the real window to the top-left corner.
            _x = rect.Left;
            _y = rect.Top;
            _targetX = rect.Left;
            _targetY = rect.Top;
            _vx = 0;
            _vy = 0;

            // The copy goes up first, undeformed, so it looks exactly like the
            // window; only then is the real window made invisible. The other
            // order would flash the desktop for a frame. The frame was taken
            // into the one-shot overlay, so the jelly is drawn from there.
            _shot.Show();
            _shot.RenderJelly(rect.Left, rect.Top, 0, 0, false);
            if (!ClearTarget())
            {
                _shot.Hide();
                _target = IntPtr.Zero;
                Log.Debug("Jelly takeover failed; using the physical shake");
                StartShake(explicitTarget);
                return;
            }

            _overlayActive = true;
            _jellyStart = DateTime.UtcNow;
            _jellyLastRender = 0;
            _jellyFrames = 0;
            _mode = Mode.Jelly;
            SetStatus("Желе");
            Log.Debug("Jelly " + fg.ToInt64() + " '" + Native.GetWindowTitle(fg) +
                "' zoomed=" + _shakeZoomed + " rect=" + rect.Left + "," + rect.Top +
                "," + rect.Width + "x" + rect.Height);
        }

        private void TickJelly(double now)
        {
            if (_target == IntPtr.Zero || !Native.IsWindow(_target) ||
                !Native.IsWindowVisible(_target) || Native.IsIconic(_target) ||
                Native.IsZoomed(_target) != _shakeZoomed)
            {
                EndJelly();
                return;
            }

            // A click or Escape means the user wants the window back now.
            if (IsKeyDown(Native.VK_LBUTTON) || IsKeyDown(Native.VK_ESCAPE))
            {
                EndJelly();
                return;
            }

            double elapsed = (DateTime.UtcNow - _jellyStart).TotalSeconds;
            if (elapsed >= 1.5)
            {
                EndJelly();
                return;
            }

            // ~80 frames a second is smooth and much cheaper than the drag
            // path's full rate.
            if (now - _jellyLastRender < 0.012) return;
            _jellyLastRender = now;

            // Jelly does not spring back along one axis. It keeps sloshing in
            // several directions at once, each at its own rate, and the wobble
            // dies away smoothly instead of stopping abruptly.
            double rise = 1.0 - Math.Exp(-elapsed / 0.05);
            double env = rise * Math.Exp(-elapsed / 0.50);
            double w = _shakeRect.Width;
            double h = _shakeRect.Height;
            double phase = 2.0 * Math.PI * elapsed;
            // Same normalisation as the release wobble: the default 70 is
            // unchanged, the top of the slider sloshes much harder.
            double visc = Math.Max(0.6, _settings.JellyFactor / 1.1);
            double ax = 0.010 * w * env * Math.Cos(phase * 1.90 + 0.8) * visc;
            double ay = 0.022 * h * env * Math.Cos(phase * 2.20) * visc;
            double ox = 0.0025 * w * env * Math.Cos(phase * 1.90 + 1.4) * visc;
            double oy = 0.0025 * h * env * Math.Cos(phase * 2.20 + 0.5) * visc;

            _shot.RenderJelly(
                _shakeRect.Left + (int)Math.Round(ox),
                _shakeRect.Top + (int)Math.Round(oy),
                ax, ay, false);
            _jellyFrames++;
        }

        private void EndJelly()
        {
            if (_overlayActive)
                Log.Debug("Jelly end frames=" + _jellyFrames + " elapsed=" +
                    (DateTime.UtcNow - _jellyStart).TotalMilliseconds.ToString("f0") + "ms");

            // Draw the copy undeformed before revealing the real window, so the
            // hand-over is invisible.
            if (_overlayActive && _target != IntPtr.Zero && Native.IsWindow(_target))
                _shot.RenderJelly(_shakeRect.Left, _shakeRect.Top, 0, 0, false);

            RestoreTargetVisibility();
            if (_overlayActive)
            {
                _shot.Hide();
                _overlayActive = false;
            }
            ResetEngine(_settings.Enabled ? "Готово" : "Пауза");
        }

    }
}
