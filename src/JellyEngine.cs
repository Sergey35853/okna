using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Okna
{
    internal sealed class JellyEngine : IDisposable
    {
        private enum Mode
        {
            Idle,
            Candidate,
            Dragging,
            Settling,
            Shaking,
            Jelly
        }

        private readonly AppSettings _settings;
        private Thread _thread;
        private volatile bool _stopping;
        private volatile bool _shakeRequested;
        private IntPtr _explicitShakeTarget;
        private IntPtr _lastExternal;
        private volatile string _status = "Готово";
        private bool _ownElevated;

        private Mode _mode = Mode.Idle;
        private IntPtr _target;
        private Native.RECT _startRect;
        private Native.RECT _pressRect;
        private Native.POINT _startCursor;
        private bool _latched;

        private double _x;
        private double _y;
        private double _vx;
        private double _vy;
        private double _targetX;
        private double _targetY;

        private int _baseWidth;
        private int _baseHeight;
        private bool _canResize;

        private double _lastCursorX;
        private double _lastCursorY;
        private DateTime _lastCursorChange;
        private double _cursorVx;
        private double _cursorVy;
        private double _recentDragSpeed;
        private double _recentDirX;
        private double _recentDirY;
        private double _wobblePhase;
        private double _dragWobbleAmp;
        private double _dragPerpX;
        private double _dragPerpY;

        private double _releaseSpeed;
        private double _releaseDirX;
        private double _releaseDirY;
        private double _releasePerpX;
        private double _releasePerpY;
        private double _releaseAlongAmp;
        private double _releasePerpAmp;
        private double _releaseSizeAmp;

        private int _lastRequestedX = int.MinValue;
        private int _lastRequestedY = int.MinValue;
        private int _lastRequestedW = int.MinValue;
        private int _lastRequestedH = int.MinValue;
        private int _frame;
        private int _followTicks;
        private int _renderCount;

        private readonly BendOverlay _overlay;
        private bool _overlayActive;
        private bool _overlayPending;
        private bool _captureTried;
        private bool _captureOk;
        private bool _following;
        private bool _windowUnderOverlay;
        private readonly string _parkMode;
        private bool _targetAlphaSet;
        private bool _targetLayeredAdded;
        private DateTime _lastRender;
        private bool _verifyDone;
        private int _verifyStage;
        private DateTime _verifyUntil;
        private Native.RECT _verifyRect;
        private double _grabX;
        private double _grabY;
        private double _bendX;
        private double _bendY;

        private DateTime _releaseTime;

        private Native.RECT _shakeRect;
        private DateTime _shakeStart;
        private bool _shakeZoomed;

        private DateTime _jellyStart;
        private double _jellyLastRender;
        private int _jellyFrames;

        // Window-state detection. The foreground window is watched for
        // maximize/restore transitions and for a return from the taskbar; a
        // short shake is scheduled once the system animation has finished.
        private IntPtr _zoomWatch;
        private bool _zoomWatchState;
        private IntPtr _minimizedWatch;
        private IntPtr _shakePending;
        private double _shakePendingAt;
        private double _stateCheckAt;

        // Close watcher: the picture of a window is taken when its close
        // button is under the cursor (or Alt+F4 is held); if the window is gone
        // a moment later, the picture is shattered.
        private IntPtr _closeWatch;
        private Bitmap _closeShot;
        private Native.RECT _closeFrame;
        private Native.POINT _closePoint;
        private double _closeShotAt;
        private double _closeCheckAt;

        // Minimize watcher: the picture is taken while the window is still on
        // screen — when the pointer is on its minimize button, on a taskbar
        // button, or while Win/Alt+Space is held — so the window can be rolled
        // into a tube the moment the system actually minimizes it.
        private IntPtr _minimizeWatch;
        private Bitmap _minimizeShot;
        private Native.RECT _minimizeFrame;
        private double _minimizeShotAt;
        private double _minimizeCheckAt;
        private bool _minimizeDown;
        private bool _minimizeWinDown;
        private bool _minimizeTransitions;
        private double _minimizeTransitionAt;

        public JellyEngine(AppSettings settings)
        {
            _settings = settings;
            _overlay = new BendOverlay();
            string park = Environment.GetEnvironmentVariable("OKNA_PARK");
            if (string.IsNullOrEmpty(park)) park = "clear";
            _parkMode = park.Trim().ToLowerInvariant();
            if (_parkMode == "1" || _parkMode == "true") _parkMode = "offscreen";
            if (_parkMode == "0" || _parkMode == "false") _parkMode = "under";
            if (_parkMode != "offscreen" && _parkMode != "clear") _parkMode = "under";
        }

        public string Status
        {
            get { return _status; }
        }

        public bool IsRunning
        {
            get { return _thread != null && _thread.IsAlive; }
        }

        public void Start()
        {
            if (_thread != null) return;
            bool elevated;
            if (Native.IsProcessElevated(Native.GetCurrentProcessId(), out elevated))
                _ownElevated = elevated;

            _thread = new Thread(Worker);
            _thread.IsBackground = true;
            _thread.Priority = ThreadPriority.AboveNormal;
            _thread.Name = "Okna.Engine";
            _thread.Start();
            Log.Info("Engine started. elevated=" + _ownElevated);
        }

        public void RequestShake()
        {
            _explicitShakeTarget = IntPtr.Zero;
            _shakeRequested = true;
        }

        public void ShakeWindow(IntPtr hwnd)
        {
            _explicitShakeTarget = hwnd;
            _shakeRequested = true;
        }

        public void Stop()
        {
            _stopping = true;
            Thread t = _thread;
            if (t != null)
            {
                try { t.Join(1500); }
                catch { }
            }
            _thread = null;
            ClearCloseWatch();
            Log.Info("Engine stopped.");
        }

        public void Dispose()
        {
            Stop();
        }

        private void Worker()
        {
            Native.timeBeginPeriod(1);
            Stopwatch clock = Stopwatch.StartNew();
            double last = clock.Elapsed.TotalSeconds;
            try
            {
                while (!_stopping)
                {
                    Native.MSG msg;
                    while (Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
                    {
                        Native.TranslateMessage(ref msg);
                        Native.DispatchMessage(ref msg);
                    }

                    double now = clock.Elapsed.TotalSeconds;
                    double dt = now - last;
                    last = now;
                    if (dt < 0.001)
                    {
                        Thread.Sleep(1);
                        continue;
                    }
                    if (dt > 0.05) dt = 0.05;

                    try
                    {
                        Tick(dt, now);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Engine tick failed: " + ex);
                        ResetEngine("Ошибка");
                    }

                    int sleep;
                    switch (_mode)
                    {
                        case Mode.Dragging: sleep = 1; break;
                        case Mode.Candidate: sleep = 2; break;
                        case Mode.Settling: sleep = 3; break;
                        case Mode.Shaking: sleep = 5; break;
                        case Mode.Jelly: sleep = 2; break;
                        default: sleep = 8; break;
                    }
                    Thread.Sleep(sleep);
                }
            }
            finally
            {
                try
                {
                    // Safety net: never leave a foreign window transparent or
                    // layered, whatever state the effect was interrupted in.
                    RestoreTargetVisibility();
                    if (_overlayActive)
                    {
                        RestoreOverlay((int)Math.Round(_x), (int)Math.Round(_y));
                    }
                    else if (_target != IntPtr.Zero && _mode == Mode.Dragging)
                    {
                        MoveExact((int)Math.Round(_targetX), (int)Math.Round(_targetY));
                    }
                    else if (_target != IntPtr.Zero && _mode == Mode.Shaking && Native.IsWindow(_target))
                    {
                        // Never leave a shaken window parked off its original spot.
                        Native.SetWindowPos(_target, IntPtr.Zero, _shakeRect.Left, _shakeRect.Top, 0, 0, ShakeFlags());
                    }
                    else if (_mode == Mode.Jelly)
                    {
                        // Never leave the real window transparent or the copy up.
                        RestoreTargetVisibility();
                        if (_overlayActive)
                        {
                            _overlay.Hide();
                            _overlayActive = false;
                        }
                    }
                    if (_overlay != null) _overlay.Dispose();
                    ClearCloseWatch();
                    ClearMinimizeWatch();
                }
                catch (Exception ex)
                {
                    // The persisted record is the last line of defence: restore
                    // the hidden window even if the in-memory state is unusable.
                    Log.Error("Engine shutdown cleanup failed: " + ex);
                    try { HiddenWindowGuard.Restore(); } catch { }
                }
                finally
                {
                    try { Native.timeEndPeriod(1); } catch { }
                }
            }
        }

        private void Tick(double dt, double now)
        {
            if (_shakeRequested)
            {
                _shakeRequested = false;
                IntPtr requested = _explicitShakeTarget;
                _explicitShakeTarget = IntPtr.Zero;
                if (_mode == Mode.Idle)
                    StartJelly(requested);
            }

            if (_mode == Mode.Idle)
            {
                IntPtr foreground = Native.GetForegroundWindow();
                if (foreground != IntPtr.Zero && !Native.IsOwnProcessWindow(foreground) &&
                    Native.IsWindowVisible(foreground) && !Native.IsIconic(foreground))
                    _lastExternal = foreground;

                if (_settings.Enabled && _settings.Shatter)
                    WatchClose(foreground, now);
                else
                    ClearCloseWatch();

                if (_settings.Enabled && _settings.Roll)
                    WatchMinimize(foreground, now);
                else
                    ClearMinimizeWatch();

                if (_settings.Enabled && _settings.MaxShake)
                {
                    // The idle loop runs every few milliseconds, but window
                    // state changes are far slower: a look every 30 ms is
                    // plenty and keeps the idle cost where it was.
                    if (now >= _stateCheckAt)
                    {
                        _stateCheckAt = now + 0.03;
                        WatchWindowState(foreground, now);
                    }
                }
                else
                {
                    _zoomWatch = IntPtr.Zero;
                    _shakePending = IntPtr.Zero;
                }

                if (_shakePending != IntPtr.Zero && now >= _shakePendingAt)
                {
                    IntPtr pending = _shakePending;
                    _shakePending = IntPtr.Zero;
                    if (!IsKeyDown(Native.VK_LBUTTON) &&
                        Native.IsWindow(pending) && Native.IsWindowVisible(pending) &&
                        !Native.IsIconic(pending) && Native.GetForegroundWindow() == pending)
                    {
                        StartJelly(pending);
                    }
                }
            }

            if (_mode == Mode.Shaking)
            {
                TickShake(now);
                return;
            }

            if (_mode == Mode.Jelly)
            {
                TickJelly(now);
                return;
            }

            bool down = IsKeyDown(Native.VK_LBUTTON);
            bool shift = IsKeyDown(Native.VK_SHIFT);
            bool escape = IsKeyDown(Native.VK_ESCAPE);

            if (_mode == Mode.Idle)
            {
                if (!down)
                {
                    _latched = false;
                    return;
                }
                if (!_latched)
                {
                    _latched = true;
                    if (_settings.Enabled && !shift && !escape)
                        TryBeginCandidate();
                }
                return;
            }

            if (_mode == Mode.Candidate)
            {
                if (!down || shift || !_settings.Enabled)
                {
                    ResetEngine(_settings.Enabled ? "Готово" : "Пауза");
                    return;
                }
                if (!IsTargetStillGood())
                {
                    ResetEngine("Готово");
                    return;
                }

                // A drag is under way when the window itself starts moving while the
                // button is held. This works for classic captions and for windows that
                // draw their own title bar (browsers, Electron, UWP): those answer
                // HTCLIENT instead of HTCAPTION and move themselves.
                Native.RECT current;
                if (!Native.GetWindowRect(_target, out current))
                {
                    ResetEngine("Готово");
                    return;
                }
                bool resized = Math.Abs(current.Width - _pressRect.Width) > 2 ||
                               Math.Abs(current.Height - _pressRect.Height) > 2;
                if (resized)
                {
                    // Edge resize, not a move.
                    ResetEngine("Готово");
                    return;
                }
                bool moved = Math.Abs(current.Left - _pressRect.Left) >= 2 ||
                             Math.Abs(current.Top - _pressRect.Top) >= 2;
                if (moved)
                {
                    Native.POINT cursor;
                    Native.GetCursorPos(out cursor);
                    if (TakeOver(current))
                    {
                        _mode = Mode.Dragging;
                        SetStatus("Перетаскивание");
                        Log.Debug("Takeover at " + cursor.X + "," + cursor.Y + " target=" + _target.ToInt64());
                    }
                    else
                    {
                        ResetEngine("Готово");
                    }
                }
                return;
            }

            if (_mode == Mode.Dragging)
            {
                if (!_settings.Enabled)
                {
                    BeginSettle(now, false);
                    return;
                }
                if (!down)
                {
                    BeginSettle(now, true);
                    return;
                }
                if (!IsTargetStillGood())
                {
                    ResetEngine("Готово");
                    return;
                }

                Native.POINT cursor;
                Native.GetCursorPos(out cursor);

                if (!_verifyDone)
                {
                    // Take the snapshot while watching whether the window keeps
                    // moving on its own. A window that does is running its own
                    // drag loop; fighting it causes jitter, so follow it instead.
                    if (!_captureTried && _overlayPending)
                    {
                        _captureTried = true;
                        Native.RECT shot;
                        if (Native.GetWindowRect(_target, out shot))
                            _captureOk = _overlay.Capture(_target, shot);
                    }
                    if (DateTime.UtcNow < _verifyUntil)
                    {
                        UpdateDragWobble(cursor, dt);
                        return;
                    }

                    Native.RECT probe;
                    bool haveRect = Native.GetWindowRect(_target, out probe);
                    if (_verifyStage == 0)
                    {
                        // First sample: ignore the few pixels the cancelled native
                        // drag may still move. Only a second, later sample tells
                        // whether the app itself is still dragging the window.
                        _verifyStage = 1;
                        _verifyRect = probe;
                        _verifyUntil = DateTime.UtcNow.AddMilliseconds(35);
                        UpdateDragWobble(cursor, dt);
                        return;
                    }

                    _verifyDone = true;
                    if (haveRect &&
                        (Math.Abs(probe.Left - _verifyRect.Left) > 3 ||
                         Math.Abs(probe.Top - _verifyRect.Top) > 3))
                    {
                        _following = true;
                        _canResize = false;
                        Log.Debug("Native drag continues; following the window.");
                    }
                    else if (_captureOk)
                    {
                        ActivateOverlay(cursor);
                    }
                }

                if (escape)
                {
                    if (_following)
                    {
                        ResetEngine("Готово");
                        return;
                    }
                    _targetX = _pressRect.Left;
                    _targetY = _pressRect.Top;
                    BeginSettle(now, false);
                    SetStatus("Отмена");
                    return;
                }

                if (_following)
                {
                    Native.RECT actual;
                    if (Native.GetWindowRect(_target, out actual))
                    {
                        _x = actual.Left;
                        _y = actual.Top;
                        _vx = 0;
                        _vy = 0;
                        _startRect = actual;
                        _startCursor = cursor;
                        _targetX = actual.Left;
                        _targetY = actual.Top;
                    }
                    UpdateDragWobble(cursor, dt);
                    return;
                }

                _targetX = _startRect.Left + (cursor.X - _startCursor.X);
                _targetY = _startRect.Top + (cursor.Y - _startCursor.Y);
                UpdateDragWobble(cursor, dt);

                StepPhysics(dt);
                if (_overlayActive)
                {
                    UpdateBend(dt, false);
                    RenderOverlay(false);
                }
                else
                {
                    ApplyMove(false);
                    CheckNativeFollow(cursor);
                }
                return;
            }

            if (_mode == Mode.Settling)
            {
                if (down && !_latched)
                {
                    // The user started a new gesture while the window was still settling.
                    // Leave the window where it is and let Windows handle the new drag.
                    _latched = true;
                    ResetEngine(_settings.Enabled ? "Готово" : "Пауза");
                    return;
                }

                if (!IsTargetStillGood())
                {
                    ResetEngine("Готово");
                    return;
                }

                StepPhysics(dt);
                if (_overlayActive)
                {
                    UpdateBend(dt, true);
                    RenderOverlay(true);
                }
                else
                {
                    ApplyMove(true);
                }

                double elapsed = (DateTime.UtcNow - _releaseTime).TotalSeconds;
                double distance = Math.Sqrt((_targetX - _x) * (_targetX - _x) + (_targetY - _y) * (_targetY - _y));
                double speed = Math.Sqrt(_vx * _vx + _vy * _vy);
                bool wobble = _releaseAlongAmp > 0.2 || _releasePerpAmp > 0.2 || _releaseSizeAmp > 0.2;
                double minTime = wobble ? 0.95 : 0.12;
                if (_overlayActive && minTime < (wobble ? 1.10 : 0.35))
                    minTime = wobble ? 1.10 : 0.35;

                if ((elapsed > minTime && distance < 1.0 && speed < 22.0) || elapsed > 2.05)
                {
                    int fx = (int)Math.Round(_targetX);
                    int fy = (int)Math.Round(_targetY);
                    if (_overlayActive) RestoreOverlay(fx, fy);
                    else MoveExact(fx, fy);
                    ResetEngine(_settings.Enabled ? "Готово" : "Пауза");
                }
                return;
            }
        }

        private void TryBeginCandidate()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;

            IntPtr hwnd = Native.WindowFromPoint(p);
            IntPtr root = Native.GetAncestor(hwnd, 2);
            if (root == IntPtr.Zero || !Native.IsWindow(root)) return;
            if (Native.IsOwnProcessWindow(root)) return;
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
            if (pid == Native.GetCurrentProcessId()) return;
            if (!_ownElevated)
            {
                bool targetElevated;
                if (!Native.IsProcessElevated(pid, out targetElevated)) return;
                if (targetElevated) return;
            }

            UIntPtr hit;
            IntPtr ok = Native.SendMessageTimeout(root, Native.WM_NCHITTEST, IntPtr.Zero,
                Native.MakeLParam(p.X, p.Y), Native.SMTO_BLOCK | Native.SMTO_ABORTIFHUNG, 60, out hit);
            if (ok == IntPtr.Zero) return;

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
                " hit=" + hit.ToUInt64());
        }

        private bool TakeOver(Native.RECT current)
        {
            if (!Native.IsWindow(_target)) return false;

            UIntPtr result;
            IntPtr ok = Native.SendMessageTimeout(_target, Native.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero,
                Native.SMTO_BLOCK | Native.SMTO_ABORTIFHUNG, 80, out result);
            if (ok == IntPtr.Zero)
            {
                Log.Debug("WM_CANCELMODE failed for " + _target.ToInt64());
                return false;
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
            _overlayActive = false;
            _overlayPending = _settings.Bend && _settings.Enabled;
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

                    _releaseAlongAmp = Math.Min(42.0, 12.0 + _releaseSpeed * 0.055);
                    _releasePerpAmp = Math.Min(30.0, 9.0 + _releaseSpeed * 0.045);
                    if (_canResize)
                        _releaseSizeAmp = Math.Min(20.0, 8.0 + _releaseSpeed * 0.030);
                }
            }
            Log.Debug("Release speed=" + _releaseSpeed.ToString("f0") +
                " along=" + _releaseAlongAmp.ToString("f1") +
                " perp=" + _releasePerpAmp.ToString("f1") +
                " size=" + _releaseSizeAmp.ToString("f1") +
                " resize=" + _canResize);
            SetStatus("Успокаивается");
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

            Native.SetWindowPos(_target, IntPtr.Zero, nx, ny, nw, nh, flags);
            _frame++;
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
        /// The maximized/restored window is replaced for a moment by a warped
        /// copy of itself: the copy sways like a slab of jelly instead of the
        /// whole window being shoved around rigidly. The real window is hidden
        /// (alpha 1) for the duration, so the copy's deformed silhouette is
        /// what the user sees. Anything the overlay cannot capture falls back
        /// to the old physical shake.
        /// </summary>
        private void StartJelly(IntPtr explicitTarget)
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

            // One frozen frame is enough for a one-shot wobble: the window is
            // not going to change while it jiggles.
            if (!_overlay.Capture(fg, rect, false))
            {
                Log.Debug("Jelly capture failed; using the physical shake");
                StartShake(explicitTarget);
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
            // order would flash the desktop for a frame.
            _overlay.Show();
            _overlay.RenderJelly(rect.Left, rect.Top, 0, 0, false);
            if (!ClearTarget())
            {
                _overlay.Hide();
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
            double ax = 0.010 * w * env * Math.Cos(phase * 1.90 + 0.8);
            double ay = 0.022 * h * env * Math.Cos(phase * 2.20);
            double ox = 0.0025 * w * env * Math.Cos(phase * 1.90 + 1.4);
            double oy = 0.0025 * h * env * Math.Cos(phase * 2.20 + 0.5);

            _overlay.RenderJelly(
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
                _overlay.RenderJelly(_shakeRect.Left, _shakeRect.Top, 0, 0, false);

            RestoreTargetVisibility();
            if (_overlayActive)
            {
                _overlay.Hide();
                _overlayActive = false;
            }
            ResetEngine(_settings.Enabled ? "Готово" : "Пауза");
        }

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

            bool zoomed = Native.IsZoomed(foreground);

            // Back from the taskbar: the window was hidden, so it could not
            // shake while minimized — it shakes now that it is visible again.
            if (foreground == _minimizedWatch)
            {
                _minimizedWatch = IntPtr.Zero;
                _zoomWatch = foreground;
                _zoomWatchState = zoomed;
                ScheduleShake(foreground, now);
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
                _shakePending = IntPtr.Zero;
                return;
            }

            if (zoomed != _zoomWatchState)
            {
                _zoomWatchState = zoomed;
                // Let the system's maximize/restore animation finish first.
                ScheduleShake(foreground, now);
            }
        }

        /// <summary>
        /// Queues a shake for after the current system animation. The delay is
        /// what keeps the wobble from fighting the maximize/restore transition.
        /// </summary>
        private void ScheduleShake(IntPtr hwnd, double now)
        {
            _shakePending = hwnd;
            _shakePendingAt = now + 0.36;
        }

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
                    Bitmap shot = _closeShot;
                    _closeShot = null;
                    if (shot != null)
                    {
                        Log.Debug("Shatter " + gone.ToInt64() + " '" + Native.GetWindowTitle(gone) + "'");
                        ShatterOverlay.Play(shot, _closeFrame, _closePoint);
                    }
                    return;
                }

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
                shot = new Bitmap(ext.Width, ext.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(shot))
                {
                    g.CopyFromScreen(ext.Left, ext.Top, 0, 0,
                        new Size(ext.Width, ext.Height), CopyPixelOperation.SourceCopy);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Close snapshot failed: " + ex.GetType().Name + ": " + ex.Message);
                if (shot != null) shot.Dispose();
                return;
            }

            if (_closeShot != null) _closeShot.Dispose();
            _closeShot = shot;
            _closeFrame = ext;
            _closePoint = point;
            _closeShotAt = now;
            _closeWatch = hwnd;
        }

        private void ClearCloseWatch()
        {
            _closeWatch = IntPtr.Zero;
            if (_closeShot != null)
            {
                _closeShot.Dispose();
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
                    RollOverlay.Play(shot, frame, target);
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

            if (foreground == IntPtr.Zero || Native.IsOwnProcessWindow(foreground) ||
                !Native.IsWindowVisible(foreground) || Native.IsIconic(foreground) ||
                IsShellWindowClass(Native.GetWindowClass(foreground)))
                return;

            Native.POINT cursor;
            if (!Native.GetCursorPos(out cursor)) return;

            bool down = IsKeyDown(Native.VK_LBUTTON);
            bool win = IsKeyDown(Native.VK_LWIN);
            bool altSpace = IsKeyDown(Native.VK_MENU) && IsKeyDown(Native.VK_SPACE);

            bool pressedTaskbar = down && !_minimizeDown && TaskbarFinder.Contains(cursor);
            bool hoverMinimize = InMinimizeZone(foreground, cursor) && now - _minimizeShotAt > 0.35;
            bool winPressed = win && !_minimizeWinDown;
            bool winHeld = win && now - _minimizeShotAt > 0.5;

            _minimizeDown = down;
            _minimizeWinDown = win;

            if (!pressedTaskbar && !hoverMinimize && !winPressed && !winHeld && !altSpace)
                return;

            TakeMinimizeShot(foreground, now);
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
            int buttonW = (46 * dpi + 48) / 96;
            int buttonH = (32 * dpi + 48) / 96;

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
            Native.RECT ext;
            if (!GetVisibleFrame(hwnd, out ext)) return;
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
                shot = new Bitmap(ext.Width, ext.Height,
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
                if (shot != null) shot.Dispose();
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

            if (_minimizeShot != null) _minimizeShot.Dispose();
            _minimizeShot = shot;
            _minimizeFrame = ext;
            _minimizeShotAt = now;
            _minimizeWatch = hwnd;
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
                _minimizeShot.Dispose();
                _minimizeShot = null;
            }
        }

        private void ResetEngine(string status)
        {
            if (_overlayActive)
            {
                if (_mode == Mode.Jelly)
                {
                    // The jelly copy must never drag the real window back to a
                    // stale rectangle (the user may have restored it meanwhile).
                    RestoreTargetVisibility();
                    _overlay.Hide();
                    _overlayActive = false;
                }
                else
                {
                    RestoreOverlay((int)Math.Round(_x), (int)Math.Round(_y));
                }
            }
            // Safety net: never leave a foreign window hidden or transparent.
            RestoreTargetVisibility();

            if (_mode == Mode.Settling && _canResize && _target != IntPtr.Zero &&
                Native.IsWindow(_target) && !Native.IsZoomed(_target))
            {
                Native.RECT actual;
                if (Native.GetWindowRect(_target, out actual) &&
                    (actual.Width != _baseWidth || actual.Height != _baseHeight))
                {
                    int x = actual.Left + (actual.Width - _baseWidth) / 2;
                    int y = actual.Top + (actual.Height - _baseHeight) / 2;
                    Native.SetWindowPos(_target, IntPtr.Zero, x, y, _baseWidth, _baseHeight,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
                }
            }

            _mode = Mode.Idle;
            _target = IntPtr.Zero;
            _followTicks = 0;
            _windowUnderOverlay = false;
            _lastRequestedX = int.MinValue;
            _lastRequestedY = int.MinValue;
            _lastRequestedW = int.MinValue;
            _lastRequestedH = int.MinValue;
            SetStatus(status);
        }

        private void SetStatus(string status)
        {
            if (!string.Equals(_status, status, StringComparison.Ordinal))
                _status = status;
        }

        private static bool IsKeyDown(int key)
        {
            return (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        }

        private static bool IsShellWindowClass(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return true;
            switch (cls)
            {
                case "Progman":
                case "WorkerW":
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                case "NotifyIconOverflowWindow":
                    return true;
                default:
                    return false;
            }
        }
    }
}
