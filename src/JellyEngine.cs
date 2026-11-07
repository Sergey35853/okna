using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Okna
{
    internal sealed partial class JellyEngine : IDisposable
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
        private readonly AutoPause _autoPause;
        private Thread _thread;
        private volatile int _restarts;
        private volatile bool _stopping;
        private volatile bool _shakeRequested;
        private bool _shakeBypassProfile;
        private volatile bool _manualPause;
        private IntPtr _explicitShakeTarget;
        private IntPtr _lastExternal;
        private volatile IntPtr _ownTarget;
        private volatile string _status = "Готово";
        private bool _ownElevated;

        private bool _excluded;
        private string _excludedName;
        private IntPtr _excludeCheckHwnd;
        private double _excludeCheckAt;

        // Per-program profile of the window in front. A program may switch
        // off the bend, the shake, the roll or the close effect while the
        // global switches stay on.
        private bool _appBend = true;
        private bool _appShake = true;
        private bool _appRoll = true;
        private int _appCloseEffect = -1;
        private IntPtr _appCheckHwnd;
        private double _appCheckAt;

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
        private bool _resizeOnly;

        // Edge-resize watcher: after the user lets go of a resized window, a
        // short size wave makes the new size land like jelly. The watcher only
        // arms when the press began near a border and the size really changed.
        private IntPtr _resizeWatch;
        private Native.RECT _resizeStartRect;
        private Native.RECT _resizeLastRect;
        private double _resizeLastAt;
        private double _resizeCheckAt;
        private bool _resizeArmed;
        private double _resizeSpeed;
        // Some windows (WinUI, Notepad) finish the resize after the button is
        // released. The watcher waits a few frames for the final rectangle
        // before it starts the wave, so those windows get one too.
        private bool _resizePending;
        private Native.RECT _resizePendingRect;
        private double _resizePendingAt;
        private double _resizePendingUntil;
        private double _resizePendingSpeed;
        private int _resizeWaves;

        private Native.RECT _shakeRect;
        private DateTime _shakeStart;
        private bool _shakeZoomed;

        private DateTime _jellyStart;
        private double _jellyLastRender;
        private int _jellyFrames;

        // Window-state detection. The foreground window is watched for
        // maximize/restore transitions and for a return from the taskbar; a
        // short shake is scheduled as soon as the window has landed.
        private IntPtr _zoomWatch;
        private bool _zoomWatchState;
        private Native.RECT _zoomWatchRect;
        private IntPtr _minimizedWatch;
        private IntPtr _shakePending;
        private double _shakePendingAt;
        private double _shakeExpiresAt;
        private Native.RECT _shakePendingRect;
        private double _stateCheckAt;
        // Window classes whose PrintWindow call is too slow for a one-shot
        // wobble (see StartJelly), and the overlay used for those one-shots.
        // The wobble capture runs on a worker so a slow window cannot stall
        // the engine.
        private readonly System.Collections.Generic.HashSet<string> _slowCapture =
            new System.Collections.Generic.HashSet<string>();
        private readonly object _slowLock = new object();
        private readonly BendOverlay _shot = new BendOverlay();
        private volatile bool _shotBusy;

        // Close watcher: the picture of a window is taken when its close
        // button is under the cursor (or Alt+F4 is held); if the window is gone
        // a moment later, the picture is shattered. A window that only hides
        // because its program keeps running in the tray is rolled there
        // instead: nothing was actually closed.
        private IntPtr _closeWatch;
        private Bitmap _closeShot;
        private Native.RECT _closeFrame;
        private Native.POINT _closePoint;
        private double _closeShotAt;
        private double _closeCheckAt;
        private double _closeHiddenAt;
        private string _closeExe;

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
        private Native.POINT _minimizeCursor;
        private double _minimizeHoverAt;

        public JellyEngine(AppSettings settings)
        {
            _settings = settings;
            _autoPause = new AutoPause(settings);
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

        /// <summary>How many times the supervisor had to restart the worker.</summary>
        public int Restarts
        {
            get { return _restarts; }
        }

        /// <summary>
        /// How many resize waves have started since the process began. The
        /// self-test and the diagnostics report use it to prove that the
        /// effect actually ran.
        /// </summary>
        public int ResizeWaves
        {
            get { return _resizeWaves; }
        }

        /// <summary>
        /// True while the user asked for a temporary pause (the 30 minute
        /// item in the tray menu). The master switch is not touched.
        /// </summary>
        public bool ManuallyPaused
        {
            get { return _manualPause; }
            set { _manualPause = value; }
        }

        /// <summary>
        /// Why the effects are standing aside right now, or null when they
        /// are working. The settings pill and the tray menu show this.
        /// </summary>
        public string PauseReason
        {
            get
            {
                if (!_settings.Enabled) return "Пауза";
                if (_manualPause) return "Пауза на время";
                if (_autoPause.Paused) return _autoPause.Reason;
                if (_excluded) return "Исключение: " + _excludedName;
                return null;
            }
        }

        private bool Active
        {
            get
            {
                return _settings.Enabled && !_manualPause && !_autoPause.Paused && !_excluded;
            }
        }

        /// <summary>
        /// Refreshes the automatic pause and the per-program exclusion for
        /// the window in front. Runs from the engine thread, so the settings
        /// arrays are read as immutable snapshots.
        /// </summary>
        private void UpdateGates(IntPtr foreground, double now)
        {
            _autoPause.Update(foreground, now);

            if (foreground != _excludeCheckHwnd || now >= _excludeCheckAt)
            {
                _excludeCheckHwnd = foreground;
                _excludeCheckAt = now + 1.0;
                string exe = Native.GetWindowProcessName(foreground);
                string cls = Native.GetWindowClass(foreground);
                bool excluded = _settings.IsExcluded(exe, cls);
                if (excluded != _excluded)
                {
                    _excluded = excluded;
                    _excludedName = excluded ? (exe ?? cls) : null;
                    Log.Debug(excluded ? "Excluded: " + (exe ?? cls) : "Excluded: none");
                }
                else if (excluded)
                {
                    _excludedName = exe ?? cls;
                }
            }

            if (foreground != _appCheckHwnd || now >= _appCheckAt)
            {
                _appCheckHwnd = foreground;
                _appCheckAt = now + 0.5;
                string exe = Native.GetWindowProcessName(foreground);
                int effect;
                bool bend;
                bool shake;
                bool roll;
                bool found = _settings.GetAppProfile(exe, out effect, out bend,
                    out shake, out roll);
                _appBend = !found || bend;
                _appShake = !found || shake;
                _appRoll = !found || roll;
                _appCloseEffect = found ? effect : -1;
            }
        }

        private bool RollAllowed(string executableName)
        {
            int effect;
            bool bend;
            bool shake;
            bool roll;
            if (_settings.GetAppProfile(executableName, out effect, out bend,
                    out shake, out roll))
                return _settings.Roll && roll;
            return _settings.Roll;
        }

        public void Start()
        {
            if (_thread != null) return;
            bool elevated;
            if (Native.IsProcessElevated(Native.GetCurrentProcessId(), out elevated))
                _ownElevated = elevated;

            _thread = new Thread(Supervisor);
            _thread.IsBackground = true;
            _thread.Priority = ThreadPriority.AboveNormal;
            _thread.Name = "Okna.Engine";
            AdaptiveQuality.Reset();
            _thread.Start();
            Log.Info("Engine started. elevated=" + _ownElevated);
        }

        /// <summary>
        /// Keeps the engine worker alive. The worker itself already catches a
        /// failing tick, but a failure outside that inner guard (a COM call in
        /// the message pump, a broken capture object) must not leave the
        /// program running without any effects. The supervisor logs it and
        /// starts a fresh worker after a short pause.
        /// </summary>
        private void Supervisor()
        {
            int restarts = 0;
            while (!_stopping)
            {
                try
                {
                    Worker();
                }
                catch (Exception ex)
                {
                    Log.Error("Engine worker crashed: " + ex);
                }

                if (_stopping) break;
                restarts++;
                _restarts = restarts;
                Log.Error("Engine worker stopped unexpectedly; restarting (" + restarts + ")");
                try { Thread.Sleep(1000); } catch { }
            }
        }

        public void RequestShake()
        {
            _explicitShakeTarget = IntPtr.Zero;
            _shakeBypassProfile = true;
            _shakeRequested = true;
        }

        public void ShakeWindow(IntPtr hwnd)
        {
            _explicitShakeTarget = hwnd;
            _shakeBypassProfile = true;
            _shakeRequested = true;
        }

        /// <summary>
        /// Lets one window of this process be treated like any other target.
        /// The settings window registers itself here, so it can be dragged and
        /// wobbled while the jelly slider is being tried out. Everything else
        /// in the process (the tray, dialogs) stays out of the effects.
        /// </summary>
        public void AllowOwnWindow(IntPtr hwnd)
        {
            _ownTarget = hwnd;
        }

        public void DisallowOwnWindow(IntPtr hwnd)
        {
            if (_ownTarget == hwnd) _ownTarget = IntPtr.Zero;
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
                    double frameMs = dt * 1000.0;
                    last = now;
                    if (dt < 0.001)
                    {
                        Thread.Sleep(1);
                        continue;
                    }
                    if (dt > 0.05) dt = 0.05;
                    AdaptiveQuality.Sample(frameMs, now);

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
                    if (_mode == Mode.Jelly)
                    {
                        // The wobble is drawn from the one-shot overlay; never
                        // leave that copy on screen.
                        _shot.Hide();
                        _overlayActive = false;
                    }
                    else if (_overlayActive)
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
                    if (_overlay != null) _overlay.Dispose();
                    // A capture that is still rendering owns the one-shot
                    // overlay's buffers; let it finish and let the process
                    // reclaim them.
                    if (!_shotBusy) _shot.Dispose();
                    ClearCloseWatch();
                    ClearMinimizeWatch();
                    ShotPool.Clear();
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
                bool userRequest = _shakeBypassProfile;
                _shakeBypassProfile = false;
                IntPtr requested = _explicitShakeTarget;
                _explicitShakeTarget = IntPtr.Zero;
                if (_mode == Mode.Idle)
                    StartJelly(requested, userRequest);
            }

            if (_mode == Mode.Idle)
            {
                IntPtr foreground = Native.GetForegroundWindow();
                if (foreground != IntPtr.Zero && !Native.IsOwnProcessWindow(foreground) &&
                    Native.IsWindowVisible(foreground) && !Native.IsIconic(foreground))
                    _lastExternal = foreground;

                UpdateGates(foreground, now);

                bool closeOn = _appCloseEffect >= 0
                    ? _appCloseEffect != 0
                    : (_settings.Shatter || _settings.Burn);
                if (Active && closeOn)
                    WatchClose(foreground, now);
                else
                    ClearCloseWatch();

                if (Active && _settings.Roll && _appRoll)
                    WatchMinimize(foreground, now);
                else
                    ClearMinimizeWatch();

                if (Active && _settings.MaxShake && _appShake)
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

                if (_shakePending != IntPtr.Zero)
                {
                    IntPtr pending = _shakePending;
                    // The shell's zoom transition moves the window about a
                    // tenth of a second after the state flips. Shake as soon as
                    // the frame has moved (the user sees the window arrive and
                    // wobble in one go) and fall back to the deadline for the
                    // cases where it does not move at all.
                    Native.RECT rect;
                    bool landed = Native.GetWindowRect(pending, out rect) &&
                                  !SameRect(rect, _shakePendingRect);
                    if (now > _shakeExpiresAt)
                    {
                        // Nothing arrived in time: a wobble now would look like
                        // it happened on its own.
                        _shakePending = IntPtr.Zero;
                    }
                    else if (now >= _shakePendingAt || landed)
                    {
                        // A drag-to-the-top snap maximizes while the button is
                        // still held: wait for the release instead of dropping
                        // the wobble.
                        if (!IsKeyDown(Native.VK_LBUTTON))
                        {
                            _shakePending = IntPtr.Zero;
                            if (Native.IsWindow(pending) && Native.IsWindowVisible(pending) &&
                                !Native.IsIconic(pending) && Native.GetForegroundWindow() == pending)
                            {
                                StartJelly(pending);
                            }
                        }
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

            // The resize watcher runs beside the idle/candidate states: a
            // press near a border is not a drag, but the size change has to be
            // noticed before the button goes up so the wave can start then.
            if ((_mode == Mode.Idle || _mode == Mode.Candidate) &&
                Active && _settings.Bend && !_settings.Economy && _appBend)
                WatchResize(Native.GetForegroundWindow(), now);
            else if (_mode == Mode.Idle || _mode == Mode.Candidate)
                ClearResizeWatch();

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
                    if (Active && !shift && !escape)
                        TryBeginCandidate();
                }
                return;
            }

            if (_mode == Mode.Candidate)
            {
                if (!down || shift || !Active)
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
                if (!Active)
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
                    SetStatus("Отменено");
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
                if (_resizeOnly) minTime = 0.62;
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

        private void ResetEngine(string status)
        {
            if (_overlayActive)
            {
                if (_mode == Mode.Jelly)
                {
                    // The jelly copy must never drag the real window back to a
                    // stale rectangle (the user may have restored it meanwhile).
                    RestoreTargetVisibility();
                    _shot.Hide();
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
            _resizeOnly = false;
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
            {
                _status = status;
                if (StatusChanged != null) StatusChanged(status);
            }
        }

        public event Action<string> StatusChanged;

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
