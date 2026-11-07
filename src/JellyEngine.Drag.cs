using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Okna
{
    internal sealed partial class JellyEngine : IDisposable
    {
        private void TryBeginCandidate()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;

            IntPtr hwnd = Native.WindowFromPoint(p);
            IntPtr root = Native.GetAncestor(hwnd, 2);
            if (root == IntPtr.Zero || !Native.IsWindow(root)) return;
            // Own-process windows are normally off limits. The settings form
            // registers itself as the one exception, so the jelly can be tried
            // on the real settings window instead of a fake preview.
            bool ownTarget = root == _ownTarget;
            if (Native.IsOwnProcessWindow(root) && !ownTarget) return;
            if (!Native.IsWindowVisible(root) || Native.IsIconic(root) || Native.IsZoomed(root) || !Native.IsWindowEnabled(root))
                return;

            string cls = Native.GetWindowClass(root);
            if (IsShellWindowClass(cls)) return;

            Native.RECT rect;
            if (!Native.GetWindowRect(root, out rect)) return;
            if (rect.Width < 80 || rect.Height < 40) return;

            long exStyle = Native.GetWindowLongPtr(root, Native.GWL_EXSTYLE);
            if ((exStyle & Native.WS_EX_TOOLWINDOW) != 0) return;
            if ((exStyle & Native.WS_EX_NOACTIVATE) != 0) return;
            if ((exStyle & Native.WS_EX_TRANSPARENT) != 0) return;

            if (Native.IsCloaked(root)) return;

            uint pid;
            Native.GetWindowThreadProcessId(root, out pid);
            if (pid == Native.GetCurrentProcessId() && !ownTarget) return;

            // An elevated target (e.g. Rufus run as administrator) cannot be
            // moved by a non-elevated Okna due to Windows UIPI: SetWindowPos
            // returns false and the jelly drag cannot drive the window. Skip it
            // with a clear debug message; the user can run Okna as administrator
            // to get jelly on elevated windows too.
            bool targetElevated;
            Native.IsProcessElevated(pid, out targetElevated);
            if (targetElevated && !ownTarget) return;
            UIntPtr hit = (UIntPtr)Native.HTCAPTION;
            IntPtr ok = Native.SendMessageTimeout(root, Native.WM_NCHITTEST, IntPtr.Zero,
                Native.MakeLParam(p.X, p.Y), Native.SMTO_BLOCK | Native.SMTO_ABORTIFHUNG, 60, out hit);
            if (ok == IntPtr.Zero)
            {
                // UIPI blocks the call for elevated targets (e.g. Rufus as
                // administrator): we cannot query the hit test, so assume the
                // title bar and let the position-based zone check below filter
                // clicks outside it.
                hit = (UIntPtr)Native.HTCAPTION;
            }

            // Classic windows report HTCAPTION for their title bar. Browsers, Electron
            // and UWP apps draw their own header and report HTCLIENT, so accept a press
            // in the top strip of the window as a candidate as well. The actual takeover
            // only happens once the window really starts moving (see Tick), so clicks on
            // tabs, buttons and toolbars stay untouched.
            if (hit.ToUInt64() != Native.HTCAPTION)
            {
                int zone = Math.Min(72, Math.Max(36, rect.Height / 6));
                if (p.Y - rect.Top > zone) return;
            }

            _target = root;
            _startRect = rect;
            _pressRect = rect;
            _startCursor = p;
            _baseWidth = rect.Width;
            _baseHeight = rect.Height;
            _canResize = (Native.GetWindowLongPtr(root, Native.GWL_STYLE) & Native.WS_THICKFRAME) != 0;
            _mode = Mode.Candidate;
            _followTicks = 0;
            Log.Debug("Candidate " + root.ToInt64() + " '" + Native.GetWindowTitle(root) + "' rect=" +
                rect.Left + "," + rect.Top + " " + rect.Width + "x" + rect.Height +
                " hit=" + hit.ToUInt64() + " pid=" + pid + " elevated=" + targetElevated);
        }

        private bool TakeOver(Native.RECT current)
        {
            if (!Native.IsWindow(_target)) return false;

            UIntPtr result;
            IntPtr ok = Native.SendMessageTimeout(_target, Native.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero,
                Native.SMTO_BLOCK | Native.SMTO_ABORTIFHUNG, 80, out result);
            if (ok == IntPtr.Zero)
            {
                // UIPI blocks the call for elevated targets: cannot cancel the
                // target's modal state, but proceed anyway — the overlay draws
                // on top regardless.
                Log.Debug("WM_CANCELMODE failed for " + _target.ToInt64() + " (elevated target?)");
            }

            // Continue from where the window actually is: the native drag may have
            // already moved it a few pixels before we stopped it.
            Native.RECT after;
            if (!Native.GetWindowRect(_target, out after)) after = current;
            Native.POINT cursor;
            if (!Native.GetCursorPos(out cursor)) cursor = _startCursor;

            _startRect = after;
            _startCursor = cursor;
            _x = after.Left;
            _y = after.Top;
            _vx = 0;
            _vy = 0;
            _targetX = after.Left;
            _targetY = after.Top;
            _baseWidth = after.Width;
            _baseHeight = after.Height;
            _lastCursorX = cursor.X;
            _lastCursorY = cursor.Y;
            _lastCursorChange = DateTime.UtcNow;
            _cursorVx = 0;
            _cursorVy = 0;
            _recentDragSpeed = 0;
            _recentDirX = 0;
            _recentDirY = 0;
            _wobblePhase = 0;
            _dragWobbleAmp = 0;
            _dragPerpX = 0;
            _dragPerpY = 0;
            _releaseAlongAmp = 0;
            _releasePerpAmp = 0;
            _releaseSizeAmp = 0;
            _lastRequestedX = int.MinValue;
            _lastRequestedY = int.MinValue;
            _lastRequestedW = int.MinValue;
            _lastRequestedH = int.MinValue;
            _frame = 0;
            _followTicks = 0;
            _resizeOnly = false;
            ClearResizeWatch();
            _overlayActive = false;
            _overlayPending = _settings.Bend && !_settings.Economy && Active && _appBend;
            _captureTried = false;
            _captureOk = false;
            _following = false;
            _windowUnderOverlay = false;
            _lastRender = DateTime.UtcNow.AddSeconds(-1);
            _verifyDone = false;
            _verifyStage = 0;
            _verifyUntil = DateTime.UtcNow.AddMilliseconds(25);
            _verifyRect = after;
            _grabX = 0;
            _grabY = 0;
            _bendX = 0;
            _bendY = 0;
            return true;
        }

        private void UpdateDragWobble(Native.POINT cursor, double dt)
        {
            if (dt <= 0.0005) dt = 0.008;
            DateTime utcNow = DateTime.UtcNow;
            bool moved = cursor.X != _lastCursorX || cursor.Y != _lastCursorY;

            if (moved)
            {
                double moveSeconds = (utcNow - _lastCursorChange).TotalSeconds;
                if (moveSeconds < 0.002) moveSeconds = 0.002;
                if (moveSeconds > 0.20) moveSeconds = 0.20;

                double rawVx = (cursor.X - _lastCursorX) / moveSeconds;
                double rawVy = (cursor.Y - _lastCursorY) / moveSeconds;
                if (rawVx > 5000) rawVx = 5000;
                if (rawVx < -5000) rawVx = -5000;
                if (rawVy > 5000) rawVy = 5000;
                if (rawVy < -5000) rawVy = -5000;

                _cursorVx = _cursorVx * 0.60 + rawVx * 0.40;
                _cursorVy = _cursorVy * 0.60 + rawVy * 0.40;
                _lastCursorX = cursor.X;
                _lastCursorY = cursor.Y;
                _lastCursorChange = utcNow;
            }
            else
            {
                double idle = (utcNow - _lastCursorChange).TotalSeconds;
                if (idle > 0.055)
                {
                    double decay = Math.Exp(-dt / 0.14);
                    _cursorVx *= decay;
                    _cursorVy *= decay;
                }
            }

            double speed = Math.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy);
            if (speed > 40.0)
            {
                _recentDirX = _cursorVx / speed;
                _recentDirY = _cursorVy / speed;
            }
            _recentDragSpeed = Math.Max(speed, _recentDragSpeed * Math.Exp(-dt / 0.50));

            double targetAmp = Math.Min(28.0, 5.0 + _recentDragSpeed * 0.055);
            if (_recentDragSpeed < 50.0)
                targetAmp *= _recentDragSpeed / 50.0;
            _dragWobbleAmp += (targetAmp - _dragWobbleAmp) * Math.Min(0.35, dt * 16.0);
            if (_dragWobbleAmp < 0.05) _dragWobbleAmp = 0;

            if (speed > 25.0)
            {
                double px = -_cursorVy / speed;
                double py = _cursorVx / speed;
                _dragPerpX = _dragPerpX * 0.80 + px * 0.20;
                _dragPerpY = _dragPerpY * 0.80 + py * 0.20;
                double len = Math.Sqrt(_dragPerpX * _dragPerpX + _dragPerpY * _dragPerpY);
                if (len > 0.001)
                {
                    _dragPerpX /= len;
                    _dragPerpY /= len;
                }
            }

            _wobblePhase += 2.0 * Math.PI * 4.2 * dt;
            if (_wobblePhase > 2.0 * Math.PI) _wobblePhase -= 2.0 * Math.PI;
        }

        private void BeginSettle(double now, bool allowWobble)
        {
            _mode = Mode.Settling;
            _resizeOnly = false;
            _releaseTime = DateTime.UtcNow;
            _releaseAlongAmp = 0;
            _releasePerpAmp = 0;
            _releaseSizeAmp = 0;

            if (allowWobble && _settings.ReleaseWobble)
            {
                double cursorSpeed = Math.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy);
                double springSpeed = Math.Sqrt(_vx * _vx + _vy * _vy);
                _releaseSpeed = Math.Max(_recentDragSpeed, Math.Max(cursorSpeed, springSpeed));

                if (_releaseSpeed > 85.0)
                {
                    if (cursorSpeed > 40.0)
                    {
                        _releaseDirX = _cursorVx / cursorSpeed;
                        _releaseDirY = _cursorVy / cursorSpeed;
                    }
                    else if (Math.Abs(_recentDirX) + Math.Abs(_recentDirY) > 0.1)
                    {
                        _releaseDirX = _recentDirX;
                        _releaseDirY = _recentDirY;
                    }
                    else if (springSpeed > 0.1)
                    {
                        _releaseDirX = _vx / springSpeed;
                        _releaseDirY = _vy / springSpeed;
                    }
                    else
                    {
                        _releaseDirX = 1;
                        _releaseDirY = 0;
                    }
                    _releasePerpX = -_releaseDirY;
                    _releasePerpY = _releaseDirX;

                    // The slider keeps working above the old 100: the top of
                    // the new range makes the release noticeably gooier. The
                    // factors are normalised so the default 70 feels exactly as
                    // before.
                    double visc = Math.Max(0.6, _settings.JellyFactor / 1.1);
                    double alongCap = Math.Min(80.0, 42.0 * visc);
                    double perpCap = Math.Min(56.0, 30.0 * visc);
                    double sizeCap = Math.Min(36.0, 20.0 * visc);
                    _releaseAlongAmp = Math.Min(alongCap, (12.0 + _releaseSpeed * 0.055) * visc);
                    _releasePerpAmp = Math.Min(perpCap, (9.0 + _releaseSpeed * 0.045) * visc);
                    if (_canResize)
                        _releaseSizeAmp = Math.Min(sizeCap, (8.0 + _releaseSpeed * 0.030) * visc);
                }
            }
            Log.Debug("Release speed=" + _releaseSpeed.ToString("f0") +
                " along=" + _releaseAlongAmp.ToString("f1") +
                " perp=" + _releasePerpAmp.ToString("f1") +
                " size=" + _releaseSizeAmp.ToString("f1") +
                " resize=" + _canResize);
            SetStatus("Успокаивается");
        }

        /// <summary>
        /// Starts the short size wave that follows an edge resize. The window
        /// itself already has its new size; only the settle animation changes
        /// it for a few frames and then puts it back exactly.
        /// </summary>
        /// <summary>
        /// How strong the post-resize wave should be. Speed keeps a quick
        /// flick lively; the distance keeps a slow, long drag visible.
        /// </summary>
        internal static double ResizeAmplitude(double speed, int delta, double jellyFactor)
        {
            double visc = Math.Max(0.6, jellyFactor / 1.1);
            return Math.Min(14.0, (2.5 + speed * 0.012 + delta * 0.02) * visc);
        }

        private void BeginResizeSettle(IntPtr hwnd, Native.RECT rect, double speed, int delta)
        {
            if (_mode != Mode.Idle || !Active) return;
            if (_settings.Economy || !_settings.Bend) return;

            string exe = Native.GetWindowProcessName(hwnd);
            int effect;
            bool bend;
            bool shake;
            bool roll;
            if (_settings.GetAppProfile(exe, out effect, out bend, out shake, out roll) && !bend)
                return;

            // The wave follows both the speed of the hand and the distance the
            // edge travelled. A slow 300 px drag used to give almost nothing,
            // while a quick 40 px nudge looked stronger than the drag.
            double amp = ResizeAmplitude(speed, delta, _settings.JellyFactor);
            if (amp < 1.5) return;

            _target = hwnd;
            _baseWidth = rect.Width;
            _baseHeight = rect.Height;
            _x = rect.Left;
            _y = rect.Top;
            _vx = 0;
            _vy = 0;
            _targetX = rect.Left;
            _targetY = rect.Top;
            _canResize = true;
            _overlayActive = false;
            _overlayPending = false;
            _following = false;
            _windowUnderOverlay = false;
            _resizeOnly = true;
            _releaseTime = DateTime.UtcNow;
            _releaseAlongAmp = 0;
            _releasePerpAmp = 0;
            _releaseSizeAmp = amp;
            _releaseDirX = 1;
            _releaseDirY = 0;
            _releasePerpX = 0;
            _releasePerpY = 1;
            _lastRequestedX = int.MinValue;
            _lastRequestedY = int.MinValue;
            _lastRequestedW = int.MinValue;
            _lastRequestedH = int.MinValue;
            _mode = Mode.Settling;
            SetStatus("Упруго");
            System.Threading.Interlocked.Increment(ref _resizeWaves);
            Log.Debug("Resize settle " + hwnd.ToInt64() + " " + rect.Width + "x" +
                rect.Height + " speed=" + speed.ToString("f0") + " delta=" + delta +
                " amp=" + amp.ToString("f1"));
        }

        private void StepPhysics(double dt)
        {
            // Без пружины: окно точно следует за курсором, без лага и перелёта.
            // Инерцию даёт только изгиб.
            _x = _targetX;
            _y = _targetY;
            _vx = 0;
            _vy = 0;
        }

        private void ComputeRender(bool settling, out double renderX, out double renderY, out double sizeWave)
        {
            renderX = _x;
            renderY = _y;
            sizeWave = 0;

            if (settling)
            {
                double elapsed = (DateTime.UtcNow - _releaseTime).TotalSeconds;
                double envelope = Math.Exp(-elapsed / 0.42);
                if (_releaseAlongAmp > 0.05 || _releasePerpAmp > 0.05)
                {
                    double along = _releaseAlongAmp * envelope * Math.Sin(2.0 * Math.PI * 2.7 * elapsed);
                    double perp = _releasePerpAmp * envelope * Math.Sin(2.0 * Math.PI * 4.2 * elapsed + 0.5);
                    renderX += _releaseDirX * along + _releasePerpX * perp;
                    renderY += _releaseDirY * along + _releasePerpY * perp;
                }
                if (_releaseSizeAmp > 0.05)
                {
                    double sizeEnvelope = Math.Exp(-elapsed / 0.34) * (1.0 - Math.Exp(-elapsed / 0.04));
                    sizeWave = _releaseSizeAmp * sizeEnvelope * Math.Sin(2.0 * Math.PI * 3.1 * elapsed + 0.35);
                }
            }
        }

        private void ApplyMove(bool settling)
        {
            double renderX;
            double renderY;
            double sizeWave;
            ComputeRender(settling, out renderX, out renderY, out sizeWave);

            int drawW = _baseWidth;
            int drawH = _baseHeight;
            double centerShiftX = 0;
            double centerShiftY = 0;

            if (_canResize && Math.Abs(sizeWave) > 0.05)
            {
                int dw = (int)Math.Round(sizeWave);
                int dh = (int)Math.Round(-sizeWave * 0.62);
                drawW += dw;
                drawH += dh;
                if (drawW < 80) drawW = 80;
                if (drawH < 50) drawH = 50;
                centerShiftX = (drawW - _baseWidth) / 2.0;
                centerShiftY = (drawH - _baseHeight) / 2.0;
            }

            int nx = (int)Math.Round(renderX - centerShiftX);
            int ny = (int)Math.Round(renderY - centerShiftY);
            int nw = drawW;
            int nh = drawH;
            if (nx == _lastRequestedX && ny == _lastRequestedY &&
                nw == _lastRequestedW && nh == _lastRequestedH)
                return;

            _lastRequestedX = nx;
            _lastRequestedY = ny;
            _lastRequestedW = nw;
            _lastRequestedH = nh;

            uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE |
                         Native.SWP_NOOWNERZORDER | Native.SWP_ASYNCWINDOWPOS;
            if (nw == _baseWidth && nh == _baseHeight)
                flags |= Native.SWP_NOSIZE;

            bool moved = Native.SetWindowPos(_target, IntPtr.Zero, nx, ny, nw, nh, flags);
            _frame++;
            if (!moved && _frame % 10 == 1)
                Log.Debug("Drag SetWindowPos failed (elevated target?) target=" + _target.ToInt64());
        }

        private void ActivateOverlay(Native.POINT cursor)
        {
            Native.RECT rect;
            if (!Native.GetWindowRect(_target, out rect))
            {
                _overlayPending = false;
                return;
            }

            _grabX = cursor.X - rect.Left;
            _grabY = cursor.Y - rect.Top;
            if (_grabX < 0) _grabX = 0;
            if (_grabX > rect.Width) _grabX = rect.Width;
            if (_grabY < 0) _grabY = 0;
            if (_grabY > rect.Height) _grabY = rect.Height;

            // Render while hidden, then reveal: no flash of stale content.
            _overlay.Render(rect.Left, rect.Top, 0, 0, (int)Math.Round(_grabX), (int)Math.Round(_grabY), true);

            uint flags = Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;

            bool live = _overlay.LiveCapable && !Native.IsZoomed(_target);
            _overlay.Show();

            if (live && _overlay.StartLive(_target))
            {
                _overlayActive = true;
                _overlayPending = false;
                _windowUnderOverlay = true;
                if (_parkMode == "clear" && ClearTarget())
                {
                    // Hide the real window without moving it: a layered window
                    // at alpha 1 is composited (so Graphics Capture keeps
                    // producing frames) but does not reach the screen. Moving it
                    // off-screen, by contrast, stops Graphics Capture after the
                    // first frame.
                    _windowUnderOverlay = false;
                    Log.Debug("Bend overlay active clear " + _target.ToInt64() + " " +
                        rect.Width + "x" + rect.Height + " grab=" + _grabX.ToString("f0") + "," +
                        _grabY.ToString("f0") + " live=" + (_overlay.WgcCapable ? "wgc" : "print"));
                    return;
                }
                if (_parkMode != "under" && !_overlay.WgcCapable)
                {
                    // No Graphics Capture: PrintWindow keeps returning frames for
                    // a window that is off-screen, so park it out of the way.
                    Native.SetWindowPos(_target, IntPtr.Zero, -30000, -30000, 0, 0, flags);
                    Native.RECT parked;
                    if (Native.GetWindowRect(_target, out parked) &&
                        Math.Abs(parked.Left + 30000) < 400 && Math.Abs(parked.Top + 30000) < 400)
                    {
                        _windowUnderOverlay = false;
                        Log.Debug("Bend overlay active parked " + _target.ToInt64() + " " +
                            rect.Width + "x" + rect.Height + " grab=" + _grabX.ToString("f0") + "," +
                            _grabY.ToString("f0") + " live=print");
                        return;
                    }
                    Native.SetWindowPos(_target, IntPtr.Zero, rect.Left, rect.Top, 0, 0, flags);
                }
                Log.Debug("Bend overlay active under window " + _target.ToInt64() + " " +
                    rect.Width + "x" + rect.Height + " grab=" + _grabX.ToString("f0") + "," +
                    _grabY.ToString("f0") + " live=" + (_overlay.WgcCapable ? "wgc" : "print"));
                return;
            }
            if (live)
            {
                // No live source: fall back to the frozen copy off-screen.
                _windowUnderOverlay = false;
                _overlayActive = false;
            }

            // Park the real window fully off-screen and show the frozen copy.
            Native.SetWindowPos(_target, IntPtr.Zero, -30000, -30000, 0, 0, flags);
            Native.RECT check;
            if (!Native.GetWindowRect(_target, out check) ||
                Math.Abs(check.Left + 30000) > 400 || Math.Abs(check.Top + 30000) > 400)
            {
                Native.SetWindowPos(_target, IntPtr.Zero, rect.Left, rect.Top, 0, 0, flags);
                _overlay.Hide();
                _overlayPending = false;
                Log.Debug("Off-screen park failed; using rigid effect.");
                return;
            }

            _overlayActive = true;
            _overlayPending = false;
            Log.Debug("Bend overlay active for " + _target.ToInt64() + " " + rect.Width + "x" + rect.Height +
                " grab=" + _grabX.ToString("f0") + "," + _grabY.ToString("f0") + " live=frozen");
        }


        private void UpdateBend(double dt, bool settling)
        {
            if (dt > 0.03) dt = 0.03;
            double jelly = 0.4 + _settings.Jelly / 100.0;
            double maxBend = Math.Min(80.0, Math.Min(_baseWidth, _baseHeight) * 0.14) * jelly;
            double tx = 0;
            double ty = 0;
            if (!settling)
            {
                tx = -_cursorVx * 0.070 * jelly;
                // Физика: нижний край отстаёт от захваченного верхнего, как
                // лист за верхний край. Знак обратный скорости.
                ty = -_cursorVy * 0.070 * jelly;
                if (tx > maxBend) tx = maxBend;
                else if (tx < -maxBend) tx = -maxBend;
                if (ty > maxBend) ty = maxBend;
                else if (ty < -maxBend) ty = -maxBend;
            }

            // Плавное следование за целью без пружинных колебаний:
            // окно изгибается по инерции, а выпрямляется без дрожи.
            double tau = 0.07;
            double alpha = 1.0 - Math.Exp(-dt / tau);
            _bendX += (tx - _bendX) * alpha;
            _bendY += (ty - _bendY) * alpha;

            if (double.IsNaN(_bendX) || double.IsInfinity(_bendX)) { _bendX = 0; }
            if (double.IsNaN(_bendY) || double.IsInfinity(_bendY)) { _bendY = 0; }
        }

        private void RenderOverlay(bool settling)
        {
            if (!_overlayActive) return;
            // The engine ticks every millisecond, but the desktop only refreshes
            // at the display rate: drawing the bent copy more than ~140 times a
            // second is pure heat.
            DateTime renderNow = DateTime.UtcNow;
            if (!settling && (renderNow - _lastRender).TotalMilliseconds < 7) return;
            _lastRender = renderNow;
            double renderX;
            double renderY;
            double sizeWave;
            ComputeRender(settling, out renderX, out renderY, out sizeWave);
            int x = (int)Math.Round(renderX);
            int y = (int)Math.Round(renderY);
            if (_windowUnderOverlay && _target != IntPtr.Zero && Native.IsWindow(_target))
            {
                // Keep the real window under the copy so Graphics Capture keeps
                // refreshing it. The move must be synchronous: with
                // SWP_ASYNCWINDOWPOS the window is still at its previous place
                // when the copy is drawn, and reading the rectangle back right
                // after the request returns that stale place as well, so the
                // straight window slides out from under the bent copy and reads
                // as a second window.
                Native.SetWindowPos(_target, IntPtr.Zero, x, y, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE |
                    Native.SWP_NOOWNERZORDER);
            }
            _overlay.Render(x, y, _bendX, _bendY, (int)Math.Round(_grabX), (int)Math.Round(_grabY),
                _windowUnderOverlay);

            _renderCount++;
            if ((_renderCount % 20) == 0)
                Log.Debug("render " + _renderCount + " pos=" + x + "," + y +
                    " bend=" + _bendX.ToString("f1") + "," + _bendY.ToString("f1") +
                    " v=" + _cursorVx.ToString("f0") + "," + _cursorVy.ToString("f0"));
        }

        private void RestoreOverlay(int x, int y)
        {
            if (!_overlayActive) return;
            RestoreTargetVisibility();
            if (_target != IntPtr.Zero && Native.IsWindow(_target))
            {
                // Put the real window under the overlay first, then reveal it.
                Native.SetWindowPos(_target, IntPtr.Zero, x, y, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
            }
            _overlay.Hide();
            _overlayActive = false;
        }

        /// <summary>
        /// Hides the target without moving it: WS_EX_LAYERED + alpha 1 keeps the
        /// window composited (Graphics Capture still sees fresh frames) while it
        /// does not reach the screen.
        /// </summary>
        private bool ClearTarget()
        {
            if (_target == IntPtr.Zero || !Native.IsWindow(_target)) return false;
            long ex = Native.GetWindowLongPtr(_target, Native.GWL_EXSTYLE);
            // A window that is already layered manages its own alpha; taking it
            // over would destroy that. Leave such windows to the other modes.
            if ((ex & Native.WS_EX_LAYERED) != 0) return false;
            _targetLayeredAdded = true;
            Native.SetWindowLongPtr(_target, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
            // Persist the handle before the window disappears: if the process
            // dies between these two calls, the next start still knows what to
            // put back.
            HiddenWindowGuard.Mark(_target);
            _targetAlphaSet = Native.SetWindowAlpha(_target, 1);
            if (!_targetAlphaSet)
            {
                HiddenWindowGuard.Clear();
                Native.SetWindowLongPtr(_target, Native.GWL_EXSTYLE, ex);
                _targetLayeredAdded = false;
            }
            return _targetAlphaSet;
        }

        private void RestoreTargetVisibility()
        {
            if (!_targetAlphaSet) return;
            _targetAlphaSet = false;
            if (_target == IntPtr.Zero || !Native.IsWindow(_target))
            {
                HiddenWindowGuard.Clear();
                return;
            }
            Native.SetWindowAlpha(_target, 255);
            if (_targetLayeredAdded)
            {
                long ex = Native.GetWindowLongPtr(_target, Native.GWL_EXSTYLE);
                Native.SetWindowLongPtr(_target, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_LAYERED);
                _targetLayeredAdded = false;
            }
            HiddenWindowGuard.Clear();
        }

        private void CheckNativeFollow(Native.POINT cursor)
        {
            if ((_frame & 3) != 0) return;
            Native.RECT actual;
            if (!Native.GetWindowRect(_target, out actual)) return;

            double cx = _startRect.Left + (cursor.X - _startCursor.X);
            double cy = _startRect.Top + (cursor.Y - _startCursor.Y);
            double toCursor = Math.Sqrt((actual.Left - cx) * (actual.Left - cx) + (actual.Top - cy) * (actual.Top - cy));
            double toRequested = Math.Sqrt((actual.Left - _lastRequestedX) * (actual.Left - _lastRequestedX) +
                                           (actual.Top - _lastRequestedY) * (actual.Top - _lastRequestedY));

            // The window tracks the cursor exactly while our own requested
            // position is far away: the app is dragging it itself. Stop moving
            // it and let it follow natively; the release wobble still applies.
            if (toCursor < 12.0 && toRequested > 70.0)
                _followTicks++;
            else
                _followTicks = 0;

            if (_followTicks >= 4)
            {
                _followTicks = 0;
                _following = true;
                _canResize = false;
                Log.Debug("Window is moving itself; following it.");
            }
        }

        private void MoveExact(int x, int y)
        {
            if (_target == IntPtr.Zero || !Native.IsWindow(_target)) return;

            uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
            int width = 0;
            int height = 0;
            if (_canResize && _baseWidth > 0 && _baseHeight > 0)
            {
                width = _baseWidth;
                height = _baseHeight;
            }
            else
            {
                flags |= Native.SWP_NOSIZE;
            }

            bool ok = Native.SetWindowPos(_target, IntPtr.Zero, x, y, width, height, flags);
            if (!ok)
                Log.Debug("Final SetWindowPos failed for " + _target.ToInt64() + " error=" + Marshal.GetLastWin32Error());
            _lastRequestedX = x;
            _lastRequestedY = y;
            _lastRequestedW = width;
            _lastRequestedH = height;
        }

        private bool IsTargetStillGood()
        {
            if (_target == IntPtr.Zero || !Native.IsWindow(_target)) return false;
            if (!Native.IsWindowVisible(_target) || Native.IsIconic(_target) || Native.IsZoomed(_target)) return false;
            return true;
        }

        private void StartShake(IntPtr explicitTarget)
        {
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

            Native.RECT rect;
            if (!Native.GetWindowRect(fg, out rect)) return;
            if (rect.Width < 80 || rect.Height < 40) return;

            _target = fg;
            _shakeRect = rect;
            _shakeZoomed = Native.IsZoomed(fg);
            _shakeStart = DateTime.UtcNow;
            _mode = Mode.Shaking;
            SetStatus("Покачивание");
            Log.Debug("Shake " + fg.ToInt64() + " '" + Native.GetWindowTitle(fg) + "' zoomed=" + _shakeZoomed);
        }

        private void TickShake(double now)
        {
            if (_target == IntPtr.Zero || !Native.IsWindow(_target))
            {
                ResetEngine(_settings.Enabled ? "Готово" : "Пауза");
                return;
            }

            // Hidden or minimized mid-shake: the window is out of sight, but
            // its placement must not stay offset, or it would come back a few
            // pixels off. Put it back first, then stop. (A minimized maximized
            // window reports IsZoomed=false, so this has to run before the
            // state check below.)
            if (!Native.IsWindowVisible(_target) || Native.IsIconic(_target))
            {
                Native.SetWindowPos(_target, IntPtr.Zero, _shakeRect.Left, _shakeRect.Top, 0, 0, ShakeFlags());
                ResetEngine(_settings.Enabled ? "Готово" : "Пауза");
                return;
            }

            // If the user changed the window state under the shake (restored,
            // maximized), do not fight it: leave it where the system put it.
            if (Native.IsZoomed(_target) != _shakeZoomed)
            {
                ResetEngine(_settings.Enabled ? "Готово" : "Пауза");
                return;
            }

            if (IsKeyDown(Native.VK_LBUTTON))
            {
                EndShake();
                return;
            }

            double elapsed = (DateTime.UtcNow - _shakeStart).TotalSeconds;
            if (elapsed >= 1.35)
            {
                EndShake();
                return;
            }

            double envelope = Math.Exp(-elapsed / 0.42);
            double dx = 22.0 * envelope * Math.Sin(2.0 * Math.PI * 3.1 * elapsed);
            double dy = 7.0 * envelope * Math.Sin(2.0 * Math.PI * 4.2 * elapsed + 0.6);

            Native.SetWindowPos(_target, IntPtr.Zero,
                _shakeRect.Left + (int)Math.Round(dx),
                _shakeRect.Top + (int)Math.Round(dy),
                0, 0, ShakeFlags());
        }

        /// <summary>
        /// A maximized window normally restores itself as soon as it is moved.
        /// SWP_NOSENDCHANGING keeps the default handler from seeing the move,
        /// so the window stays maximized and simply shakes.
        /// </summary>
        private uint ShakeFlags()
        {
            uint flags = Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE |
                         Native.SWP_NOOWNERZORDER;
            if (_shakeZoomed) return flags | Native.SWP_NOSENDCHANGING;
            return flags | Native.SWP_ASYNCWINDOWPOS;
        }

        private void EndShake()
        {
            if (_target != IntPtr.Zero && Native.IsWindow(_target))
            {
                Native.SetWindowPos(_target, IntPtr.Zero, _shakeRect.Left, _shakeRect.Top, 0, 0, ShakeFlags());
            }
            ResetEngine(_settings.Enabled ? "Готово" : "Пауза");
        }

        /// <summary>
        /// Takes the single frame the wobble needs, on a worker thread: the
        /// PrintWindow call for some windows (the Windows 11 Notepad among
        /// them) takes seconds, and waiting for it would freeze the engine.
        /// When no frame arrives within 150 ms the wobble is skipped; a
        /// capture that still takes over 250 ms marks its class as slow, so
        /// later transitions of that window are not even attempted.
        /// </summary>
        private bool CaptureOnce(IntPtr hwnd, Native.RECT rect, string cls, out bool failed)
        {
            const int CaptureWait = 150;

            failed = false;
            if (_shotBusy) return false;
            // The window has to belong to the engine thread (see Prepare).
            if (!_shot.Prepare()) return false;
            _shotBusy = true;
            bool ok = false;
            Thread worker = new Thread(delegate()
            {
                try
                {
                    ok = _shot.Capture(hwnd, rect, false);
                    if (ok && _shot.LastPrintMs > 250 && cls.Length > 0)
                    {
                        lock (_slowLock) _slowCapture.Add(cls);
                        Log.Debug("Jelly: '" + cls + "' capture took " +
                            _shot.LastPrintMs.ToString("f0") + " ms; skipping it");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Jelly capture failed: " + ex.Message);
                }
                finally
                {
                    _shotBusy = false;
                }
            });
            worker.IsBackground = true;
            worker.Name = "OknaJellyCapture";
            try
            {
                worker.Start();
            }
            catch (Exception ex)
            {
                _shotBusy = false;
                Log.Error("Jelly capture thread failed: " + ex.Message);
                failed = true;
                return false;
            }
            if (!worker.Join(CaptureWait))
            {
                Log.Debug("Jelly skipped: capture still running after " +
                    CaptureWait + " ms");
                return false;
            }
            failed = !ok;
            return ok;
        }

        /// <summary>
        /// True when the screen shows the window without changing any more:
        /// the shell has finished animating it and the app has repainted it at
        /// the new size. Two quiet samples in a row are required (a single
        /// stalled frame is not "settled"), and a window that keeps changing
        /// for 700 ms gets no wobble at all.
        /// </summary>
        private bool WaitUntilSettled(IntPtr hwnd, Native.RECT rect)
        {
            const int SettleMin = 60;   // ms; never wobble sooner than this
            const int SettleLimit = 700;
            const int Quiet = 20;       // per mille of the sample that may change
            const int QuietSamples = 2; // two quiet samples in a row

            int quiet = 0;
            Stopwatch watch = Stopwatch.StartNew();
            while (true)
            {
                int change = _overlay.ScreenChange(hwnd, rect);
                if (change < 0) return true;   // cannot sample: grab it now
                if (change <= Quiet) quiet++;
                else quiet = 0;

                long elapsed = watch.ElapsedMilliseconds;
                if (quiet >= QuietSamples && elapsed >= SettleMin) return true;
                if (elapsed >= SettleLimit) return false;
                if (!Native.IsWindow(hwnd) || Native.GetForegroundWindow() != hwnd)
                    return false;
                Thread.Sleep(20);
            }
        }

    }
}
