using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Okna
{
    /// <summary>
    /// Adds per-window lock commands to foreign system menus and restores the
    /// selected bounds while the application is running.
    /// </summary>
    internal sealed class WindowBoundsLockManager : IDisposable
    {
        private const uint ScanIntervalMs = 1000;
        private const uint PositionIdStart = 0xE100;
        private const uint PositionIdEnd = 0xEFFE;
        private const uint MenuNotFound = 0xFFFFFFFF;

        private readonly Dictionary<IntPtr, WindowState> _windows =
            new Dictionary<IntPtr, WindowState>();
        private readonly Native.WinEventProc _eventCallback;
        private readonly Timer _timer;
        private IntPtr _invokeHook;
        private IntPtr _locationHook;
        private IntPtr _foregroundHook;
        private IntPtr _activeWindow;
        private DateTime _nextScan;
        private bool _disposed;

        private sealed class WindowState
        {
            public IntPtr Handle;
            public uint ProcessId;
            public uint ThreadId;
            public uint PositionCommand;
            public uint SizeCommand;
            public string PositionLabel;
            public string SizeLabel;
            public bool PositionLocked;
            public bool SizeLocked;
            public Native.RECT PositionBounds;
            public Native.RECT SizeBounds;
            public DateTime LastFailureLog;
        }

        public WindowBoundsLockManager()
        {
            _eventCallback = OnWinEvent;
            _timer = new Timer();
            _timer.Interval = 30;
            _timer.Tick += OnTimer;
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException("WindowBoundsLockManager");
            if (_timer.Enabled) return;

            _invokeHook = Native.SetWinEventHook(
                Native.EVENT_OBJECT_INVOKED, Native.EVENT_OBJECT_INVOKED,
                IntPtr.Zero, _eventCallback, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            if (_invokeHook == IntPtr.Zero)
            {
                Log.Error("Window bounds lock menu unavailable: SetWinEventHook failed with error " +
                    Marshal.GetLastWin32Error());
            }

            _locationHook = Native.SetWinEventHook(
                Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _eventCallback, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            if (_locationHook == IntPtr.Zero)
                Log.Debug("Window bounds lock is using timer-only enforcement; location hook failed with error " +
                    Marshal.GetLastWin32Error());

            _foregroundHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _eventCallback, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            if (_foregroundHook == IntPtr.Zero)
                Log.Debug("Window bounds lock is using timer-only active-window tracking; foreground hook failed with error " +
                    Marshal.GetLastWin32Error());

            ScanWindows();
            RememberForegroundWindow(Native.GetForegroundWindow());
            _nextScan = DateTime.UtcNow.AddMilliseconds(ScanIntervalMs);
            _timer.Start();
        }

        private void OnTimer(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if (now >= _nextScan)
            {
                ScanWindows();
                _nextScan = now.AddMilliseconds(ScanIntervalMs);
            }
            RememberForegroundWindow(Native.GetForegroundWindow());

            foreach (WindowState state in _windows.Values)
                EnforceBounds(state);
        }

        private void ScanWindows()
        {
            Native.EnumWindowsProc callback = delegate(IntPtr hwnd, IntPtr unused)
            {
                TrackWindow(hwnd);
                return true;
            };

            if (!Native.EnumWindows(callback, IntPtr.Zero))
                Log.Debug("Window bounds lock scan failed with error " + Marshal.GetLastWin32Error());

            List<IntPtr> stale = null;
            foreach (KeyValuePair<IntPtr, WindowState> pair in _windows)
            {
                WindowState state = pair.Value;
                if (!IsSameWindow(state))
                {
                    if (stale == null) stale = new List<IntPtr>();
                    stale.Add(pair.Key);
                    continue;
                }
            }

            if (stale != null)
                for (int i = 0; i < stale.Count; i++)
                    _windows.Remove(stale[i]);
        }

        private static bool IsOwnedLabel(string text, string previous, string russian)
        {
            return string.Equals(text, previous, StringComparison.Ordinal) ||
                   string.Equals(text, russian, StringComparison.Ordinal) ||
                   string.Equals(text, Loc.T(russian), StringComparison.Ordinal);
        }

        private static string ReadMenuText(IntPtr menu, uint command)
        {
            StringBuilder text = new StringBuilder(256);
            int length = Native.GetMenuString(menu, command, text, text.Capacity,
                Native.MF_BYCOMMAND);
            return length <= 0 ? null : text.ToString();
        }

        private void EnsureSystemMenu(WindowState state)
        {
            IntPtr menu = Native.GetSystemMenu(state.Handle, false);
            if (menu == IntPtr.Zero) return;

            if (state.PositionCommand == 0 || state.SizeCommand == 0)
            {
                if (!AllocateCommands(state, menu)) return;
            }

            string positionLabel = Loc.T("Зафиксировать положение");
            string sizeLabel = Loc.T("Зафиксировать размер");
            if (!EnsureMenuItem(menu, ref state.PositionCommand, positionLabel,
                    state.PositionLabel, "Зафиксировать положение") ||
                !EnsureMenuItem(menu, ref state.SizeCommand, sizeLabel,
                    state.SizeLabel, "Зафиксировать размер"))
            {
                // A window can rebuild its system menu. Do not overwrite an
                // application item if its command ID has since been reused.
                RemoveOwnedMenuItems(state, menu);
                state.PositionCommand = 0;
                state.SizeCommand = 0;
                state.PositionLabel = null;
                state.SizeLabel = null;
                if (!AllocateCommands(state, menu)) return;
                if (!EnsureMenuItem(menu, ref state.PositionCommand, positionLabel,
                        null, "Зафиксировать положение") ||
                    !EnsureMenuItem(menu, ref state.SizeCommand, sizeLabel,
                        null, "Зафиксировать размер"))
                    return;
            }

            state.PositionLabel = positionLabel;
            state.SizeLabel = sizeLabel;
            UpdateMenuChecks(state, menu);
        }

        private static bool EnsureMenuItem(IntPtr menu, ref uint command, string label,
            string previousLabel, string russianLabel)
        {
            uint itemState = Native.GetMenuState(menu, command, Native.MF_BYCOMMAND);
            if (itemState == MenuNotFound)
            {
                uint existingCommand = FindExistingCommand(menu, previousLabel, russianLabel);
                if (existingCommand != 0)
                {
                    command = existingCommand;
                    return true;
                }
                if (Native.AppendMenu(menu, Native.MF_STRING, new UIntPtr(command), label))
                    return true;
                Log.Debug("Could not add a window bounds lock menu item; error=" +
                    Marshal.GetLastWin32Error());
                return false;
            }

            string existing = ReadMenuText(menu, command);
            if (!IsOwnedLabel(existing, previousLabel, russianLabel))
                return false;
            if (!string.Equals(existing, label, StringComparison.Ordinal))
            {
                if (Native.ModifyMenu(menu, command,
                        Native.MF_BYCOMMAND | Native.MF_STRING, new UIntPtr(command), label))
                    return true;
                Log.Debug("Could not update a window bounds lock menu item; error=" +
                    Marshal.GetLastWin32Error());
                return false;
            }
            return true;
        }

        private static uint FindExistingCommand(IntPtr menu, string previousLabel,
            string russianLabel)
        {
            int count = Native.GetMenuItemCount(menu);
            for (int i = 0; i < count; i++)
            {
                StringBuilder text = new StringBuilder(256);
                int length = Native.GetMenuString(menu, (uint)i, text, text.Capacity,
                    Native.MF_BYPOSITION);
                if (length <= 0 ||
                    !IsOwnedLabel(text.ToString(), previousLabel, russianLabel))
                    continue;

                uint command = Native.GetMenuItemID(menu, i);
                if (command != MenuNotFound) return command;
            }
            return 0;
        }

        private static bool AllocateCommands(WindowState state, IntPtr menu)
        {
            uint existingPosition = 0;
            uint existingSize = 0;
            int itemCount = Native.GetMenuItemCount(menu);
            for (int i = 0; i < itemCount; i++)
            {
                StringBuilder text = new StringBuilder(256);
                int length = Native.GetMenuString(menu, (uint)i, text, text.Capacity,
                    Native.MF_BYPOSITION);
                if (length <= 0) continue;

                uint command = Native.GetMenuItemID(menu, i);
                if (command == MenuNotFound) continue;
                string label = text.ToString();
                if (existingPosition == 0 &&
                    IsOwnedLabel(label, null, "Зафиксировать положение"))
                    existingPosition = command;
                else if (existingSize == 0 &&
                    IsOwnedLabel(label, null, "Зафиксировать размер"))
                    existingSize = command;
            }

            state.PositionCommand = existingPosition;
            state.SizeCommand = existingSize;
            if (state.PositionCommand != 0 && state.SizeCommand != 0)
                return true;

            for (uint positionCommand = PositionIdStart;
                 positionCommand <= PositionIdEnd; positionCommand += 2)
            {
                uint sizeCommand = positionCommand + 1;
                if (state.PositionCommand == 0 &&
                    Native.GetMenuState(menu, positionCommand, Native.MF_BYCOMMAND) == MenuNotFound)
                {
                    state.PositionCommand = positionCommand;
                    if (state.SizeCommand != 0) return true;
                }
                if (state.SizeCommand == 0 &&
                    Native.GetMenuState(menu, sizeCommand, Native.MF_BYCOMMAND) == MenuNotFound)
                {
                    state.SizeCommand = sizeCommand;
                    if (state.PositionCommand != 0) return true;
                }
            }

            Log.Debug("Window bounds lock menu skipped: no unused system-menu command IDs");
            return false;
        }

        private static void UpdateMenuChecks(WindowState state, IntPtr menu)
        {
            Native.CheckMenuItem(menu, state.PositionCommand,
                Native.MF_BYCOMMAND | (state.PositionLocked ? Native.MF_CHECKED : 0));
            Native.CheckMenuItem(menu, state.SizeCommand,
                Native.MF_BYCOMMAND | (state.SizeLocked ? Native.MF_CHECKED : 0));
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd,
            int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (_disposed || hwnd == IntPtr.Zero) return;

            try
            {
                if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
                {
                    RememberForegroundWindow(hwnd);
                    return;
                }

                WindowState state;
                if (!_windows.TryGetValue(hwnd, out state)) return;
                if (!IsSameWindow(state))
                {
                    _windows.Remove(hwnd);
                    return;
                }

                if (eventType == Native.EVENT_OBJECT_LOCATIONCHANGE)
                {
                    if (objectId == Native.OBJID_WINDOW) EnforceBounds(state);
                    return;
                }
                if (eventType != Native.EVENT_OBJECT_INVOKED) return;

                IntPtr menu = Native.GetSystemMenu(hwnd, false);
                if (menu == IntPtr.Zero) return;

                uint command;
                if (!TryResolveMenuCommand(objectId, childId, state.PositionCommand,
                        state.SizeCommand, menu, out command))
                    return;

                bool changed;
                if (command == state.PositionCommand)
                    changed = TogglePosition(state);
                else if (command == state.SizeCommand)
                    changed = ToggleSize(state);
                else return;

                UpdateMenuChecks(state, menu);
                EnforceBounds(state);
                Log.Debug("System-menu bounds command " + command + " invoked for " +
                    state.Handle.ToInt64() + "; position=" + state.PositionLocked +
                    "; size=" + state.SizeLocked + "; captured=" + changed +
                    "; object=" + objectId + "; child=" + childId);
            }
            catch (Exception ex)
            {
                Log.Error("Window bounds lock menu event failed: " + ex);
            }
        }

        internal static bool TryResolveMenuCommand(int objectId, int childId,
            uint positionCommand, uint sizeCommand, IntPtr menu, out uint command)
        {
            command = 0;
            if (objectId != Native.OBJID_MENU && objectId != Native.OBJID_SYSMENU)
                return false;

            command = unchecked((uint)childId);
            if (command != positionCommand && command != sizeCommand)
            {
                if (childId < 0 || menu == IntPtr.Zero ||
                    childId >= Native.GetMenuItemCount(menu))
                    return false;
                command = Native.GetMenuItemID(menu, childId);
            }
            return command == positionCommand || command == sizeCommand;
        }

        private void RememberForegroundWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            IntPtr root = Native.GetAncestor(hwnd, Native.GA_ROOT);

            RememberActiveWindow(root == IntPtr.Zero ? hwnd : root);
        }

        internal bool RememberActiveWindow(IntPtr hwnd)
        {
            WindowState state = TrackWindow(hwnd);
            if (state == null) return false;
            _activeWindow = state.Handle;
            return true;
        }

        private WindowState TrackWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return null;
            uint processId;
            uint threadId = Native.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0 || processId == Native.GetCurrentProcessId()) return null;

            string className = Native.GetWindowClass(hwnd);
            if (!IsTrackableWindowClass(className))
                return null;

            WindowState state;
            if (!_windows.TryGetValue(hwnd, out state) ||
                state.ProcessId != processId || state.ThreadId != threadId)
            {
                state = new WindowState();
                state.Handle = hwnd;
                state.ProcessId = processId;
                state.ThreadId = threadId;
                _windows[hwnd] = state;
            }
            EnsureSystemMenu(state);
            return state;
        }

        internal static bool IsTrackableWindowClass(string className)
        {
            return className != "Progman" && className != "WorkerW" &&
                className != "Shell_TrayWnd" && className != "Shell_SecondaryTrayWnd" &&
                !JellyEngine.IsTransientOpenWindowClass(className);
        }

        internal IntPtr LastActiveWindow
        {
            get
            {
                WindowState state;
                if (_activeWindow == IntPtr.Zero ||
                    !_windows.TryGetValue(_activeWindow, out state) ||
                    !IsSameWindow(state))
                {
                    _activeWindow = IntPtr.Zero;
                    return IntPtr.Zero;
                }
                return _activeWindow;
            }
        }

        internal bool TryGetLockState(IntPtr hwnd, out bool positionLocked,
            out bool sizeLocked)
        {
            positionLocked = false;
            sizeLocked = false;
            WindowState state;
            if (hwnd == IntPtr.Zero || !_windows.TryGetValue(hwnd, out state) ||
                !IsSameWindow(state))
                return false;
            positionLocked = state.PositionLocked;
            sizeLocked = state.SizeLocked;
            return true;
        }

        internal bool ToggleLastActivePosition()
        {
            return ToggleWindowLock(LastActiveWindow, true);
        }

        internal bool ToggleLastActiveSize()
        {
            return ToggleWindowLock(LastActiveWindow, false);
        }

        internal bool ToggleWindowLock(IntPtr hwnd, bool position)
        {
            WindowState state;
            if (hwnd == IntPtr.Zero || !_windows.TryGetValue(hwnd, out state) ||
                !IsSameWindow(state) || Native.IsIconic(hwnd))
                return false;

            bool toggled = position ? TogglePosition(state) : ToggleSize(state);
            if (!toggled) return false;
            IntPtr menu = Native.GetSystemMenu(hwnd, false);
            if (menu != IntPtr.Zero) UpdateMenuChecks(state, menu);
            EnforceBounds(state);
            return true;
        }

        private static bool TogglePosition(WindowState state)
        {
            if (state.PositionLocked)
            {
                state.PositionLocked = false;
                return true;
            }

            Native.RECT bounds;
            if (!Native.GetWindowRect(state.Handle, out bounds))
            {
                Log.Debug("Could not capture window position for " + state.Handle.ToInt64());
                return false;
            }
            state.PositionBounds = bounds;
            state.PositionLocked = true;
            return true;
        }

        private static bool ToggleSize(WindowState state)
        {
            if (state.SizeLocked)
            {
                state.SizeLocked = false;
                return true;
            }

            Native.RECT bounds;
            if (!Native.GetWindowRect(state.Handle, out bounds))
            {
                Log.Debug("Could not capture window size for " + state.Handle.ToInt64());
                return false;
            }
            state.SizeBounds = bounds;
            state.SizeLocked = true;
            return true;
        }

        private static Native.RECT GetLockedBounds(Native.RECT current,
            Native.RECT positionBounds, Native.RECT sizeBounds,
            bool lockPosition, bool lockSize)
        {
            Native.RECT result = current;
            if (lockPosition)
            {
                result.Left = positionBounds.Left;
                result.Top = positionBounds.Top;
                result.Right = result.Left + current.Width;
                result.Bottom = result.Top + current.Height;
            }
            if (lockSize)
            {
                int width = sizeBounds.Width;
                int height = sizeBounds.Height;
                result.Right = result.Left + width;
                result.Bottom = result.Top + height;
            }
            return result;
        }

        internal static bool TryGetLockedBounds(Native.RECT current,
            Native.RECT positionBounds, Native.RECT sizeBounds,
            bool lockPosition, bool lockSize, out Native.RECT result)
        {
            result = GetLockedBounds(current, positionBounds, sizeBounds,
                lockPosition, lockSize);
            return lockPosition || lockSize;
        }

        private static void EnforceBounds(WindowState state)
        {
            if (!state.PositionLocked && !state.SizeLocked) return;
            if (!IsSameWindow(state) || Native.IsIconic(state.Handle)) return;

            Native.RECT current;
            if (!Native.GetWindowRect(state.Handle, out current)) return;

            Native.RECT desired = GetLockedBounds(current, state.PositionBounds,
                state.SizeBounds, state.PositionLocked, state.SizeLocked);
            bool positionChanged = desired.Left != current.Left || desired.Top != current.Top;
            bool sizeChanged = desired.Width != current.Width || desired.Height != current.Height;
            if (!positionChanged && !sizeChanged) return;

            uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;
            if (!positionChanged) flags |= Native.SWP_NOMOVE;
            if (!sizeChanged) flags |= Native.SWP_NOSIZE;
            if (!Native.SetWindowPos(state.Handle, IntPtr.Zero, desired.Left, desired.Top,
                    desired.Width, desired.Height, flags))
            {
                DateTime now = DateTime.UtcNow;
                if (now - state.LastFailureLog >= TimeSpan.FromSeconds(5))
                {
                    Log.Debug("Could not restore locked bounds for " + state.Handle.ToInt64() +
                        "; SetWindowPos error=" + Marshal.GetLastWin32Error());
                    state.LastFailureLog = now;
                }
            }
        }

        private static bool IsSameWindow(WindowState state)
        {
            if (!Native.IsWindow(state.Handle)) return false;
            uint processId;
            uint threadId = Native.GetWindowThreadProcessId(state.Handle, out processId);
            return threadId == state.ThreadId && processId == state.ProcessId;
        }

        private static void RemoveOwnedMenuItems(WindowState state, IntPtr menu)
        {
            if (state.PositionCommand != 0)
            {
                string text = ReadMenuText(menu, state.PositionCommand);
                if (IsOwnedLabel(text, state.PositionLabel, "Зафиксировать положение"))
                    Native.DeleteMenu(menu, state.PositionCommand, Native.MF_BYCOMMAND);
            }
            if (state.SizeCommand != 0)
            {
                string text = ReadMenuText(menu, state.SizeCommand);
                if (IsOwnedLabel(text, state.SizeLabel, "Зафиксировать размер"))
                    Native.DeleteMenu(menu, state.SizeCommand, Native.MF_BYCOMMAND);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTimer;
            _timer.Dispose();
            if (_invokeHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_invokeHook);
                _invokeHook = IntPtr.Zero;
            }
            if (_locationHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_locationHook);
                _locationHook = IntPtr.Zero;
            }
            if (_foregroundHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }

            foreach (WindowState state in _windows.Values)
            {
                if (IsSameWindow(state))
                {
                    IntPtr menu = Native.GetSystemMenu(state.Handle, false);
                    if (menu != IntPtr.Zero) RemoveOwnedMenuItems(state, menu);
                }
            }
            _windows.Clear();
        }
    }
}
