using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Okna
{
    internal sealed partial class JellyEngine
    {
        private readonly Native.WinEventProc _windowShowCallback;
        private IntPtr _windowShowHook;
        private Stopwatch _windowShowClock;
        private IntPtr _windowShown;
        private double _windowShownAt;
        private IntPtr _windowOpenPending;
        private double _windowOpenAt;
        private int _windowOpenWatchChecks;
        private IntPtr _windowOpenWatchHwnd;
        private readonly HashSet<IntPtr> _windowOpenTriggered = new HashSet<IntPtr>();

        internal static bool UsesLightWindowOpenEffect(int effect, bool economy, int adaptiveLevel)
        {
            if (effect == 2) return true;
            if (effect == 1 || effect == 3)
                return economy || adaptiveLevel > 0;
            return false;
        }

        private void InstallWindowOpenHook(Stopwatch clock)
        {
            _windowShowClock = clock;
            _windowShowHook = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW,
                Native.EVENT_OBJECT_SHOW, IntPtr.Zero, _windowShowCallback, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT);
            if (_windowShowHook == IntPtr.Zero)
                Log.Error("Window-open effect hook could not be installed.");
        }

        private void UninstallWindowOpenHook()
        {
            if (_windowShowHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_windowShowHook);
                _windowShowHook = IntPtr.Zero;
            }
            _windowShowClock = null;
            _windowShown = IntPtr.Zero;
            _windowOpenPending = IntPtr.Zero;
            _windowOpenWatchHwnd = IntPtr.Zero;
            _windowOpenTriggered.Clear();
        }

        private void OnWindowShown(IntPtr hook, uint eventType, IntPtr hwnd,
            int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (eventType != Native.EVENT_OBJECT_SHOW ||
                objectId != Native.OBJID_WINDOW || childId != Native.CHILDID_SELF ||
                _settings.WindowOpenEffect == 0 || !IsWindowOpenCandidate(hwnd))
                return;

            Stopwatch clock = _windowShowClock;
            if (clock == null) return;
            _windowShown = hwnd;
            _windowShownAt = clock.Elapsed.TotalSeconds;
        }

        private void WatchWindowOpen(IntPtr foreground, double now)
        {
            _windowOpenWatchChecks++;
            if (_settings.WindowOpenEffect == 0 || !Active)
            {
                _windowShown = IntPtr.Zero;
                _windowOpenPending = IntPtr.Zero;
                _windowOpenWatchHwnd = foreground;
                return;
            }

            if (_windowOpenPending != IntPtr.Zero)
            {
                if (_mode != Mode.Idle ||
                    !IsWindowOpenCandidate(_windowOpenPending))
                {
                    _windowOpenPending = IntPtr.Zero;
                }
                else if (now >= _windowOpenAt)
                {
                    IntPtr target = _windowOpenPending;
                    int effect = _settings.WindowOpenEffect;
                    _windowOpenPending = IntPtr.Zero;
                    _windowShown = IntPtr.Zero;
                    if (_windowOpenTriggered.Add(target))
                    {
                        if (_windowOpenTriggered.Count > 600)
                            _windowOpenTriggered.Clear();
                        if (UsesLightWindowOpenEffect(effect, _settings.Economy, AdaptiveQuality.Level))
                            StartShake(target);
                        else
                            StartJelly(target, false);
                    }
                    return;
                }
            }

            bool foregroundChanged = foreground != _windowOpenWatchHwnd;
            _windowOpenWatchHwnd = foreground;
            bool recentlyShown = _windowShown != IntPtr.Zero &&
                _windowShown == foreground && now - _windowShownAt <= 1.8;
            if (!foregroundChanged && !recentlyShown)
            {
                _windowShown = IntPtr.Zero;
                return;
            }
            if (!IsWindowOpenCandidate(foreground) ||
                _windowOpenTriggered.Contains(foreground) ||
                _windowOpenPending == foreground)
            {
                _windowShown = IntPtr.Zero;
                return;
            }

            _windowOpenPending = foreground;
            _windowOpenAt = now + 0.28;
            _windowShown = IntPtr.Zero;
        }

        internal bool SmokeIdleWindowOpenWatcherRuns()
        {
            bool enabled = _settings.Enabled;
            int effect = _settings.WindowOpenEffect;
            _settings.Enabled = false;
            _settings.WindowOpenEffect = 1;
            _windowOpenWatchChecks = 0;
            try
            {
                Tick(0.008, 0.008);
                return _windowOpenWatchChecks == 1;
            }
            finally
            {
                _settings.Enabled = enabled;
                _settings.WindowOpenEffect = effect;
            }
        }

        private bool IsWindowOpenCandidate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) ||
                !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) ||
                Native.IsZoomed(hwnd) || Native.IsOwnProcessWindow(hwnd))
                return false;

            string cls = Native.GetWindowClass(hwnd);
            if (IsTransientOpenWindow(hwnd, cls) || JellyEngine.IsShellWindowClass(cls) ||
                _settings.IsExcluded(Native.GetWindowProcessName(hwnd), cls))
                return false;

            Native.RECT rect;
            return Native.GetWindowRect(hwnd, out rect);
        }

        private static bool IsTransientOpenWindow(IntPtr hwnd, string cls)
        {
            if (IsTransientOpenWindowClass(cls))
                return true;
            if (cls == "Windows.UI.Core.CoreWindow" &&
                Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero)
                return true;

            long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE);
            long extendedStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
            if ((extendedStyle & Native.WS_EX_TOOLWINDOW) != 0 &&
                (extendedStyle & Native.WS_EX_APPWINDOW) == 0 &&
                Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero)
                return true;
            if ((style & Native.WS_CHILD) != 0) return true;

            Native.RECT rect;
            return Native.GetWindowRect(hwnd, out rect) &&
                (rect.Width < 80 || rect.Height < 50);
        }

        internal static bool IsTransientOpenWindowClass(string cls)
        {
            return string.IsNullOrEmpty(cls) || cls == "#32768" ||
                cls == "tooltips_class32" || cls == "ComboLBox" ||
                cls == "Auto-Suggest Dropdown" || cls == "Xaml_WindowedPopupClass";
        }
    }
}
