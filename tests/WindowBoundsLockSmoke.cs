using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Automation;

namespace Okna.Tests
{
    internal static class WindowBoundsLockSmoke
    {
        private const uint MenuByPosition = 0x0400;
        private const uint MenuChecked = 0x0008;
        private const uint WindowClose = 0x0010;
        private const uint WmSysCommand = 0x0112;
        private const int ScKeyMenu = 0xF100;
        private const uint SetWindowFlags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;
        private const int CreateThirdWindowMessage = Native.WM_APP + 80;
        private static Native.WinEventProc _eventObserver;
        private static IntPtr _eventObserverHook;
        private static TrayApp _tray;
        private static JellyEngine _engine;
        private static Icon _icon;
        private static bool _actualPopupBlocked;

        private sealed class MenuCommands
        {
            public IntPtr Menu;
            public uint PositionId;
            public uint SizeId;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--target")
                return RunTarget(args);

            if (!Environment.UserInteractive)
            {
                Console.Error.WriteLine("The window lock smoke test requires an interactive desktop.");
                return 2;
            }

            WindowBoundsLockManager manager = null;
            Process target = null;
            IntPtr[] windows = null;
            string handlesFile = Path.Combine(Path.GetTempPath(),
                "OknaWindowLockSmoke-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                EnsureNoCompetingOknaProcess();
                target = StartTarget(handlesFile);
                windows = WaitForTargetWindows(handlesFile);

                manager = new WindowBoundsLockManager();
                manager.Start();
                EnsureNoCompetingOknaProcess();
                IntPtr newlyOpened = CreateThirdWindowAfterManagerStarts(
                    windows[0], handlesFile + ".third");
                Native.NotifyWinEvent(Native.EVENT_SYSTEM_FOREGROUND, newlyOpened,
                    Native.OBJID_WINDOW, Native.CHILDID_SELF);
                Pump(250);
                bool newWindowPosition;
                bool newWindowSize;
                Require(manager.LastActiveWindow == newlyOpened &&
                    manager.TryGetLockState(newlyOpened, out newWindowPosition, out newWindowSize),
                    "A newly foreground external window was not immediately tracked");
                MenuCommands newWindowMenu = FindMenuCommands(newlyOpened);
                Require(newWindowMenu.PositionId != 0 && newWindowMenu.SizeId != 0,
                    "A newly foreground external window did not receive system-menu entries");

                AppSettings settings = new AppSettings();
                settings.AutoUpdate = false;
                _engine = new JellyEngine(settings);
                _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (_icon == null) _icon = (Icon)SystemIcons.Application.Clone();
                _tray = new TrayApp(settings, _engine, _icon, false);
                _tray.SmokeMode = true;
                _tray.BoundsLocks = manager;
                _tray.UpdateMenu();

                _eventObserver = delegate(IntPtr hook, uint eventType, IntPtr hwnd,
                    int objectId, int childId, uint eventThread, uint eventTime)
                {
                    if (hwnd == windows[0] && eventType == Native.EVENT_OBJECT_INVOKED)
                        Console.WriteLine("Invoke event object=" + objectId + " child=" + childId);
                };
                _eventObserverHook = Native.SetWinEventHook(Native.EVENT_OBJECT_INVOKED,
                    Native.EVENT_OBJECT_INVOKED, IntPtr.Zero, _eventObserver, 0, 0,
                    Native.WINEVENT_OUTOFCONTEXT);
                if (_eventObserverHook == IntPtr.Zero)
                    Console.WriteLine("Observer hook unavailable; error=" +
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                Pump(1300);
                EnsureNoCompetingOknaProcess();

                MenuCommands commands = FindMenuCommands(windows[0]);
                Native.RECT original = GetRect(windows[0]);
                Native.RECT secondOriginal = GetRect(windows[1]);

                if (!InvokeActualMenuCommand(windows[0], "Зафиксировать положение", "Lock position"))
                    InvokeMenuCommand(windows[0], commands.PositionId);
                CheckMenuState(manager, windows[0], commands, commands.PositionId, true, "position lock");

                int requestedWidth = original.Width + 70;
                int requestedHeight = original.Height + 60;
                Native.SetWindowPos(windows[0], IntPtr.Zero,
                    original.Left + 120, original.Top + 80,
                    requestedWidth, requestedHeight, SetWindowFlags);

                Native.RECT positionResult = WaitForRect(windows[0], delegate(Native.RECT r)
                {
                    return r.Left == original.Left && r.Top == original.Top &&
                           r.Width == requestedWidth && r.Height == requestedHeight;
                }, "Position lock did not restore position while allowing size changes");

                Native.SetWindowPos(windows[1], IntPtr.Zero,
                    secondOriginal.Left + 80, secondOriginal.Top + 60,
                    secondOriginal.Width + 30, secondOriginal.Height + 20,
                    SetWindowFlags);
                Pump(300);
                Native.RECT secondResult = GetRect(windows[1]);
                Require(secondResult.Left == secondOriginal.Left + 80 &&
                        secondResult.Top == secondOriginal.Top + 60 &&
                        secondResult.Width == secondOriginal.Width + 30 &&
                        secondResult.Height == secondOriginal.Height + 20,
                    "A lock on one window changed a separate window");

                if (!InvokeActualMenuCommand(windows[0], "Зафиксировать положение", "Lock position"))
                    InvokeMenuCommand(windows[0], commands.PositionId);
                CheckMenuState(manager, windows[0], commands, commands.PositionId, false, "position unlock");

                Native.RECT sizeBaseline = GetRect(windows[0]);
                if (!InvokeActualMenuCommand(windows[0], "Зафиксировать размер", "Lock size"))
                    InvokeMenuCommand(windows[0], commands.SizeId);
                CheckMenuState(manager, windows[0], commands, commands.SizeId, true, "size lock");

                int movedX = sizeBaseline.Left + 100;
                int movedY = sizeBaseline.Top + 90;
                Native.SetWindowPos(windows[0], IntPtr.Zero,
                    movedX, movedY, sizeBaseline.Width + 50, sizeBaseline.Height + 40,
                    SetWindowFlags);
                Native.RECT sizeResult = WaitForRect(windows[0], delegate(Native.RECT r)
                {
                    return r.Left == movedX && r.Top == movedY &&
                           r.Width == sizeBaseline.Width && r.Height == sizeBaseline.Height;
                }, "Size lock did not restore size while allowing position changes");

                if (!InvokeActualMenuCommand(windows[0], "Зафиксировать размер", "Lock size"))
                    InvokeMenuCommand(windows[0], commands.SizeId);
                CheckMenuState(manager, windows[0], commands, commands.SizeId, false, "size unlock");

                IntPtr trayTarget = manager.LastActiveWindow;
                Require(trayTarget != IntPtr.Zero,
                    "No external window remained active for the tray lock test");
                IntPtr trayOther = trayTarget == windows[0] ? windows[1] : windows[0];
                Native.RECT trayOtherBefore = GetRect(trayOther);
                VerifyTrayActions(manager, _tray, trayTarget);
                Native.RECT trayOtherAfter = GetRect(trayOther);
                Require(trayOtherAfter.Left == trayOtherBefore.Left &&
                    trayOtherAfter.Top == trayOtherBefore.Top &&
                    trayOtherAfter.Width == trayOtherBefore.Width &&
                    trayOtherAfter.Height == trayOtherBefore.Height,
                    "Tray locks changed the other external window");

                Console.WriteLine("PASS: system-menu invocation callbacks toggle both locks; tray " +
                    "items route to the remembered active external window; position and size " +
                    "restore independently; a second window remains unlocked.");
                Console.WriteLine("Position-only result: " + Format(positionResult));
                Console.WriteLine("Size-only result: " + Format(sizeResult));
                Console.WriteLine("Independent window result: " + Format(secondResult));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
            finally
            {
                if (manager != null)
                {
                    try { manager.Dispose(); }
                    catch (Exception ex) { Console.Error.WriteLine("Manager cleanup failed: " + ex.Message); }
                }
                if (_tray != null)
                {
                    try { _tray.DisposeForSmoke(); }
                    catch (Exception ex) { Console.Error.WriteLine("Tray cleanup failed: " + ex.Message); }
                    _tray = null;
                }
                if (_engine != null)
                {
                    try { _engine.Dispose(); } catch { }
                    _engine = null;
                }
                if (_icon != null)
                {
                    try { _icon.Dispose(); } catch { }
                    _icon = null;
                }
                if (_eventObserverHook != IntPtr.Zero)
                {
                    Native.UnhookWinEvent(_eventObserverHook);
                    _eventObserverHook = IntPtr.Zero;
                }

                if (target != null && !target.HasExited)
                {
                    try
                    {
                        if (windows != null)
                            for (int i = 0; i < windows.Length; i++)
                                Native.PostMessage(windows[i], WindowClose, IntPtr.Zero, IntPtr.Zero);
                        if (!target.WaitForExit(5000))
                            Console.Error.WriteLine("Test-window process did not close after WM_CLOSE.");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Test-window cleanup failed: " + ex.Message);
                    }
                }

                try { if (File.Exists(handlesFile)) File.Delete(handlesFile); }
                catch { }
                try { if (File.Exists(handlesFile + ".third")) File.Delete(handlesFile + ".third"); }
                catch { }
            }
        }

        private static int RunTarget(string[] args)
        {
            if (args.Length != 2) return 2;

            Form first = new TargetForm("Okna bounds smoke A", 180, 160);
            Form second = new TargetForm("Okna bounds smoke B", 850, 160);
            ((TargetForm)first).CreateThirdWindow = delegate
            {
                Form third = CreateTestForm("Okna bounds smoke C", 520, 620);
                third.Show();
                File.WriteAllText(args[1] + ".third",
                    third.Handle.ToInt64().ToString(CultureInfo.InvariantCulture));
            };
            first.Show();
            second.Show();

            string handles = first.Handle.ToInt64().ToString(CultureInfo.InvariantCulture) + "," +
                second.Handle.ToInt64().ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(args[1], handles);
            Application.Run(first);
            second.Dispose();
            return 0;
        }

        private sealed class TargetForm : Form
        {
            public Action CreateThirdWindow;

            public TargetForm(string title, int x, int y)
            {
                Text = title;
                StartPosition = FormStartPosition.Manual;
                Location = new System.Drawing.Point(x, y);
                Size = new System.Drawing.Size(560, 420);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == CreateThirdWindowMessage && CreateThirdWindow != null)
                {
                    Action create = CreateThirdWindow;
                    CreateThirdWindow = null;
                    create();
                }
                base.WndProc(ref message);
            }
        }

