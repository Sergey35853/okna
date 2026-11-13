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
        // The engine thread reads the lock state while the UI thread updates
        // it. A separate immutable snapshot keeps that read safe; touching the
        // main dictionary from two threads is not.
        private readonly object _stateLock = new object();
        private readonly Dictionary<IntPtr, LockSnapshot> _lockStates =
            new Dictionary<IntPtr, LockSnapshot>();
        private readonly Native.WinEventProc _eventCallback;
        private readonly Native.HookProc _mouseCallback;
        private readonly Native.HookProc _keyboardCallback;
        private readonly Timer _timer;
        private IntPtr _invokeHook;
        private IntPtr _focusHook;
        private IntPtr _menuHook;
        private IntPtr _locationHook;
        private IntPtr _foregroundHook;
        private IntPtr _mouseHook;
        private IntPtr _keyboardHook;
        private IntPtr _activeWindow;
        private DateTime _nextScan;
        private bool _disposed;

        // System menus do not raise EVENT_OBJECT_INVOKED on current Windows
        // versions, so a real mouse click is observed with a low-level hook.
        // Keyboard activation is tracked through menu focus events + Enter.
        private IntPtr _pressedOwner;
        private uint _pressedCommand;
        private bool _pressedValid;
        private IntPtr _keyboardOwner;
        private uint _keyboardCommand;
        private bool _keyboardValid;
        private WindowState _pendingState;
        private uint _pendingCommand;
        private string _pendingSource;
        private IntPtr _lastInputOwner;
        private uint _lastInputCommand;
        private DateTime _lastInputAt;

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
            public DateTime LastCancelAt;
            public DateTime MenuRetryAfter;
            public DateTime LastMenuFailureLog;
            public int MenuFailures;
        }

        private sealed class LockSnapshot
        {
            public uint ProcessId;
            public uint ThreadId;
            public bool PositionLocked;
            public bool SizeLocked;
        }

        public WindowBoundsLockManager()
        {
            _eventCallback = OnWinEvent;
            _mouseCallback = OnMouseHook;
            _keyboardCallback = OnKeyboardHook;
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

            _focusHook = Native.SetWinEventHook(
                Native.EVENT_OBJECT_FOCUS, Native.EVENT_OBJECT_FOCUS,
                IntPtr.Zero, _eventCallback, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            if (_focusHook == IntPtr.Zero)
                Log.Debug("Window bounds lock cannot track keyboard menu focus; error=" +
                    Marshal.GetLastWin32Error());

            _menuHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_MENUSTART, Native.EVENT_SYSTEM_MENUPOPUPEND,
                IntPtr.Zero, _eventCallback, 0, 0, Native.WINEVENT_OUTOFCONTEXT);

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

            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL,
                _mouseCallback, IntPtr.Zero, 0);
            if (_mouseHook == IntPtr.Zero)
                Log.Error("Window bounds lock cannot detect menu clicks; mouse hook failed with error " +
                    Marshal.GetLastWin32Error());

            _keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL,
                _keyboardCallback, IntPtr.Zero, 0);
            if (_keyboardHook == IntPtr.Zero)
                Log.Debug("Window bounds lock cannot detect keyboard menu activation; error=" +
                    Marshal.GetLastWin32Error());

            ScanWindows();
            RememberForegroundWindow(Native.GetForegroundWindow());
            _nextScan = DateTime.UtcNow.AddMilliseconds(ScanIntervalMs);
            _timer.Start();
        }

        private void OnTimer(object sender, EventArgs e)
        {
            ProcessPendingToggle();

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

        /// <summary>
        /// Applies a lock toggle that was queued by the mouse or keyboard
        /// hook. Runs on the UI thread, so the hooks themselves stay short.
        /// </summary>
        private void ProcessPendingToggle()
        {
            WindowState state = _pendingState;
            if (state == null) return;
            uint command = _pendingCommand;
            string source = _pendingSource;
            _pendingState = null;
            _pendingCommand = 0;
            _pendingSource = null;

            if (!IsSameWindow(state) || Native.IsIconic(state.Handle)) return;
            if (command != state.PositionCommand && command != state.SizeCommand) return;

            bool changed = command == state.PositionCommand
                ? TogglePosition(state)
                : ToggleSize(state);
            if (!changed) return;
            PublishLockState(state);

            IntPtr menu = Native.GetSystemMenu(state.Handle, false);
            if (menu != IntPtr.Zero) UpdateMenuChecks(state, menu);
            EnforceBounds(state);
            Log.Debug("System-menu bounds command " + command + " invoked for " +
                state.Handle.ToInt64() + " via " + source + "; position=" +
                state.PositionLocked + "; size=" + state.SizeLocked);
        }

        private void QueueToggle(WindowState state, uint command, string source)
        {
            if (state == null || command == 0) return;
            _pendingState = state;
            _pendingCommand = command;
            _pendingSource = source;
            _lastInputOwner = state.Handle;
            _lastInputCommand = command;
            _lastInputAt = DateTime.UtcNow;
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
                {
                    _windows.Remove(stale[i]);
                    ForgetLockState(stale[i]);
                }
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
            DateTime now = DateTime.UtcNow;
            if (now < state.MenuRetryAfter) return;

            IntPtr menu = Native.GetSystemMenu(state.Handle, false);
            if (menu == IntPtr.Zero) return;

            if (state.PositionCommand == 0 || state.SizeCommand == 0)
            {
                if (!AllocateCommands(state, menu))
                {
                    MarkMenuUnavailable(state, 0);
                    return;
                }
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
                if (!AllocateCommands(state, menu))
                {
                    MarkMenuUnavailable(state, 0);
                    return;
                }
                if (!EnsureMenuItem(menu, ref state.PositionCommand, positionLabel,
                        null, "Зафиксировать положение") ||
                    !EnsureMenuItem(menu, ref state.SizeCommand, sizeLabel,
                        null, "Зафиксировать размер"))
                {
                    MarkMenuUnavailable(state, Marshal.GetLastWin32Error());
                    return;
                }
            }

            state.PositionLabel = positionLabel;
            state.SizeLabel = sizeLabel;
            state.MenuFailures = 0;
            UpdateMenuChecks(state, menu);
        }

        /// <summary>
        /// Backs off from a window whose system menu cannot be modified (for
        /// example an elevated process while Okna runs unelevated) instead of
        /// retrying every second and flooding the log.
        /// </summary>
        private static void MarkMenuUnavailable(WindowState state, int error)
        {
            DateTime now = DateTime.UtcNow;
            state.MenuRetryAfter = now.AddSeconds(60);
            state.MenuFailures++;
            if (now - state.LastMenuFailureLog < TimeSpan.FromMinutes(5)) return;
            state.LastMenuFailureLog = now;
            Log.Debug("Window bounds lock menu skipped for " + state.Handle.ToInt64() +
                "; error=" + error + "; attempts=" + state.MenuFailures);
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
                if (eventType == Native.EVENT_OBJECT_FOCUS)
                {
                    TrackMenuFocus(hwnd, childId, eventThread);
                    return;
                }
                if (eventType == Native.EVENT_SYSTEM_MENUEND ||
                    eventType == Native.EVENT_SYSTEM_MENUPOPUPEND)
                {
                    _keyboardValid = false;
                    return;
                }
                if (eventType == Native.EVENT_SYSTEM_MENUSTART)
                    return;

                if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
                {
                    RememberForegroundWindow(hwnd);
                    return;
                }

                WindowState state;
                if (!_windows.TryGetValue(hwnd, out state))
                {
                    ForgetLockState(hwnd);
                    return;
                }
                if (!IsSameWindow(state))
                {
                    _windows.Remove(hwnd);
                    ForgetLockState(hwnd);
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

                // Newer Windows versions do not raise EVENT_OBJECT_INVOKED for
                // system-menu items. Keep supporting environments that do, but
                // ignore the event when the low-level hook has already handled
                // the same click / keystroke.
                if (hwnd == _lastInputOwner && command == _lastInputCommand &&
                    DateTime.UtcNow - _lastInputAt < TimeSpan.FromSeconds(2))
                    return;

                bool changed;
                if (command == state.PositionCommand)
                    changed = TogglePosition(state);
                else if (command == state.SizeCommand)
                    changed = ToggleSize(state);
                else return;

                PublishLockState(state);
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

        /// <summary>
        /// Remembers which of our items is focused in an open system menu so a
        /// subsequent Enter or Space can toggle it. Mouse clicks are handled by
        /// the low-level hook, not by this method.
        /// </summary>
        private void TrackMenuFocus(IntPtr popup, int childId, uint eventThread)
        {
            if (popup == IntPtr.Zero || childId <= 0) return;
            if (Native.GetWindowClass(popup) != "#32768") return;

            Native.GUITHREADINFO info = new Native.GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO));
            uint threadId = eventThread;
            if (threadId == 0)
            {
                uint processId;
                threadId = Native.GetWindowThreadProcessId(popup, out processId);
            }
            if (threadId == 0 || !Native.GetGUIThreadInfo(threadId, ref info)) return;
            if ((info.flags & Native.GUI_INMENUMODE) == 0) return;

            IntPtr owner = info.hwndMenuOwner;
            if (owner == IntPtr.Zero) owner = Native.GetWindow(popup, Native.GW_OWNER);
            WindowState state;
            if (owner == IntPtr.Zero || !_windows.TryGetValue(owner, out state) ||
                !IsSameWindow(state))
                return;

            IntPtr menu = Native.GetSystemMenu(owner, false);
            int count = menu == IntPtr.Zero ? 0 : Native.GetMenuItemCount(menu);
            int index = childId - 1;
            if (index < 0 || index >= count) return;

            uint command = Native.GetMenuItemID(menu, index);
            if (command != state.PositionCommand && command != state.SizeCommand)
                return;
            _keyboardOwner = owner;
            _keyboardCommand = command;
            _keyboardValid = true;
        }

        private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_disposed)
            {
                try
                {
                    int message = wParam.ToInt32();
                    if (message == Native.WM_LBUTTONDOWN || message == Native.WM_LBUTTONUP)
                    {
                        Native.MSLLHOOKSTRUCT data = (Native.MSLLHOOKSTRUCT)
                            Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                        WindowState state;
                        uint command;
                        bool hit = TryHitOwnedMenuItem(data.pt, out state, out command);
                        if (message == Native.WM_LBUTTONDOWN)
                        {
                            _pressedValid = hit;
                            _pressedOwner = hit ? state.Handle : IntPtr.Zero;
                            _pressedCommand = command;
                        }
                        else if (_pressedValid)
                        {
                            if (hit && state.Handle == _pressedOwner &&
                                command == _pressedCommand)
                                QueueToggle(state, command, "mouse");
                            _pressedValid = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Window bounds lock mouse hook failed: " + ex.Message);
                }
            }
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_disposed)
            {
                try
                {
                    int message = wParam.ToInt32();
                    if (message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN)
                    {
                        Native.KBDLLHOOKSTRUCT data = (Native.KBDLLHOOKSTRUCT)
                            Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                        if (data.vkCode == Native.VK_RETURN || data.vkCode == Native.VK_SPACE)
                        {
                            if (_keyboardValid)
                            {
                                WindowState state;
                                if (_windows.TryGetValue(_keyboardOwner, out state) &&
                                    IsSameWindow(state) && _keyboardCommand != 0)
                                    QueueToggle(state, _keyboardCommand, "keyboard");
                                _keyboardValid = false;
                            }
                        }
                        else if (data.vkCode == Native.VK_ESCAPE)
                        {
                            _keyboardValid = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Window bounds lock keyboard hook failed: " + ex.Message);
                }
            }
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        /// <summary>
        /// Finds our item under the pointer in a currently open system menu.
        /// GetMenuItemRect gives the screen rectangle of the real popup, so a
        /// click can be matched without injecting code into the target app.
        /// </summary>
        private bool TryHitOwnedMenuItem(Native.POINT point,
            out WindowState state, out uint command)
        {
            state = null;
            command = 0;
            IntPtr popup = Native.WindowFromPoint(point);
            if (popup == IntPtr.Zero) return false;

            IntPtr owner = IntPtr.Zero;
            uint processId;
            uint threadId = Native.GetWindowThreadProcessId(popup, out processId);
            Native.GUITHREADINFO info = new Native.GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO));
            if (threadId != 0 && Native.GetGUIThreadInfo(threadId, ref info))
            {
                if ((info.flags & Native.GUI_INMENUMODE) == 0) return false;
                owner = info.hwndMenuOwner;
            }
            if (owner == IntPtr.Zero) owner = Native.GetWindow(popup, Native.GW_OWNER);
            if (owner == IntPtr.Zero) return false;

            WindowState candidate;
            if (!_windows.TryGetValue(owner, out candidate) ||
                !IsSameWindow(candidate) || Native.IsIconic(owner))
                return false;

            IntPtr menu = Native.GetSystemMenu(owner, false);
            if (menu == IntPtr.Zero) return false;

            Native.RECT rect;
            int position = FindMenuPosition(menu, candidate.PositionCommand);
            if (position >= 0 &&
                Native.GetMenuItemRect(owner, menu, (uint)position, out rect) &&
                Contains(rect, point))
            {
                state = candidate;
                command = candidate.PositionCommand;
                return true;
            }
            int size = FindMenuPosition(menu, candidate.SizeCommand);
            if (size >= 0 &&
                Native.GetMenuItemRect(owner, menu, (uint)size, out rect) &&
                Contains(rect, point))
            {
                state = candidate;
                command = candidate.SizeCommand;
                return true;
            }
            return false;
        }

        /// <summary>GetMenuItemRect wants a zero-based position, not a command id.</summary>
        private static int FindMenuPosition(IntPtr menu, uint command)
        {
            if (command == 0) return -1;
            int count = Native.GetMenuItemCount(menu);
            for (int i = 0; i < count; i++)
                if (Native.GetMenuItemID(menu, i) == command)
                    return i;
            return -1;
        }

        private static bool Contains(Native.RECT rect, Native.POINT point)
        {
            return point.X >= rect.Left && point.X < rect.Right &&
                   point.Y >= rect.Top && point.Y < rect.Bottom;
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

            if (!Native.IsWindowVisible(hwnd))
                return null;

            string className = Native.GetWindowClass(hwnd);
            if (!IsTrackableWindowClass(className))
                return null;

            // A window without WS_SYSMENU has no system menu to extend. Skip
            // it instead of hammering an invalid menu handle once a second.
            if ((Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE) &
                    Native.WS_SYSMENU) == 0)
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
            PublishLockState(state);
            EnsureSystemMenu(state);
            return state;
        }

        /// <summary>
        /// Copies the lock flags for the engine thread. Called on the UI
        /// thread whenever a lock or a tracked window changes.
        /// </summary>
        private void PublishLockState(WindowState state)
        {
            lock (_stateLock)
            {
                LockSnapshot snapshot;
                if (!_lockStates.TryGetValue(state.Handle, out snapshot) ||
                    snapshot.ProcessId != state.ProcessId ||
                    snapshot.ThreadId != state.ThreadId)
                {
                    snapshot = new LockSnapshot();
                    snapshot.ProcessId = state.ProcessId;
                    snapshot.ThreadId = state.ThreadId;
                    _lockStates[state.Handle] = snapshot;
                }
                snapshot.PositionLocked = state.PositionLocked;
                snapshot.SizeLocked = state.SizeLocked;
            }
        }

        private void ForgetLockState(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            lock (_stateLock)
                _lockStates.Remove(hwnd);
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
            if (hwnd == IntPtr.Zero) return false;

            uint processId = 0;
            LockSnapshot snapshot;
            lock (_stateLock)
            {
                if (!_lockStates.TryGetValue(hwnd, out snapshot)) return false;
                positionLocked = snapshot.PositionLocked;
                sizeLocked = snapshot.SizeLocked;
                processId = snapshot.ProcessId;
                if (snapshot.ThreadId == 0) return false;
                uint actualProcess;
                uint actualThread = Native.GetWindowThreadProcessId(hwnd, out actualProcess);
                if (!Native.IsWindow(hwnd) || actualThread != snapshot.ThreadId ||
                    actualProcess != processId)
                {
                    positionLocked = false;
                    sizeLocked = false;
                    return false;
                }
            }
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
            PublishLockState(state);
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

            // A maximized window's coordinates belong to the system. Locking
            // them would move the restored window to the maximized frame's
            // corner; enforcing them while maximized would push most of the
            // window off the screen.
            if (Native.IsZoomed(state.Handle))
            {
                Log.Info("Position lock skipped for " + state.Handle.ToInt64() +
                    ": the window is maximized");
                return false;
            }

            Native.RECT bounds;
            if (!Native.GetWindowRect(state.Handle, out bounds))
            {
                Log.Debug("Could not capture window position for " + state.Handle.ToInt64());
                return false;
            }

            // The jelly effect parks a window off-screen while it shows a
            // frozen copy. Locking at that moment used to remember -30000 and
            // send the real window there for good when the effect ended.
            if (!IsReachableBounds(bounds))
            {
                Log.Info("Position lock skipped for " + state.Handle.ToInt64() +
                    ": the window is off-screen at " + bounds.Left + "," + bounds.Top);
                return false;
            }

            state.PositionBounds = bounds;
            state.PositionLocked = true;
            Log.Debug("Position lock captured for " + state.Handle.ToInt64() + " at " +
                bounds.Left + "," + bounds.Top);
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

        /// <summary>
        /// True when enough of the rectangle sits on a monitor for the user to
        /// see and grab it. A window parked at -30000 is not reachable, and a
        /// position lock must never remember such coordinates.
        /// </summary>
        internal static bool IsReachableBounds(Native.RECT bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;
            IntPtr monitor = Native.MonitorFromRect(ref bounds, Native.MONITOR_DEFAULTTONULL);
            if (monitor == IntPtr.Zero) return false;

            Native.MONITORINFO info = new Native.MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            if (!Native.GetMonitorInfo(monitor, ref info)) return true;

            int left = Math.Max(bounds.Left, info.rcMonitor.Left);
            int top = Math.Max(bounds.Top, info.rcMonitor.Top);
            int right = Math.Min(bounds.Right, info.rcMonitor.Right);
            int bottom = Math.Min(bounds.Bottom, info.rcMonitor.Bottom);
            return right - left >= 32 && bottom - top >= 16;
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

        private void EnforceBounds(WindowState state)
        {
            if (!state.PositionLocked && !state.SizeLocked) return;
            if (!IsSameWindow(state) || Native.IsIconic(state.Handle)) return;

            // A maximized window's frame is owned by the system: its left/top
            // are off-screen shadow coordinates, and forcing the captured
            // position onto it pushes the window out of sight. Enforce again
            // once the user restores it.
            if (Native.IsZoomed(state.Handle)) return;

            // Monitors can be disconnected after the lock was captured. Never
            // keep sending a window to coordinates that no longer exist.
            if (state.PositionLocked && !IsReachableBounds(state.PositionBounds))
            {
                Log.Info("Position lock released for " + state.Handle.ToInt64() +
                    ": stored position is off-screen at " +
                    state.PositionBounds.Left + "," + state.PositionBounds.Top);
                state.PositionLocked = false;
                PublishLockState(state);
                IntPtr menu = Native.GetSystemMenu(state.Handle, false);
                if (menu != IntPtr.Zero) UpdateMenuChecks(state, menu);
            }
            if (!state.PositionLocked && !state.SizeLocked) return;

            Native.RECT current;
            if (!Native.GetWindowRect(state.Handle, out current)) return;

            Native.RECT desired = GetLockedBounds(current, state.PositionBounds,
                state.SizeBounds, state.PositionLocked, state.SizeLocked);
            bool positionChanged = desired.Left != current.Left || desired.Top != current.Top;
            bool sizeChanged = desired.Width != current.Width || desired.Height != current.Height;
            if (!positionChanged && !sizeChanged) return;

            // The user is dragging an axis that is fixed. Cancel the system's
            // modal move/size loop before restoring: without this, the window
            // follows the mouse until every location-change event catches it,
            // which reads as a rubber band being pulled.
            if ((state.PositionLocked && positionChanged) ||
                (state.SizeLocked && sizeChanged))
                CancelUserGesture(state);

            // ASYNCWINDOWPOS matters: a synchronous SetWindowPos into another
            // app's window can block this thread (and deadlock with that app
            // resizing itself at the same moment). Queueing the move keeps the
            // tray and engine responsive.
            uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE |
                Native.SWP_ASYNCWINDOWPOS;
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

        /// <summary>
        /// Ends a native move/size loop owned by the target's UI thread.
        /// WM_CANCELMODE releases its mouse capture, so the window does not
        /// keep following the pointer while the lock restores its bounds.
        /// </summary>
        private static void CancelUserGesture(WindowState state)
        {
            DateTime now = DateTime.UtcNow;
            if (now - state.LastCancelAt < TimeSpan.FromMilliseconds(250)) return;

            Native.GUITHREADINFO info = new Native.GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO));
            if (!Native.GetGUIThreadInfo(state.ThreadId, ref info)) return;
            if ((info.flags & Native.GUI_INMOVESIZE) == 0) return;
            if (info.hwndMoveSize != IntPtr.Zero && info.hwndMoveSize != state.Handle) return;

            state.LastCancelAt = now;
            if (Native.PostMessage(state.Handle, Native.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero))
                Log.Debug("Cancelled locked move/size gesture for " + state.Handle.ToInt64());
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
            if (_focusHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_focusHook);
                _focusHook = IntPtr.Zero;
            }
            if (_menuHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_menuHook);
                _menuHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_keyboardHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
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
            lock (_stateLock)
                _lockStates.Clear();
        }
    }
}
