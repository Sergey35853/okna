using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Okna.Tests
{
    internal static class WindowBoundsLockSmoke
    {
        private const uint MenuByPosition = 0x0400;
        private const uint MenuChecked = 0x0008;
        private const uint WindowClose = 0x0010;
        private const uint SetWindowFlags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;
        private static Native.WinEventProc _eventObserver;
        private static IntPtr _eventObserverHook;

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
                target = StartTarget(handlesFile);
                windows = WaitForTargetWindows(handlesFile);

                manager = new WindowBoundsLockManager();
                manager.Start();
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

                MenuCommands commands = FindMenuCommands(windows[0]);
                Native.RECT original = GetRect(windows[0]);
                Native.RECT secondOriginal = GetRect(windows[1]);

                InvokeMenuCommand(windows[0], commands.PositionId);
                CheckMenuState(commands, commands.PositionId, true, "position lock");

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

                InvokeMenuCommand(windows[0], commands.PositionId);
                CheckMenuState(commands, commands.PositionId, false, "position unlock");

                Native.RECT sizeBaseline = GetRect(windows[0]);
                InvokeMenuCommand(windows[0], commands.SizeId);
                CheckMenuState(commands, commands.SizeId, true, "size lock");

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

                InvokeMenuCommand(windows[0], commands.SizeId);
                CheckMenuState(commands, commands.SizeId, false, "size unlock");

                Console.WriteLine("PASS: system-menu items are unique; native invocation events " +
                    "toggle their checked states; position and size restore independently; " +
                    "a second window remains unlocked.");
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
            }
        }

        private static int RunTarget(string[] args)
        {
            if (args.Length != 2) return 2;

            Form first = CreateTestForm("Okna bounds smoke A", 180, 160);
            Form second = CreateTestForm("Okna bounds smoke B", 850, 160);
            first.Show();
            second.Show();

            string handles = first.Handle.ToInt64().ToString(CultureInfo.InvariantCulture) + "," +
                second.Handle.ToInt64().ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(args[1], handles);
            Application.Run(first);
            second.Dispose();
            return 0;
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

        private static MenuCommands FindMenuCommands(IntPtr hwnd)
        {
            IntPtr menu = Native.GetSystemMenu(hwnd, false);
            Require(menu != IntPtr.Zero, "The test window has no system menu");

            MenuCommands result = new MenuCommands();
            result.Menu = menu;
            int positionCount = 0;
            int sizeCount = 0;
            int itemCount = Native.GetMenuItemCount(menu);
            for (int i = 0; i < itemCount; i++)
            {
                System.Text.StringBuilder textBuffer = new System.Text.StringBuilder(256);
                int textLength = Native.GetMenuString(menu, (uint)i, textBuffer,
                    textBuffer.Capacity, MenuByPosition);
                string text = textLength > 0 ? textBuffer.ToString() : string.Empty;
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

        private static void InvokeMenuCommand(IntPtr hwnd, uint commandId)
        {
            Native.NotifyWinEvent(Native.EVENT_OBJECT_INVOKED, hwnd,
                Native.OBJID_MENU, unchecked((int)commandId));
            Pump(250);
        }

        private static void CheckMenuState(MenuCommands commands, uint itemId,
            bool expected, string description)
        {
            uint state = Native.GetMenuState(commands.Menu, itemId, Native.MF_BYCOMMAND);
            bool isChecked = (state & MenuChecked) != 0;
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