        private static Form CreateTestForm(string title, int x, int y)
        {
            return new Form
            {
                Text = title,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(560, 420)
            };
        }

        private static Process StartTarget(string handlesFile)
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = "--target \"" + handlesFile + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("Could not start test-window process.");
            return process;
        }

        private static void EnsureNoCompetingOknaProcess()
        {
            Process[] processes = Process.GetProcessesByName("Okna");
            try
            {
                if (processes.Length != 0)
                    throw new InvalidOperationException("Close the running Okna application before this smoke test; " +
                        "its global menu hook can otherwise duplicate system-menu items.");
            }
            finally
            {
                for (int i = 0; i < processes.Length; i++) processes[i].Dispose();
            }
        }

        private static IntPtr[] WaitForTargetWindows(string handlesFile)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until)
            {
                if (File.Exists(handlesFile))
                {
                    string[] values = File.ReadAllText(handlesFile).Split(',');
                    if (values.Length == 2)
                    {
                        IntPtr first = new IntPtr(long.Parse(values[0], CultureInfo.InvariantCulture));
                        IntPtr second = new IntPtr(long.Parse(values[1], CultureInfo.InvariantCulture));
                        if (Native.IsWindow(first) && Native.IsWindow(second))
                            return new IntPtr[] { first, second };
                    }
                }
                Pump(50);
            }
            throw new TimeoutException("Test windows did not start in time.");
        }

        private static IntPtr CreateThirdWindowAfterManagerStarts(IntPtr target,
            string handleFile)
        {
            if (!Native.PostMessage(target, CreateThirdWindowMessage, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("Could not request the post-start external window.");
            DateTime until = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < until)
            {
                if (File.Exists(handleFile))
                {
                    IntPtr hwnd = new IntPtr(long.Parse(File.ReadAllText(handleFile),
                        CultureInfo.InvariantCulture));
                    if (Native.IsWindow(hwnd)) return hwnd;
                }
                Pump(50);
            }
            throw new TimeoutException("The external window opened after manager startup was not created.");
        }

        private static MenuCommands FindMenuCommands(IntPtr hwnd)
        {
            IntPtr menu = Native.GetSystemMenu(hwnd, false);
            Require(menu != IntPtr.Zero, "The test window has no system menu");

            MenuCommands result = new MenuCommands();
            result.Menu = menu;
            int positionCount = 0;
            int sizeCount = 0;
            int itemCount = Native.GetMenuItemCount(menu);
            Console.WriteLine("System menu item count: " + itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                System.Text.StringBuilder textBuffer = new System.Text.StringBuilder(256);
                int textLength = Native.GetMenuString(menu, (uint)i, textBuffer,
                    textBuffer.Capacity, MenuByPosition);
                string text = textLength > 0 ? textBuffer.ToString() : string.Empty;
                Console.WriteLine("System menu[" + i + "]: " + text);
                if (text == "Зафиксировать положение" || text == "Lock position")
                {
                    result.PositionId = Native.GetMenuItemID(menu, i);
                    positionCount++;
                }
                if (text == "Зафиксировать размер" || text == "Lock size")
                {
                    result.SizeId = Native.GetMenuItemID(menu, i);
                    sizeCount++;
                }
            }

            Require(positionCount == 1 && sizeCount == 1,
                "Expected one position item and one size item in the external system menu");
            return result;
        }

        private static bool InvokeActualMenuCommand(IntPtr hwnd,
            string russianName, string englishName)
        {
            bool foreground = Native.SetForegroundWindow(hwnd);
            Console.WriteLine("SetForegroundWindow result: " + foreground +
                "; foreground HWND=" + Native.GetForegroundWindow().ToInt64());
            Thread popupThread = new Thread(delegate()
            {
                Native.SendMessage(hwnd, WmSysCommand,
                    new IntPtr(ScKeyMenu), new IntPtr(Native.VK_SPACE));
            });
            popupThread.IsBackground = true;
            popupThread.Start();

            AutomationElement item = null;
            DateTime until = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < until && item == null)
            {
                try
                {
                    AutomationElement owner = AutomationElement.FromHandle(hwnd);
                    item = owner.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, russianName));
                    if (item == null)
                        item = owner.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.NameProperty, englishName));
                    item = AutomationElement.RootElement.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, russianName)) ?? item;
                    if (item == null)
                        item = AutomationElement.RootElement.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.NameProperty, englishName));
                }
                catch (ElementNotAvailableException) { }
                if (item == null) Pump(50);
            }

            if (item == null)
            {
                Native.PostMessage(hwnd, Native.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
                popupThread.Join(500);
                if (!_actualPopupBlocked)
                {
                    Console.WriteLine("BLOCKED: UI Automation could not access the actual system-menu popup; " +
                        "verifying its invocation callback directly and tray actions end-to-end.");
                    _actualPopupBlocked = true;
                }
                return false;
            }

            object pattern;
            if (item.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                ((InvokePattern)pattern).Invoke();
            else if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                ((SelectionItemPattern)pattern).Select();
            else
                throw new InvalidOperationException("The system-menu item has no invoke/select pattern.");
            Pump(250);
            popupThread.Join(1000);
            Console.WriteLine("PASS: UI Automation invoked actual system-menu item " + russianName);
            return true;
        }

        private static void InvokeMenuCommand(IntPtr hwnd, uint commandId)
        {
            Native.NotifyWinEvent(Native.EVENT_OBJECT_INVOKED, hwnd,
                Native.OBJID_MENU, unchecked((int)commandId));
            Pump(250);
        }

        private static void VerifyTrayActions(WindowBoundsLockManager manager,
            TrayApp tray, IntPtr hwnd)
        {
            Require(manager.RememberActiveWindow(hwnd),
                "The tray test target was not tracked as an active external window");
            tray.UpdateMenu();

            Native.RECT baseline = GetRect(hwnd);
            tray.PerformBoundsLockItemForSmoke(true);
            CheckActiveLock(manager, tray, hwnd, true, false, "tray position lock");
            Native.SetWindowPos(hwnd, IntPtr.Zero,
                baseline.Left + 120, baseline.Top + 85,
                baseline.Width + 35, baseline.Height + 25, SetWindowFlags);
            WaitForRect(hwnd, delegate(Native.RECT r)
            {
                return r.Left == baseline.Left && r.Top == baseline.Top &&
                       r.Width == baseline.Width + 35 && r.Height == baseline.Height + 25;
            }, "Tray position lock did not restore position independently");

            tray.PerformBoundsLockItemForSmoke(false);
            CheckActiveLock(manager, tray, hwnd, true, true, "tray size lock");
            Native.RECT locked = GetRect(hwnd);
            Native.SetWindowPos(hwnd, IntPtr.Zero,
                locked.Left + 90, locked.Top + 75,
                locked.Width + 45, locked.Height + 30, SetWindowFlags);
            WaitForRect(hwnd, delegate(Native.RECT r)
            {
                return r.Left == baseline.Left && r.Top == baseline.Top &&
                       r.Width == locked.Width && r.Height == locked.Height;
            }, "Combined tray locks did not restore the captured bounds");

            tray.PerformBoundsLockItemForSmoke(true);
            CheckActiveLock(manager, tray, hwnd, false, true, "tray position unlock");
            Native.RECT sizeOnlyBaseline = GetRect(hwnd);
            Native.SetWindowPos(hwnd, IntPtr.Zero,
                sizeOnlyBaseline.Left + 110, sizeOnlyBaseline.Top + 95,
                sizeOnlyBaseline.Width + 50, sizeOnlyBaseline.Height + 35, SetWindowFlags);
            WaitForRect(hwnd, delegate(Native.RECT r)
            {
                return r.Left == sizeOnlyBaseline.Left + 110 &&
                       r.Top == sizeOnlyBaseline.Top + 95 &&
                       r.Width == sizeOnlyBaseline.Width &&
                       r.Height == sizeOnlyBaseline.Height;
            }, "Tray size lock did not preserve the position/size independence");

            tray.PerformBoundsLockItemForSmoke(false);
            CheckActiveLock(manager, tray, hwnd, false, false, "tray size unlock");
            Native.RECT unlocked = GetRect(hwnd);
            Native.SetWindowPos(hwnd, IntPtr.Zero,
                unlocked.Left + 70, unlocked.Top + 65,
                unlocked.Width + 25, unlocked.Height + 20, SetWindowFlags);
            WaitForRect(hwnd, delegate(Native.RECT r)
            {
                return r.Left == unlocked.Left + 70 && r.Top == unlocked.Top + 65 &&
                       r.Width == unlocked.Width + 25 && r.Height == unlocked.Height + 20;
            }, "Bounds remained constrained after tray locks were removed");
            Console.WriteLine("Tray-driven bounds recovery: position lock, size lock, " +
                "combined locks, independent unlock, and free bounds all passed.");
        }

        private static void CheckActiveLock(WindowBoundsLockManager manager, TrayApp tray,
            IntPtr hwnd, bool position, bool size, string name)
        {
            bool actualPosition;
            bool actualSize;
            bool found = manager.TryGetLockState(hwnd, out actualPosition, out actualSize);
            Require(found && actualPosition == position && actualSize == size,
                name + " manager state did not match (found=" + found +
                ", actual=" + actualPosition + "/" + actualSize +
                ", expected=" + position + "/" + size +
                ", active=" + manager.LastActiveWindow.ToInt64() +
                ", target=" + hwnd.ToInt64() + ")");
            tray.UpdateMenu();
            Require(tray.IsBoundsLockItemCheckedForSmoke(true) == position &&
                tray.IsBoundsLockItemCheckedForSmoke(false) == size,
                name + " tray checkmarks did not match");
        }

        private static void CheckMenuState(WindowBoundsLockManager manager, IntPtr hwnd,
            MenuCommands commands, uint itemId, bool expected, string description)
        {
            uint state = Native.GetMenuState(commands.Menu, itemId, Native.MF_BYCOMMAND);
            bool isChecked = (state & MenuChecked) != 0;
            bool position;
            bool size;
            manager.TryGetLockState(hwnd, out position, out size);
            Console.WriteLine(description + ": menu checked=" + isChecked +
                "; manager position/size=" + position + "/" + size);
            Require(isChecked == expected, description + " checked state was " + isChecked);
        }

        private static Native.RECT GetRect(IntPtr hwnd)
        {
            Native.RECT rect;
            if (!Native.GetWindowRect(hwnd, out rect))
                throw new InvalidOperationException("GetWindowRect failed: " +
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return rect;
        }

        private static Native.RECT WaitForRect(IntPtr hwnd,
            Func<Native.RECT, bool> predicate, string failure)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < until)
            {
                Pump(40);
                Native.RECT rect = GetRect(hwnd);
                if (predicate(rect)) return rect;
            }
            throw new TimeoutException(failure + "; actual bounds: " + Format(GetRect(hwnd)));
        }

        private static void Pump(int milliseconds)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        private static string Format(Native.RECT rect)
        {
            return rect.Left + "," + rect.Top + " " + rect.Width + "x" + rect.Height;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
