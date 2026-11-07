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
        /// Watches for a window that is about to be closed. The picture has to
        /// be taken while the window still exists, so it is grabbed when the
        /// pointer reaches the close button (or while Alt+F4 is held) and kept
        /// until the window is gone; then the shards fly.
        /// </summary>

        private void WatchClose(IntPtr foreground, double now)
        {
            if (_closeWatch != IntPtr.Zero)
            {
                if (!Native.IsWindow(_closeWatch))
                {
                    IntPtr gone = _closeWatch;
                    _closeWatch = IntPtr.Zero;
                    _closeHiddenAt = 0;
                    Bitmap shot = _closeShot;
                    _closeShot = null;
                    if (shot != null)
                    {
                        // A per-program choice wins over the global one. The
                        // executable name was recorded while the window still
                        // existed; it cannot be read after it is gone.
                        int effect = _settings.GetCloseEffect(_closeExe);
                        if (effect < 0)
                            effect = _settings.CloseEffect;

                        if (effect == 3)
                        {
                            Log.Debug("Fade " + gone.ToInt64() + " '" + Native.GetWindowTitle(gone) + "'");
                            FadeOverlay.Play(shot, _closeFrame);
                        }
                        else if (effect == 2)
                        {
                            Log.Debug("Burn " + gone.ToInt64() + " '" + Native.GetWindowTitle(gone) + "'");
                            BurnOverlay.Play(shot, _closeFrame);
                        }
                        else if (effect == 1)
                        {
                            Log.Debug("Shatter " + gone.ToInt64() + " '" + Native.GetWindowTitle(gone) + "'");
                            ShatterOverlay.Play(shot, _closeFrame, _closePoint);
                        }
                        else
                        {
                            ShotPool.Return(shot);
                        }
                    }
                    return;
                }

                // Programs that keep running in the tray (Telegram, Steam,
                // Discord) do not destroy their window when it is closed: they
                // hide it. Nothing was closed, so the copy is rolled into a
                // tube and sent to the notification area instead of being
                // shattered. The wait is deliberately generous: a slow app can
                // take half a second to hide its window, and rolling a window
                // that is really exiting would look wrong.
                if (!Native.IsWindowVisible(_closeWatch))
                {
                    if (_closeHiddenAt == 0) _closeHiddenAt = now;
                    if (now - _closeHiddenAt < 0.5) return;
                    IntPtr hidden = _closeWatch;
                    _closeWatch = IntPtr.Zero;
                    _closeHiddenAt = 0;
                    Bitmap hiddenShot = _closeShot;
                    _closeShot = null;
                    if (hiddenShot != null)
                    {
                        Log.Debug("Roll to tray " + hidden.ToInt64() + " '" +
                            Native.GetWindowTitle(hidden) + "'");
                        if (RollAllowed(_closeExe))
                            RollOverlay.PlayToTray(hiddenShot, _closeFrame, _settings.RollDirection);
                        else
                            ShotPool.Return(hiddenShot);
                    }
                    return;
                }
                _closeHiddenAt = 0;

                // Keep the picture fresh while the pointer stays on the button.
                if (now - _closeShotAt > 8.0)
                {
                    ClearCloseWatch();
                    return;
                }
                if (now >= _closeCheckAt)
                {
                    _closeCheckAt = now + 0.05;
                    Native.POINT p;
                    if (Native.GetCursorPos(out p) && InCloseZone(_closeWatch, p) &&
                        now - _closeShotAt > 0.4)
                        TakeCloseShot(_closeWatch, p, now);
                }
                return;
            }

            if (foreground == IntPtr.Zero || Native.IsOwnProcessWindow(foreground) ||
                !Native.IsWindowVisible(foreground) || Native.IsIconic(foreground) ||
                IsShellWindowClass(Native.GetWindowClass(foreground)))
                return;

            if (now < _closeCheckAt) return;
            _closeCheckAt = now + 0.05;

            Native.POINT cursor;
            if (!Native.GetCursorPos(out cursor)) return;

            // Alt+F4 closes whatever is in front, wherever the pointer is.
            if (IsKeyDown(Native.VK_MENU) && IsKeyDown(Native.VK_F4))
            {
                TakeCloseShot(foreground, cursor, now);
                return;
            }

            if (InCloseZone(foreground, cursor))
                TakeCloseShot(foreground, cursor, now);
        }

        /// <summary>
        /// True when the point is over the window's close button, i.e. in the
        /// top-right corner of what the user actually sees.
        /// </summary>
        private static bool InCloseZone(IntPtr hwnd, Native.POINT p)
        {
            Native.RECT ext;
            if (!GetVisibleFrame(hwnd, out ext)) return false;
            int zoneW = Math.Min(70, Math.Max(44, ext.Width / 12));
            int zoneH = Math.Min(48, Math.Max(30, ext.Height / 20));
            return p.X >= ext.Right - zoneW && p.X <= ext.Right &&
                   p.Y >= ext.Top && p.Y <= ext.Top + zoneH;
        }

        private static bool GetVisibleFrame(IntPtr hwnd, out Native.RECT ext)
        {
            ext = new Native.RECT();
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out ext,
                    Marshal.SizeOf(typeof(Native.RECT))) != 0 ||
                ext.Width <= 0 || ext.Height <= 0)
            {
                if (!Native.GetWindowRect(hwnd, out ext)) return false;
            }
            return ext.Width > 0 && ext.Height > 0;
        }

        private void TakeCloseShot(IntPtr hwnd, Native.POINT point, double now)
        {
            Native.RECT ext;
            if (!GetVisibleFrame(hwnd, out ext)) return;
            // Use the whole virtual desktop, not the primary monitor: windows
            // on a second monitor to the left have negative coordinates.
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int vw = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            int vh = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (vw <= 0 || vh <= 0) return;
            if (ext.Width < 60 || ext.Height < 40 || ext.Width > 8192 || ext.Height > 8192) return;
            if (ext.Left < vx || ext.Top < vy ||
                ext.Right > vx + vw || ext.Bottom > vy + vh) return;

            Bitmap shot = null;
            try
            {
                shot = ShotPool.Rent(ext.Width, ext.Height,
                    System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(shot))
                {
                    g.CopyFromScreen(ext.Left, ext.Top, 0, 0,
                        new Size(ext.Width, ext.Height), CopyPixelOperation.SourceCopy);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Close snapshot failed: " + ex.GetType().Name + ": " + ex.Message);
                if (shot != null) ShotPool.Return(shot);
                return;
            }

            if (_closeShot != null) ShotPool.Return(_closeShot);
            _closeShot = shot;
            _closeFrame = ext;
            _closePoint = point;
            _closeExe = Native.GetWindowProcessName(hwnd);
            _closeShotAt = now;
            _closeWatch = hwnd;
        }

        private void ClearCloseWatch()
        {
            _closeWatch = IntPtr.Zero;
            _closeHiddenAt = 0;
            _closeExe = null;
            if (_closeShot != null)
            {
                ShotPool.Return(_closeShot);
                _closeShot = null;
            }
        }

        /// <summary>
        /// Watches for a window that is about to be minimized. The picture has
        /// to be taken while the window is still on screen, so it is grabbed
        /// when the pointer reaches the minimize button, when a taskbar button
        /// is pressed, or while Win / Alt+Space is held; the moment the window
        /// turns iconic the picture is rolled into a tube.
        /// </summary>
        private void WatchMinimize(IntPtr foreground, double now)
        {
            if (_minimizeWatch != IntPtr.Zero)
            {
                // Keep the armed picture alive while the pointer rests on the
                // minimize button, but do not grab it again: re-taking the
                // snapshot every couple of seconds would flicker the window
                // and burn CPU for no reason.
                if (now - _minimizeShotAt > 1.0)
                {
                    Native.POINT resting;
                    if (Native.GetCursorPos(out resting) && InMinimizeZone(_minimizeWatch, resting))
                        _minimizeShotAt = now;
                }

                if (!Native.IsWindow(_minimizeWatch) || now - _minimizeShotAt > 2.5)
                {
                    ClearMinimizeWatch();
                    return;
                }

                if (Native.IsIconic(_minimizeWatch))
                {
                    IntPtr target = _minimizeWatch;
                    Bitmap shot = _minimizeShot;
                    Native.RECT frame = _minimizeFrame;
                    RestoreWindowTransitions();
                    _minimizeWatch = IntPtr.Zero;
                    _minimizeShot = null;
                    ClearCloseWatch();
                    Log.Debug("Roll " + target.ToInt64());
                    if (RollAllowed(Native.GetWindowProcessName(target)))
                        RollOverlay.Play(shot, frame, target, _settings.RollDirection);
                    else if (shot != null)
                        ShotPool.Return(shot);
                    return;
                }

                // The user only hovered the button or pressed Win: the window
                // must get its own transitions back quickly.
                if (_minimizeTransitions && now - _minimizeTransitionAt > 1.5)
                    RestoreWindowTransitions();
                return;
            }

            if (now < _minimizeCheckAt) return;
            _minimizeCheckAt = now + 0.04;

            Native.POINT cursor;
            if (!Native.GetCursorPos(out cursor)) return;

            bool down = IsKeyDown(Native.VK_LBUTTON);
            bool win = IsKeyDown(Native.VK_LWIN);
            bool altSpace = IsKeyDown(Native.VK_MENU) && IsKeyDown(Native.VK_SPACE);

            // The window whose minimize button is under the cursor. It is not
            // always the foreground one: clicking the button of a background
            // window minimizes it too, and the foreground may not have caught
            // up yet when the pointer arrives.
            //
            // The hit test walks the window stack, so it is only repeated when
            // the pointer moves, a button or key is used, or five times a
            // second — a pointer parked on the desktop costs almost nothing.
            bool moved = cursor.X != _minimizeCursor.X || cursor.Y != _minimizeCursor.Y;
            _minimizeCursor = cursor;
            IntPtr hover = IntPtr.Zero;
            if (moved || down || win || altSpace || now - _minimizeHoverAt > 0.2)
            {
                _minimizeHoverAt = now;
                hover = MinimizeButtonWindow(cursor);
            }
            bool hoverMinimize = hover != IntPtr.Zero && now - _minimizeShotAt > 0.35;

            bool normalForeground = false;
            if (down || win || altSpace)
            {
                normalForeground = foreground != IntPtr.Zero &&
                    !Native.IsOwnProcessWindow(foreground) && Native.IsWindowVisible(foreground) &&
                    !Native.IsCloaked(foreground) && !Native.IsIconic(foreground) &&
                    !IsShellWindowClass(Native.GetWindowClass(foreground));
            }

            bool pressedTaskbar = normalForeground && down && !_minimizeDown &&
                TaskbarFinder.Contains(cursor);
            bool winPressed = normalForeground && win && !_minimizeWinDown;
            bool winHeld = normalForeground && win && now - _minimizeShotAt > 0.5;
            bool menuMinimize = normalForeground && altSpace;

            _minimizeDown = down;
            _minimizeWinDown = win;

            if (hoverMinimize)
            {
                TakeMinimizeShot(hover, now);
                return;
            }
            if (pressedTaskbar || winPressed || winHeld || menuMinimize)
                TakeMinimizeShot(foreground, now);
        }

        /// <summary>
        /// The top-level window under the point, if the point is on that
        /// window's minimize button.
        /// </summary>
        private static IntPtr MinimizeButtonWindow(Native.POINT p)
        {
            IntPtr under = Native.WindowFromPoint(p);
            if (under == IntPtr.Zero) return IntPtr.Zero;
            under = Native.GetAncestor(under, Native.GA_ROOT);
            if (under == IntPtr.Zero) return IntPtr.Zero;
            if (Native.IsOwnProcessWindow(under) || !Native.IsWindowVisible(under) ||
                Native.IsCloaked(under) || Native.IsIconic(under) ||
                IsShellWindowClass(Native.GetWindowClass(under)))
                return IntPtr.Zero;
            return InMinimizeZone(under, p) ? under : IntPtr.Zero;
        }

        /// <summary>
        /// True when the point is over the window's minimize button. Classic
        /// windows answer WM_NCHITTEST with HTMINBUTTON, which is exact. Windows
        /// with a custom title bar (Windows 11 Explorer, Chromium, Electron)
        /// answer HTCLIENT everywhere and paint their own buttons, so for them
        /// the standard top-right caption geometry is used as a fallback.
        /// </summary>
        private static bool InMinimizeZone(IntPtr hwnd, Native.POINT p)
        {
            Native.RECT ext;
            if (!GetVisibleFrame(hwnd, out ext)) return false;

            int dpi = GetWindowDpi(hwnd);
            int buttonW = Native.ScaleForDpi(46, dpi);
            int buttonH = Native.ScaleForDpi(32, dpi);

            // Coarse pre-filter: the caption buttons live in the top-right
            // corner, so the cross-process hit test only runs there.
            int reach = Math.Min(300, Math.Max(140, buttonW * 3 + 20));
            int strip = buttonH + 8;
            if (p.Y < ext.Top || p.Y > ext.Top + strip) return false;
            if (p.X < ext.Right - reach || p.X > ext.Right) return false;

            UIntPtr hit;
            IntPtr lParam = Native.MakeLParam(p.X, p.Y);
            if (Native.SendMessageTimeout(hwnd, Native.WM_NCHITTEST, IntPtr.Zero, lParam,
                    Native.SMTO_BLOCK | Native.SMTO_ABORTIFHUNG, 30, out hit) != IntPtr.Zero)
            {
                ulong value = hit.ToUInt64();
                if (value == Native.HTMINBUTTON) return true;
                // A real non-client code means the window draws a classic
                // caption, where the hit test is authoritative.
                if (value != 1) return false;
            }

            long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE);
            if ((style & Native.WS_MINIMIZEBOX) == 0) return false;
            int fromRight = (style & Native.WS_MAXIMIZEBOX) != 0 ? 2 : 1;
            int left = ext.Right - (fromRight + 1) * buttonW - 4;
            int right = ext.Right - fromRight * buttonW + 4;
            return p.X >= left && p.X < right;
        }

        private static int GetWindowDpi(IntPtr hwnd)
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(hwnd);
                return dpi >= 48 ? (int)dpi : 96;
            }
            catch (EntryPointNotFoundException) { return 96; }
            catch (DllNotFoundException) { return 96; }
        }

        private void TakeMinimizeShot(IntPtr hwnd, double now)
        {
            Native.RECT frame;
            if (!GetVisibleFrame(hwnd, out frame)) return;
            Native.RECT ext = frame;
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int vw = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            int vh = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (vw <= 0 || vh <= 0) return;

            if (Native.IsZoomed(hwnd))
            {
                // A maximized window reports a frame that hangs off the screen
                // by its shadow borders; what the user sees is the work area of
                // its monitor, so that is what gets rolled up.
                Native.MONITORINFO info = new Native.MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
                IntPtr monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero && Native.GetMonitorInfo(monitor, ref info))
                    ext = info.rcWork;
            }

            // Clip to the desktop so a window hanging over the edge (or a
            // maximized frame's shadow) still yields a picture of the visible
            // part instead of a black band.
            Native.RECT visible = new Native.RECT();
            visible.Left = Math.Max(ext.Left, vx);
            visible.Top = Math.Max(ext.Top, vy);
            visible.Right = Math.Min(ext.Right, vx + vw);
            visible.Bottom = Math.Min(ext.Bottom, vy + vh);
            ext = visible;
            if (ext.Width < 60 || ext.Height < 60 || ext.Width > 8192 || ext.Height > 8192) return;
            if ((long)ext.Width * ext.Height > 40000000L) return;

            Bitmap shot = null;
            try
            {
                shot = ShotPool.Rent(ext.Width, ext.Height,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(shot))
                {
                    g.CopyFromScreen(ext.Left, ext.Top, 0, 0,
                        new Size(ext.Width, ext.Height), CopyPixelOperation.SourceCopy);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Minimize snapshot failed: " + ex.GetType().Name + ": " + ex.Message);
                if (shot != null) ShotPool.Return(shot);
                return;
            }

            // The click that minimizes the window may be handled while the
            // picture is being grabbed: the window is already gone and the grab
            // holds the desktop behind it. Rolling that would roll a picture of
            // the desktop, so a shot whose window vanished or moved is dropped
            // and the system's own animation plays instead.
            Native.RECT after;
            if (Native.IsIconic(hwnd) || Native.IsCloaked(hwnd) || !Native.IsWindowVisible(hwnd) ||
                !GetVisibleFrame(hwnd, out after) ||
                after.Left != frame.Left || after.Top != frame.Top ||
                after.Right != frame.Right || after.Bottom != frame.Bottom)
            {
                Log.Debug("Minimize snapshot dropped: window vanished or moved");
                ShotPool.Return(shot);
                return;
            }

            // The system's own minimize animation would slide a second copy of
            // the window to the taskbar; the tube replaces it. Turn it off
            // while the picture is armed and back on once it is used.
            if (_minimizeWatch != IntPtr.Zero && _minimizeWatch != hwnd)
                RestoreWindowTransitions();
            if (Native.SetWindowTransitions(hwnd, false))
            {
                _minimizeTransitions = true;
                _minimizeTransitionAt = now;
            }

            if (_minimizeShot != null) ShotPool.Return(_minimizeShot);
            _minimizeShot = shot;
            _minimizeFrame = ext;
            _minimizeShotAt = now;
            _minimizeWatch = hwnd;
            Log.Debug("Minimize armed " + hwnd.ToInt64() + " rect=" + ext.Left + "," + ext.Top +
                      " " + ext.Width + "x" + ext.Height);
        }

        private void RestoreWindowTransitions()
        {
            if (!_minimizeTransitions) return;
            _minimizeTransitions = false;
            IntPtr hwnd = _minimizeWatch;
            if (hwnd != IntPtr.Zero && Native.IsWindow(hwnd))
                Native.SetWindowTransitions(hwnd, true);
        }

        private void ClearMinimizeWatch()
        {
            RestoreWindowTransitions();
            _minimizeWatch = IntPtr.Zero;
            if (_minimizeShot != null)
            {
                ShotPool.Return(_minimizeShot);
                _minimizeShot = null;
            }
        }

        /// <summary>
        /// True when a press at <paramref name="p"/> began on one of the four
        /// borders of the window, i.e. it can only be an edge resize. The
        /// margin is in physical pixels and follows the window's DPI.
        /// </summary>
        internal static bool IsResizeEdge(Native.POINT p, Native.RECT rect, int margin)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;
            if (p.X < rect.Left - margin || p.X > rect.Right + margin ||
                p.Y < rect.Top - margin || p.Y > rect.Bottom + margin)
                return false;
            return p.X <= rect.Left + margin || p.X >= rect.Right - margin ||
                   p.Y <= rect.Top + margin || p.Y >= rect.Bottom - margin;
        }

        /// <summary>
        /// HTLEFT..HTBOTTOMRIGHT: the point is on a resize border. Frameless
        /// windows (Electron, Chrome) have no WS_THICKFRAME and draw their own
        /// border, so their answer to WM_NCHITTEST is the only reliable sign
        /// that the edge can be dragged.
        /// </summary>
        internal static bool IsResizeHit(long hit)
        {
            return hit >= 10 && hit <= 17;
        }

        internal static bool ReportsResizeBorder(IntPtr hwnd, Native.POINT p)
        {
            UIntPtr hit;
            IntPtr ok = Native.SendMessageTimeout(hwnd, Native.WM_NCHITTEST, IntPtr.Zero,
                Native.MakeLParam(p.X, p.Y), Native.SMTO_BLOCK | Native.SMTO_ABORTIFHUNG, 60,
                out hit);
            return ok != IntPtr.Zero && IsResizeHit((long)hit.ToUInt64());
        }

        /// <summary>
        /// Watches an edge resize and hands the final rectangle to a short
        /// size wave when the button goes up. A move never changes the size,
        /// so a drag started at the top border cannot trigger it.
        /// </summary>
        private void WatchResize(IntPtr foreground, double now)
        {
            // A slow release, or a window that finishes its resize after the
            // button is up (WinUI, Notepad), waits here for the final
            // rectangle. A new press cancels the pending wave.
            if (_resizePending)
            {
                IntPtr pending = _resizeWatch;
                if (IsKeyDown(Native.VK_LBUTTON) || pending == IntPtr.Zero ||
                    !Native.IsWindow(pending) || !Native.IsWindowVisible(pending) ||
                    Native.IsIconic(pending) || Native.IsZoomed(pending))
                {
                    ClearResizeWatch();
                    return;
                }

                Native.RECT current;
                if (!Native.GetWindowRect(pending, out current))
                {
                    ClearResizeWatch();
                    return;
                }

                if (Math.Abs(current.Width - _resizePendingRect.Width) > 1 ||
                    Math.Abs(current.Height - _resizePendingRect.Height) > 1)
                {
                    _resizePendingRect = current;
                    _resizePendingAt = now;
                }

                int pendingDelta = Math.Max(
                    Math.Abs(current.Width - _resizeStartRect.Width),
                    Math.Abs(current.Height - _resizeStartRect.Height));
                bool stable = now - _resizePendingAt >= 0.12;
                if ((pendingDelta >= 2 && stable) || now >= _resizePendingUntil)
                {
                    double pendingSpeed = _resizePendingSpeed;
                    ClearResizeWatch();
                    if (pendingDelta >= 2 && current.Width >= 80 && current.Height >= 50)
                        BeginResizeSettle(pending, current, pendingSpeed, pendingDelta);
                    else
                        Log.Debug("Resize settle skipped: no size change");
                }
                return;
            }

            bool down = IsKeyDown(Native.VK_LBUTTON);

            if (_resizeWatch != IntPtr.Zero)
            {
                if (!down)
                {
                    IntPtr hwnd = _resizeWatch;
                    Native.RECT last = _resizeLastRect;
                    bool armed = _resizeArmed;
                    double speed = _resizeSpeed;
                    int delta = Math.Max(
                        Math.Abs(last.Width - _resizeStartRect.Width),
                        Math.Abs(last.Height - _resizeStartRect.Height));

                    if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd) ||
                        Native.IsIconic(hwnd) || Native.IsZoomed(hwnd) ||
                        last.Width < 80 || last.Height < 50)
                    {
                        ClearResizeWatch();
                        Log.Debug("Resize settle skipped armed=" + armed +
                            " speed=" + speed.ToString("f0"));
                        return;
                    }

                    if (delta >= 2 && speed >= 40.0)
                    {
                        ClearResizeWatch();
                        BeginResizeSettle(hwnd, last, speed, delta);
                        return;
                    }

                    // Slow drag or a resize that has not landed yet: keep the
                    // window under watch and use the final rectangle.
                    _resizePending = true;
                    _resizePendingRect = last;
                    _resizePendingAt = now;
                    _resizePendingUntil = now + (delta >= 2 ? 0.30 : 0.70);
                    _resizePendingSpeed = speed;
                    Log.Debug("Resize settle waiting delta=" + delta +
                        " speed=" + speed.ToString("f0"));
                    return;
                }

                if (!Native.IsWindow(_resizeWatch) ||
                    !Native.IsWindowVisible(_resizeWatch) ||
                    Native.IsIconic(_resizeWatch) || Native.IsZoomed(_resizeWatch))
                {
                    ClearResizeWatch();
                    return;
                }

                if (now < _resizeCheckAt) return;
                _resizeCheckAt = now + 0.03;

                Native.RECT current;
                if (!Native.GetWindowRect(_resizeWatch, out current))
                {
                    ClearResizeWatch();
                    return;
                }

                int dw = Math.Abs(current.Width - _resizeLastRect.Width);
                int dh = Math.Abs(current.Height - _resizeLastRect.Height);
                if (dw > 1 || dh > 1)
                {
                    double dt = now - _resizeLastAt;
                    if (dt < 0.001) dt = 0.001;
                    double instant = Math.Sqrt((double)dw * dw + (double)dh * dh) / dt;
                    if (instant > 4000.0) instant = 4000.0;
                    _resizeSpeed = _resizeSpeed * 0.55 + instant * 0.45;
                    _resizeLastAt = now;
                }
                else if (now - _resizeLastAt > 0.15)
                {
                    _resizeSpeed *= 0.55;
                }

                if (Math.Abs(current.Width - _resizeStartRect.Width) > 2 ||
                    Math.Abs(current.Height - _resizeStartRect.Height) > 2)
                    _resizeArmed = true;
                _resizeLastRect = current;
                return;
            }

            if (!down || now < _resizeCheckAt) return;
            _resizeCheckAt = now + 0.04;

            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;

            IntPtr target = foreground;
            if (target == IntPtr.Zero || !Native.IsWindow(target)) return;
            if (Native.IsOwnProcessWindow(target) && target != _ownTarget) return;
            if (!Native.IsWindowVisible(target) || Native.IsIconic(target) ||
                Native.IsZoomed(target) || Native.IsCloaked(target))
                return;
            if (IsShellWindowClass(Native.GetWindowClass(target))) return;

            long style = Native.GetWindowLongPtr(target, Native.GWL_STYLE);
            if ((style & Native.WS_THICKFRAME) == 0 && !ReportsResizeBorder(target, p))
                return;

            Native.RECT rect;
            if (!Native.GetWindowRect(target, out rect)) return;
            int margin = Native.ScaleForDpi(8, GetWindowDpi(target));
            if (!IsResizeEdge(p, rect, margin)) return;

            Log.Debug("Resize watch " + target.ToInt64() + " at " + p.X + "," + p.Y +
                " rect=" + rect.Left + "," + rect.Top + " " + rect.Width + "x" + rect.Height);
            _resizeWatch = target;
            _resizeStartRect = rect;
            _resizeLastRect = rect;
            _resizeLastAt = now;
            _resizeArmed = false;
            _resizeSpeed = 0;
            _resizePending = false;
        }

        private void ClearResizeWatch()
        {
            _resizeWatch = IntPtr.Zero;
            _resizeArmed = false;
            _resizeSpeed = 0;
            _resizePending = false;
            _resizePendingRect = new Native.RECT();
            _resizePendingAt = 0;
            _resizePendingUntil = 0;
            _resizePendingSpeed = 0;
        }

    }
}
