using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace Okna
{
    /// <summary>
    /// Finds the taskbar (or taskbars) of the desktop. The tube of a
    /// minimized window has to dive into the bar of the monitor the window
    /// lives on, so every Shell_TrayWnd and Shell_SecondaryTrayWnd counts.
    /// </summary>
    internal static class TaskbarFinder
    {
        private static readonly object Sync = new object();
        private static Native.RECT[] _rects = new Native.RECT[0];
        private static DateTime _stamp = DateTime.MinValue;

        public static Native.RECT[] Rects()
        {
            lock (Sync)
            {
                if (_rects.Length > 0 && (DateTime.UtcNow - _stamp).TotalMilliseconds < 1000)
                    return _rects;

                List<Native.RECT> list = new List<Native.RECT>();
                Add(Native.FindWindow("Shell_TrayWnd", null), list);
                IntPtr after = IntPtr.Zero;
                for (int i = 0; i < 8; i++)
                {
                    IntPtr h = Native.FindWindowEx(IntPtr.Zero, after, "Shell_SecondaryTrayWnd", null);
                    if (h == IntPtr.Zero) break;
                    Add(h, list);
                    after = h;
                }

                _rects = list.ToArray();
                _stamp = DateTime.UtcNow;
                return _rects;
            }
        }

        private static void Add(IntPtr hwnd, List<Native.RECT> list)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd)) return;
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return;
            if (r.Width < 2 || r.Height < 2) return;
            list.Add(r);
        }

        public static bool Contains(Native.POINT point)
        {
            Native.RECT[] rects = Rects();
            for (int i = 0; i < rects.Length; i++)
            {
                Native.RECT r = rects[i];
                if (point.X >= r.Left && point.X < r.Right &&
                    point.Y >= r.Top && point.Y < r.Bottom) return true;
            }
            return false;
        }

        /// <summary>Picks the bar closest to the given window.</summary>
        public static bool TryGet(Native.RECT near, out Native.RECT best)
        {
            best = new Native.RECT();
            Native.RECT[] rects = Rects();
            if (rects.Length == 0) return false;

            double cx = (near.Left + near.Right) * 0.5;
            double cy = (near.Top + near.Bottom) * 0.5;
            double bestDistance = double.MaxValue;
            bool found = false;
            for (int i = 0; i < rects.Length; i++)
            {
                Native.RECT r = rects[i];
                double dx = Math.Max(0, Math.Max(r.Left - cx, cx - r.Right));
                double dy = Math.Max(0, Math.Max(r.Top - cy, cy - r.Bottom));
                double d = dx * dx + dy * dy;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = r;
                    found = true;
                }
            }
            return found;
        }
    }

    /// <summary>
    /// Finds the notification area (the tray) of the taskbar nearest to a
    /// window. A window that is closed into the tray instead of being
    /// destroyed is rolled into a tube and sent there.
    /// </summary>
    internal static class TrayFinder
    {
        private static readonly object Sync = new object();
        private static Native.RECT[] _rects = new Native.RECT[0];
        private static DateTime _stamp = DateTime.MinValue;

        public static Native.RECT[] Rects()
        {
            lock (Sync)
            {
                if (_rects.Length > 0 && (DateTime.UtcNow - _stamp).TotalMilliseconds < 1000)
                    return _rects;

                List<Native.RECT> list = new List<Native.RECT>();
                AddTray(Native.FindWindow("Shell_TrayWnd", null), list);
                IntPtr after = IntPtr.Zero;
                for (int i = 0; i < 8; i++)
                {
                    IntPtr h = Native.FindWindowEx(IntPtr.Zero, after, "Shell_SecondaryTrayWnd", null);
                    if (h == IntPtr.Zero) break;
                    AddTray(h, list);
                    after = h;
                }

                _rects = list.ToArray();
                _stamp = DateTime.UtcNow;
                return _rects;
            }
        }

        private static void AddTray(IntPtr bar, List<Native.RECT> list)
        {
            if (bar == IntPtr.Zero || !Native.IsWindowVisible(bar)) return;
            // TrayNotifyWnd holds the icons and the clock. Older shells may not
            // expose it, in which case the bar itself is close enough.
            IntPtr tray = Native.FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray == IntPtr.Zero) tray = bar;
            Native.RECT r;
            if (!Native.GetWindowRect(tray, out r)) return;
            if (r.Width < 8 || r.Height < 8) return;
            list.Add(r);
        }

        /// <summary>Picks the tray closest to the given window.</summary>
        public static bool TryGet(Native.RECT near, out Native.RECT best)
        {
            best = new Native.RECT();
            Native.RECT[] rects = Rects();
            if (rects.Length == 0) return false;

            double cx = (near.Left + near.Right) * 0.5;
            double cy = (near.Top + near.Bottom) * 0.5;
            double bestDistance = double.MaxValue;
            bool found = false;
            for (int i = 0; i < rects.Length; i++)
            {
                Native.RECT r = rects[i];
                double dx = Math.Max(0, Math.Max(r.Left - cx, cx - r.Right));
                double dy = Math.Max(0, Math.Max(r.Top - cy, cy - r.Bottom));
                double d = dx * dx + dy * dy;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = r;
                    found = true;
                }
            }
            return found;
        }
    }

    /// <summary>
    /// Locates the taskbar button of a window through UI Automation. On
    /// Windows 11 the taskbar is XAML, not a toolbar of child windows, so the
    /// old geometry guess (the bar under the window centre) often landed in
    /// empty space. Every taskbar button exposes an AutomationId of the form
    /// "Appid: &lt;AppUserModelID&gt;"; when the process has no AppUserModelID
    /// (most unpackaged programs) the executable name is matched against that
    /// id, then against the button's accessible name. When automation is
    /// unavailable (Windows 8.1, a locked shell) the caller falls back to the
    /// geometry guess.
    /// </summary>
    internal static class TaskbarButtonFinder
    {
        private static readonly object Sync = new object();
        private static IntPtr _cachedHwnd;
        private static string _cachedTitle;
        private static Native.RECT _cachedRect;
        private static DateTime _cachedAt = DateTime.MinValue;

        public static bool TryGet(IntPtr hwnd, out Native.RECT rect)
        {
            rect = new Native.RECT();
            if (hwnd == IntPtr.Zero) return false;
            string title = Native.GetWindowTitle(hwnd);
            string exe = Native.GetWindowProcessName(hwnd);
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(exe)) return false;

            lock (Sync)
            {
                if (_cachedHwnd == hwnd && string.Equals(_cachedTitle, title, StringComparison.Ordinal) &&
                    (DateTime.UtcNow - _cachedAt).TotalSeconds < 2.0)
                {
                    rect = _cachedRect;
                    return rect.Width > 2 && rect.Height > 2;
                }
            }

            Native.RECT found;
            if (!Find(title, exe, hwnd, out found) &&
                !FindWin32(title, exe, hwnd, out found) &&
                !FindWin32Children(title, exe, hwnd, out found)) return false;

            lock (Sync)
            {
                _cachedHwnd = hwnd;
                _cachedTitle = title;
                _cachedRect = found;
                _cachedAt = DateTime.UtcNow;
            }
            rect = found;
            return true;
        }

        private static bool Find(string title, string exe, IntPtr hwnd, out Native.RECT rect)
        {
            rect = new Native.RECT();
            try
            {
                AutomationElement root = AutomationElement.RootElement;
                if (root == null) return false;

                List<AutomationElement> bars = new List<AutomationElement>();
                AddBars(root, new PropertyCondition(
                    AutomationElement.ClassNameProperty, "Shell_TrayWnd"), bars);
                AddBars(root, new PropertyCondition(
                    AutomationElement.ClassNameProperty, "Shell_SecondaryTrayWnd"), bars);
                if (bars.Count == 0) return false;

                string appId = Native.GetWindowAppId(hwnd);
                string exeFile = string.IsNullOrEmpty(exe) ? null : exe;
                string exeBase = BaseName(exe);

                double cx = 0, cy = 0;
                Native.RECT frame;
                if (Native.GetWindowRect(hwnd, out frame))
                {
                    cx = (frame.Left + frame.Right) * 0.5;
                    cy = (frame.Top + frame.Bottom) * 0.5;
                }

                // On Windows 11 the taskbar is XAML: its buttons are not always
                // exposed as ControlType.Button, so accept several control
                // types and let Score pick the best match.
                Condition buttons = new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Custom),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group));
                int bestScore = 0;
                double bestDistance = double.MaxValue;
                Native.RECT best = new Native.RECT();

                for (int b = 0; b < bars.Count; b++)
                {
                    AutomationElementCollection all = bars[b].FindAll(
                        TreeScope.Descendants, buttons);
                    foreach (AutomationElement candidate in all)
                    {
                        AutomationElement.AutomationElementInformation info = candidate.Current;
                        int score = Score(title, exeBase, exeFile, appId, info.Name, info.AutomationId);
                        if (score <= 0) continue;

                        System.Windows.Rect r = info.BoundingRectangle;
                        if (r.IsEmpty || r.Width < 4 || r.Height < 4) continue;

                        double dx = Math.Max(0, Math.Max(r.Left - cx, cx - r.Right));
                        double dy = Math.Max(0, Math.Max(r.Top - cy, cy - r.Bottom));
                        double distance = dx * dx + dy * dy;
                        if (score < bestScore || (score == bestScore && distance >= bestDistance))
                            continue;

                        bestScore = score;
                        bestDistance = distance;
                        best.Left = (int)Math.Round(r.Left);
                        best.Top = (int)Math.Round(r.Top);
                        best.Right = (int)Math.Round(r.Right);
                        best.Bottom = (int)Math.Round(r.Bottom);
                    }
                }

                if (bestScore <= 0) return false;
                rect = best;
                Log.Debug("Taskbar button matched score=" + bestScore +
                    " appid=" + (appId ?? "-") + " exe=" + (exeFile ?? "-"));
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Taskbar button lookup failed: " + ex.GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// Win32 fallback for locating a window's taskbar button icon. Enumerates
        /// the taskbar toolbar buttons and matches by window title or executable
        /// name. Used when UI Automation cannot expose the taskbar buttons
        /// (Windows 10/8.1 shell, locked desktop, etc.).
        /// </summary>
        private static bool FindWin32(string title, string exe, IntPtr hwnd, out Native.RECT rect)
        {
            rect = new Native.RECT();
            string exeBase = BaseName(exe);
            IntPtr after = IntPtr.Zero;
            for (int barIndex = 0; barIndex < 8; barIndex++)
            {
                IntPtr bar = barIndex == 0
                    ? Native.FindWindow("Shell_TrayWnd", null)
                    : Native.FindWindowEx(IntPtr.Zero, after, "Shell_SecondaryTrayWnd", null);
                if (bar == IntPtr.Zero) break;
                after = bar;

                IntPtr toolbar = Native.FindWindowEx(bar, IntPtr.Zero, "ToolbarWindow32", null);
                if (toolbar == IntPtr.Zero) continue;

                int count = (int)Native.SendMessage(toolbar, Native.TB_BUTTONCOUNT,
                    IntPtr.Zero, IntPtr.Zero);
                if (count <= 0) continue;

                for (int i = 0; i < count; i++)
                {
                    var info = new Native.TBBUTTONINFO();
                    info.cbSize = Marshal.SizeOf(typeof(Native.TBBUTTONINFO));
                    info.dwMask = Native.TBIF_TEXT;
                    IntPtr textBuf = Marshal.AllocHGlobal(512);
                    try
                    {
                        info.iString = textBuf;
                        IntPtr infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(info));
                        try
                        {
                            Marshal.StructureToPtr(info, infoPtr, false);
                            Native.SendMessage(toolbar, Native.TB_GETBUTTONINFO,
                                new IntPtr(i), infoPtr);
                            info = (Native.TBBUTTONINFO)Marshal.PtrToStructure(infoPtr,
                                typeof(Native.TBBUTTONINFO));
                        }
                        finally { Marshal.FreeHGlobal(infoPtr); }

                        string btnText = Marshal.PtrToStringUni(textBuf);
                        if (string.IsNullOrEmpty(btnText)) continue;

                        int score = Score(title, exeBase, exe, null, btnText, null);
                        if (score <= 0) continue;

                        Native.RECT r;
                        if (!Native.GetWindowRect(toolbar, out r)) continue;
                        // Approximate the button rect: split the toolbar width
                        // evenly among buttons and clamp to the found button index.
                        int btnW = Math.Max(1, r.Width / count);
                        rect.Left = r.Left + i * btnW;
                        rect.Right = rect.Left + btnW;
                        rect.Top = r.Top;
                        rect.Bottom = r.Bottom;
                        Log.Debug("Taskbar button (Win32): index=" + i +
                            " text='" + btnText + "' -> " + rect.Left + "," + rect.Top);
                        return true;
                    }
                    catch
                    {
                        // Continue to the next button.
                    }
                    finally { Marshal.FreeHGlobal(textBuf); }
                }
            }
            return false;
        }

        /// <summary>
        /// Second Win32 fallback: enumerates every child window of the taskbar
        /// bar and matches by visible text or executable name. Used on
        /// Windows 11 where the taskbar is XAML and exposes neither
        /// ToolbarWindow32 nor reliable UI Automation buttons.
        /// </summary>
        private static bool FindWin32Children(string title, string exe, IntPtr hwnd, out Native.RECT rect)
        {
            rect = new Native.RECT();
            string exeBase = BaseName(exe);
            IntPtr after = IntPtr.Zero;
            for (int barIndex = 0; barIndex < 8; barIndex++)
            {
                IntPtr bar = barIndex == 0
                    ? Native.FindWindow("Shell_TrayWnd", null)
                    : Native.FindWindowEx(IntPtr.Zero, after, "Shell_SecondaryTrayWnd", null);
                if (bar == IntPtr.Zero) break;
                after = bar;

                IntPtr child = IntPtr.Zero;
                while ((child = Native.FindWindowEx(bar, child, null, null)) != IntPtr.Zero)
                {
                    if (!Native.IsWindowVisible(child)) continue;
                    Native.RECT r;
                    if (!Native.GetWindowRect(child, out r)) continue;
                    // Taskbar buttons are small: 24–200 px wide and tall.
                    if (r.Width < 24 || r.Width > 200 || r.Height < 24 || r.Height > 200) continue;

                    var sb = new System.Text.StringBuilder(256);
                    Native.GetWindowText(child, sb, sb.Capacity);
                    string text = sb.ToString();

                    int score = Score(title, exeBase, exe, null, text, null);
                    if (score <= 0) continue;

                    rect = r;
                    Log.Debug("Taskbar button (Win32 child): text='" + text +
                        "' -> " + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height);
                    return true;
                }
            }
            return false;
        }

        private static void AddBars(AutomationElement root, Condition condition,
            List<AutomationElement> bars)
        {
            AutomationElementCollection found = root.FindAll(TreeScope.Children, condition);
            foreach (AutomationElement element in found) bars.Add(element);
        }

        /// <summary>
        /// Higher is better. An exact title beats a prefix, an exact
        /// AppUserModelID beats a partial one, and the executable name is the
        /// last resort for grouped buttons whose accessible name is just
        /// "App — 2 running windows".
        /// </summary>
        private static int Score(string title, string exeBase, string exeFile, string appId,
            string name, string id)
        {
            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(name))
            {
                if (string.Equals(name, title, StringComparison.OrdinalIgnoreCase)) return 1000;
                if (title.Length >= 4 &&
                    (name.StartsWith(title, StringComparison.OrdinalIgnoreCase) ||
                     title.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                    return 900;
            }

            if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(id))
            {
                if (string.Equals(id, "Appid: " + appId, StringComparison.OrdinalIgnoreCase))
                    return 850;
                if (id.IndexOf(appId, StringComparison.OrdinalIgnoreCase) >= 0)
                    return 800;
            }

            if (!string.IsNullOrEmpty(exeBase) && exeBase.Length >= 3 && !string.IsNullOrEmpty(id))
            {
                string value = id.StartsWith("Appid: ", StringComparison.OrdinalIgnoreCase)
                    ? id.Substring(7).Trim()
                    : id.Trim();
                if (string.Equals(value, exeBase, StringComparison.OrdinalIgnoreCase)) return 750;
                if (value.EndsWith("\\" + exeFile, StringComparison.OrdinalIgnoreCase) ||
                    value.EndsWith("\\" + exeBase, StringComparison.OrdinalIgnoreCase)) return 700;
            }

            if (!string.IsNullOrEmpty(exeBase) && exeBase.Length >= 3 && !string.IsNullOrEmpty(name))
            {
                if (name.IndexOf(exeBase, StringComparison.OrdinalIgnoreCase) >= 0) return 600;
            }
            return 0;
        }

        private static string BaseName(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return null;
            try
            {
                string name = Path.GetFileNameWithoutExtension(exe);
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Rolls a window that has just been minimized into a paper tube and lets
    /// the tube dive into the taskbar. The real window is already minimized by
    /// the system; this is a topmost layered window that draws a copy of its
    /// picture, so the engine keeps watching the desktop meanwhile.
    /// </summary>
    internal static class RollOverlay
    {
        private const double RollTime = 0.46;
        private const double SlideTime = 0.36;
        private const double Total = RollTime + SlideTime;
        private const int Strip = 8;

        /// <summary>
        /// Starts the animation. <paramref name="shot"/> is the window picture
        /// and is owned (and disposed) by the animation thread.
        /// </summary>
        public static void Play(Bitmap shot, Native.RECT frame, IntPtr hwnd, double speed)
        {
            PlayCore(shot, frame, hwnd, false, RollDirection.Auto, speed);
        }

        /// <summary>
        /// Plays the roll with a chosen direction. Auto follows the edge of the
        /// screen the taskbar is docked to.
        /// </summary>
        public static void Play(Bitmap shot, Native.RECT frame, IntPtr hwnd,
            RollDirection direction, double speed)
        {
            PlayCore(shot, frame, hwnd, false, direction, speed);
        }

        /// <summary>
        /// Rolls the copy into the notification area: used when closing a
        /// window only hides it because its program keeps running in the tray.
        /// </summary>
        public static void PlayToTray(Bitmap shot, Native.RECT frame, double speed)
        {
            PlayCore(shot, frame, IntPtr.Zero, true, RollDirection.Auto, speed);
        }

        public static void PlayToTray(Bitmap shot, Native.RECT frame,
            RollDirection direction, double speed)
        {
            PlayCore(shot, frame, IntPtr.Zero, true, direction, speed);
        }

        private static void PlayCore(Bitmap shot, Native.RECT frame, IntPtr hwnd, bool toTray,
            RollDirection direction, double speed)
        {
            if (shot == null) return;
            if (shot.Width < 60 || shot.Height < 60) { shot.Dispose(); return; }
            if (speed < 0.5) speed = 0.5;
            if (speed > 1.5) speed = 1.5;

            Thread thread = new Thread(delegate()
            {
                try
                {
                    Run(shot, frame, hwnd, toTray, direction, speed);
                }
                catch (Exception ex)
                {
                    Log.Debug("Roll failed: " + ex.GetType().Name + ": " + ex.Message);
                    try { shot.Dispose(); } catch { }
                }
            });
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Name = "Okna.Roll";
            thread.Start();
        }

        private static void Run(Bitmap shot, Native.RECT frame, IntPtr hwnd, bool toTray,
            RollDirection requested, double speed)
        {
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int screenW = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            int screenH = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (screenW <= 0 || screenH <= 0) { shot.Dispose(); return; }

            Native.RECT bar;
            bool hasBar = TaskbarFinder.TryGet(frame, out bar);
            Native.RECT tray = new Native.RECT();
            bool hasTray = toTray && TrayFinder.TryGet(frame, out tray);
            Native.RECT work = new Native.RECT();
            bool hasWork = hasBar && Native.GetWorkAreaForRect(bar, out work);
            RollDirection direction = ResolveDirection(frame, hasBar, bar,
                hasWork ? work : new Native.RECT(), requested);

            // The canvas has to hold the window, the bar and the little arc the
            // tube takes on its way down.
            const int margin = 90;
            int barLeft = hasBar ? bar.Left : (hasTray ? tray.Left : frame.Left);
            int barTop = hasBar ? bar.Top : (hasTray ? tray.Top : frame.Bottom);
            int barRight = hasBar ? bar.Right : (hasTray ? tray.Right : frame.Right);
            int barBottom = hasBar ? bar.Bottom : (hasTray ? tray.Bottom : frame.Bottom);
            int left = Math.Max(vx, Math.Min(frame.Left, barLeft) - margin);
            int top = Math.Max(vy, Math.Min(frame.Top, barTop) - margin);
            int right = Math.Min(vx + screenW, Math.Max(frame.Right, barRight) + margin);
            int bottom = Math.Min(vy + screenH, Math.Max(frame.Bottom, barBottom) + margin);
            int w = right - left;
            int h = bottom - top;
            if (w < 60 || h < 60 || w > 8192 || h > 8192 || (long)w * h > 40000000L)
            {
                shot.Dispose();
                return;
            }

            float targetX;
            float targetY;
            if (hasTray)
                TrayPoint(tray, direction, out targetX, out targetY);
            else
                TargetPoint(hwnd, frame, hasBar, bar, direction, out targetX, out targetY);

            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) { shot.Dispose(); return; }
            IntPtr dc = IntPtr.Zero;
            IntPtr bmp = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            IntPtr bits = IntPtr.Zero;
            Bitmap canvas = null;
            IntPtr overlay = IntPtr.Zero;
            Stopwatch clock = Stopwatch.StartNew();
            int frames = 0;
            try
            {
                dc = Native.CreateCompatibleDC(screen);
                Native.BITMAPINFO info = new Native.BITMAPINFO();
                info.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                info.bmiHeader.biWidth = w;
                info.bmiHeader.biHeight = -h;
                info.bmiHeader.biPlanes = 1;
                info.bmiHeader.biBitCount = 32;
                info.bmiHeader.biCompression = 0;
                bmp = Native.CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
                if (dc == IntPtr.Zero || bmp == IntPtr.Zero) return;
                old = Native.SelectObject(dc, bmp);

                canvas = new Bitmap(w, h, PixelFormat.Format32bppPArgb);

                overlay = Native.CreateWindowEx(
                    (uint)(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE |
                           Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT),
                    "STATIC", "OknaRoll", Native.WS_POPUP,
                    left, top, w, h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (overlay == IntPtr.Zero) return;
                Native.ShowWindow(overlay, Native.SW_SHOWNOACTIVATE);
                Native.SetWindowPos(overlay, new IntPtr(-1), 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

                Native.POINT dst = new Native.POINT();
                dst.X = left;
                dst.Y = top;
                Native.SIZE size = new Native.SIZE();
                size.cx = w;
                size.cy = h;
                Native.POINT src = new Native.POINT();
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;

                double last = 0;
                while (true)
                {
                    double t = clock.Elapsed.TotalSeconds * speed;
                    if (t >= Total) break;

                    // The window may have been restored (or closed) while the
                    // tube is still rolling; in that case get out of the way.
                    if ((frames & 3) == 0 && hwnd != IntPtr.Zero &&
                        (!Native.IsWindow(hwnd) || !Native.IsIconic(hwnd))) break;

                    blend.SourceConstantAlpha = 255;
                    using (Graphics g = Graphics.FromImage(canvas))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.CompositingQuality = CompositingQuality.HighSpeed;
                        g.PixelOffsetMode = PixelOffsetMode.HighSpeed;

                        if (t < RollTime)
                            DrawRoll(g, shot, frame, left, top, t / RollTime, direction);
                        else
                            DrawSlide(g, shot, frame, left, top, targetX, targetY,
                                (t - RollTime) / SlideTime, toTray, direction, ref blend);
                    }

                    BitmapData data = canvas.LockBits(new Rectangle(0, 0, w, h),
                        ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    try
                    {
                        int rowBytes = w * 4;
                        for (int y = 0; y < h; y++)
                        {
                            Native.CopyMemory(IntPtr.Add(bits, y * rowBytes),
                                IntPtr.Add(data.Scan0, y * data.Stride), (IntPtr)rowBytes);
                        }
                    }
                    finally
                    {
                        canvas.UnlockBits(data);
                    }

                    Native.UpdateLayeredWindow(overlay, IntPtr.Zero, ref dst, ref size, dc, ref src, 0,
                        ref blend, Native.ULW_ALPHA);
                    frames++;

                    Native.MSG msg;
                    while (Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
                    {
                        Native.TranslateMessage(ref msg);
                        Native.DispatchMessage(ref msg);
                    }

                    double dt = t - last;
                    last = t;
                    Thread.Sleep(dt < 0.012 ? 8 : 1);
                }
            }
            finally
            {
                if (overlay != IntPtr.Zero)
                {
                    Native.ShowWindow(overlay, Native.SW_HIDE);
                    Native.DestroyWindow(overlay);
                }
                if (dc != IntPtr.Zero && old != IntPtr.Zero) Native.SelectObject(dc, old);
                if (bmp != IntPtr.Zero) Native.DeleteObject(bmp);
                if (dc != IntPtr.Zero) Native.DeleteDC(dc);
                Native.ReleaseDC(IntPtr.Zero, screen);
                if (canvas != null) canvas.Dispose();
                if (shot != null) shot.Dispose();
                Log.Debug("Roll done frames=" + frames + " elapsed=" +
                    clock.ElapsedMilliseconds + "ms canvas=" + w + "x" + h +
                    " dir=" + direction);
            }
        }

        /// <summary>
        /// Auto rolls towards the taskbar: down for a bottom bar, up for a top
        /// bar, left for a right bar and right for a left bar. The forced
        /// values are returned untouched. The bar is the one nearest to the
        /// window, so each monitor follows its own taskbar.
        /// </summary>
        internal static RollDirection ResolveDirection(Native.RECT frame, bool hasBar,
            Native.RECT bar, RollDirection requested)
        {
            return ResolveDirection(frame, hasBar, bar, new Native.RECT(), requested);
        }

        internal static RollDirection ResolveDirection(Native.RECT frame, bool hasBar,
            Native.RECT bar, Native.RECT work, RollDirection requested)
        {
            if (requested != RollDirection.Auto) return requested;
            if (!hasBar) return RollDirection.Down;

            RollDirection edge;
            if (ResolveBarEdge(bar, work, out edge)) return edge;

            // No usable work area (a synthetic bar in a test, an unusual
            // shell): fall back to comparing the centres.
            bool barHorizontal = bar.Width >= bar.Height;
            double barCx = (bar.Left + bar.Right) * 0.5;
            double barCy = (bar.Top + bar.Bottom) * 0.5;
            double frameCx = (frame.Left + frame.Right) * 0.5;
            double frameCy = (frame.Top + frame.Bottom) * 0.5;

            if (barHorizontal)
                return barCy >= frameCy ? RollDirection.Down : RollDirection.Up;
            return barCx >= frameCx ? RollDirection.Right : RollDirection.Left;
        }

        /// <summary>
        /// The edge a bar is docked to, read from the monitor work area: the
        /// work area stops exactly where the bar begins. This is more reliable
        /// than comparing centres when a window sits far from the bar.
        /// </summary>
        internal static bool ResolveBarEdge(Native.RECT bar, Native.RECT work,
            out RollDirection direction)
        {
            direction = RollDirection.Down;
            if (work.Width <= 0 || work.Height <= 0) return false;

            if (bar.Width >= bar.Height)
            {
                if (bar.Bottom >= work.Bottom - 2 && bar.Top >= work.Top - 2)
                {
                    direction = RollDirection.Down;
                    return true;
                }
                if (bar.Top <= work.Top + 2 && bar.Bottom <= work.Bottom + 2)
                {
                    direction = RollDirection.Up;
                    return true;
                }
                return false;
            }

            if (bar.Right >= work.Right - 2)
            {
                direction = RollDirection.Right;
                return true;
            }
            if (bar.Left <= work.Left + 2)
            {
                direction = RollDirection.Left;
                return true;
            }
            return false;
        }

        internal static bool IsHorizontal(RollDirection direction)
        {
            return direction == RollDirection.Left || direction == RollDirection.Right;
        }

        /// <summary>
        /// Where the tube should end up: inside the window's real taskbar
        /// button when automation can find it, otherwise in the bar under the
        /// window's centre. The point is always clamped into the bar so the
        /// tube never dives into empty desktop.
        /// </summary>
        internal static void TargetPoint(IntPtr hwnd, Native.RECT frame, bool hasBar,
            Native.RECT bar, RollDirection direction, out float x, out float y)
        {
            Native.RECT button;
            if (hwnd != IntPtr.Zero && TaskbarButtonFinder.TryGet(hwnd, out button))
            {
                x = (button.Left + button.Right) * 0.5f;
                y = (button.Top + button.Bottom) * 0.5f;
                if (hasBar) ClampToBar(ref x, ref y, bar);
                Log.Debug("Roll target: taskbar button " + button.Left + "," + button.Top +
                    " " + button.Width + "x" + button.Height + " -> " + (int)x + "," + (int)y);
                return;
            }
            Log.Debug("Roll target: geometry fallback");

            float cx = frame.Left + frame.Width * 0.5f;
            float cy = frame.Top + frame.Height * 0.5f;
            if (!hasBar)
            {
                x = cx;
                y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN) +
                    Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN) - 24;
                return;
            }

            if (bar.Width >= bar.Height)
            {
                x = Math.Max(bar.Left + 24, Math.Min(bar.Right - 24, cx));
                y = bar.Top + bar.Height * 0.5f;
            }
            else
            {
                y = Math.Max(bar.Top + 24, Math.Min(bar.Bottom - 24, cy));
                x = bar.Left + bar.Width * 0.5f;
            }
        }

        private static void ClampToBar(ref float x, ref float y, Native.RECT bar)
        {
            x = Math.Max(bar.Left + 8, Math.Min(bar.Right - 8, x));
            y = Math.Max(bar.Top + 8, Math.Min(bar.Bottom - 8, y));
        }

        /// <summary>
        /// Where the tube should end up inside the notification area. The
        /// icons sit at the start of the tray and the clock takes its end, so
        /// a third of the way in is on the icons, whatever the bar's edge.
        /// </summary>
        internal static void TrayPoint(Native.RECT tray, RollDirection direction,
            out float x, out float y)
        {
            if (tray.Width >= tray.Height)
            {
                x = tray.Left + Math.Max(18f, tray.Width * 0.30f);
                y = tray.Top + tray.Height * 0.5f;
            }
            else
            {
                y = tray.Top + Math.Max(18f, tray.Height * 0.30f);
                x = tray.Left + tray.Width * 0.5f;
            }
        }

        /// <summary>
        /// The geometry of the first phase in screen coordinates: the flat
        /// remaining picture, the tube, the texture strip on the tube and the
        /// picture region still visible. Exposed for the geometry tests.
        /// </summary>
        internal static void ComputeRollGeometry(Native.RECT frame, RollDirection direction,
            double p, out RectangleF tube, out RectangleF flat, out RectangleF flatSource,
            out RectangleF tubeSource, out bool horizontal)
        {
            if (p < 0) p = 0;
            if (p > 1) p = 1;
            double s = p * p * (3 - 2 * p);
            horizontal = IsHorizontal(direction);

            float shotW = frame.Width;
            float shotH = frame.Height;

            if (!horizontal)
            {
                float maxRadius = Math.Min(frame.Height * 0.5f, Math.Max(10f, frame.Height * 0.03f));
                float radius = 3f + (maxRadius - 3f) * (float)Math.Pow(s, 0.85);
                bool down = direction != RollDirection.Up;
                float centerY = down
                    ? frame.Top + radius + (frame.Height - 2 * radius) * (float)s
                    : frame.Bottom - radius - (frame.Height - 2 * radius) * (float)s;

                tube = new RectangleF(frame.Left, centerY - radius, frame.Width, radius * 2);
                float strip = Math.Min(Strip, shotH);
                if (down)
                {
                    float flatTop = centerY + radius;
                    flat = new RectangleF(frame.Left, flatTop, frame.Width,
                        Math.Max(0f, frame.Bottom - flatTop));
                    flatSource = new RectangleF(0, flatTop - frame.Top, shotW,
                        Math.Max(0f, frame.Bottom - flatTop));
                    tubeSource = new RectangleF(0, 0, shotW, strip);
                }
                else
                {
                    float flatBottom = centerY - radius;
                    flat = new RectangleF(frame.Left, frame.Top, frame.Width,
                        Math.Max(0f, flatBottom - frame.Top));
                    flatSource = new RectangleF(0, 0, shotW, Math.Max(0f, flatBottom - frame.Top));
                    tubeSource = new RectangleF(0, Math.Max(0f, shotH - strip), shotW, strip);
                }
            }
            else
            {
                float maxRadius = Math.Min(frame.Width * 0.5f, Math.Max(10f, frame.Width * 0.03f));
                float radius = 3f + (maxRadius - 3f) * (float)Math.Pow(s, 0.85);
                bool left = direction == RollDirection.Left;
                float centerX = left
                    ? frame.Right - radius - (frame.Width - 2 * radius) * (float)s
                    : frame.Left + radius + (frame.Width - 2 * radius) * (float)s;

                tube = new RectangleF(centerX - radius, frame.Top, radius * 2, frame.Height);
                float strip = Math.Min(Strip, shotW);
                if (left)
                {
                    float flatRight = centerX - radius;
                    flat = new RectangleF(frame.Left, frame.Top,
                        Math.Max(0f, flatRight - frame.Left), frame.Height);
                    flatSource = new RectangleF(0, 0, Math.Max(0f, flatRight - frame.Left), shotH);
                    tubeSource = new RectangleF(Math.Max(0f, shotW - strip), 0, strip, shotH);
                }
                else
                {
                    float flatLeft = centerX + radius;
                    flat = new RectangleF(flatLeft, frame.Top,
                        Math.Max(0f, frame.Right - flatLeft), frame.Height);
                    flatSource = new RectangleF(flatLeft - frame.Left, 0,
                        Math.Max(0f, frame.Right - flatLeft), shotH);
                    tubeSource = new RectangleF(0, 0, strip, shotH);
                }
            }
        }

        /// <summary>
        /// Phase one: the picture is rolled from one edge. The flat part keeps
        /// its scale (the material disappears into the tube), the tube grows
        /// as more of the window is gathered.
        /// </summary>
        private static void DrawRoll(Graphics g, Bitmap shot, Native.RECT frame,
            int originX, int originY, double p, RollDirection direction)
        {
            RectangleF tube, flat, flatSource, tubeSource;
            bool horizontal;
            ComputeRollGeometry(frame, direction, p, out tube, out flat, out flatSource,
                out tubeSource, out horizontal);

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            if (flat.Width > 1 && flat.Height > 1 && flatSource.Width > 1 && flatSource.Height > 1)
            {
                g.DrawImage(shot,
                    new RectangleF(flat.X - originX, flat.Y - originY, flat.Width, flat.Height),
                    flatSource, GraphicsUnit.Pixel);
            }

            g.InterpolationMode = InterpolationMode.Bilinear;
            DrawTube(g, shot,
                new RectangleF(tube.X - originX, tube.Y - originY, tube.Width, tube.Height),
                tubeSource, horizontal);
        }

        /// <summary>
        /// Phase two: the tube travels to the taskbar in a shallow arc,
        /// shrinking to the size of a taskbar button and fading into it.
        /// </summary>
        private static void DrawSlide(Graphics g, Bitmap shot, Native.RECT frame,
            int originX, int originY, float targetX, float targetY, double q,
            bool toTray, RollDirection direction, ref Native.BLENDFUNCTION blend)
        {
            if (q < 0) q = 0;
            if (q > 1) q = 1;
            double e = q * q * (3 - 2 * q);
            bool horizontal = IsHorizontal(direction);

            float maxRadius = Math.Min((horizontal ? frame.Width : frame.Height) * 0.5f,
                Math.Max(10f, (horizontal ? frame.Width : frame.Height) * 0.03f));
            float startLen = horizontal ? frame.Height : frame.Width;
            float startDia = maxRadius * 2;
            float startX;
            float startY;
            switch (direction)
            {
                case RollDirection.Up:
                    startX = frame.Left + frame.Width * 0.5f;
                    startY = frame.Top + maxRadius;
                    break;
                case RollDirection.Left:
                    startX = frame.Right - maxRadius;
                    startY = frame.Top + frame.Height * 0.5f;
                    break;
                case RollDirection.Right:
                    startX = frame.Left + maxRadius;
                    startY = frame.Top + frame.Height * 0.5f;
                    break;
                default:
                    startX = frame.Left + frame.Width * 0.5f;
                    startY = frame.Bottom - maxRadius;
                    break;
            }

            // A tray icon is smaller than a taskbar button: the tube shrinks
            // to its size before it disappears into the notification area.
            float endLen = toTray ? (horizontal ? 14f : 28f) : (horizontal ? 20f : 44f);
            float endDia = toTray ? (horizontal ? 7f : 14f) : (horizontal ? 10f : 20f);

            // A quadratic Bezier with the control point lifted a little makes
            // the tube leave the window with a soft hop instead of sliding in
            // a straight line.
            float lift = Math.Min(70f, Math.Abs(targetY - startY) * 0.18f + 18f);
            float controlX = startX + (targetX - startX) * 0.35f;
            float controlY = Math.Min(startY, targetY) - lift;
            float u = 1f - (float)e;
            float cx = u * u * startX + 2 * u * (float)e * controlX + (float)e * (float)e * targetX;
            float cy = u * u * startY + 2 * u * (float)e * controlY + (float)e * (float)e * targetY;

            float width = startLen + (endLen - startLen) * (float)e;
            float height = startDia + (endDia - startDia) * (float)e;

            // Fade away while the tube is already inside the bar.
            double fade = 1.0;
            if (q > 0.72) fade = 1.0 - (q - 0.72) / 0.28;
            if (fade < 0) fade = 0;
            blend.SourceConstantAlpha = (byte)Math.Round(255 * fade);

            float tilt = (float)Math.Sin(q * Math.PI) * 5f * (targetX >= startX ? 1f : -1f);
            GraphicsState state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(tilt);
            g.TranslateTransform(-cx, -cy);

            g.InterpolationMode = InterpolationMode.Bilinear;
            float strip = Math.Min(Strip, horizontal ? shot.Width : shot.Height);
            if (horizontal)
            {
                // Sideways: the tube is long along Y and thick along X. Its
                // texture is the edge the roll started from.
                float tubeX = cx - height * 0.5f - originX;
                float tubeY = cy - width * 0.5f - originY;
                RectangleF source = direction == RollDirection.Left
                    ? new RectangleF(Math.Max(0f, shot.Width - strip), 0, strip, shot.Height)
                    : new RectangleF(0, 0, strip, shot.Height);
                DrawTube(g, shot, new RectangleF(tubeX, tubeY, height, width), source, true);
            }
            else
            {
                float tubeX = cx - width * 0.5f - originX;
                float tubeY = cy - height * 0.5f - originY;
                RectangleF source = direction == RollDirection.Up
                    ? new RectangleF(0, Math.Max(0f, shot.Height - strip), shot.Width, strip)
                    : new RectangleF(0, 0, shot.Width, strip);
                DrawTube(g, shot, new RectangleF(tubeX, tubeY, width, height), source, false);
            }

            g.Restore(state);
        }

        /// <summary>
        /// Draws a rolled tube: the picture squashed into a rounded bar, lit
        /// like a cylinder, with a soft shadow underneath. For a sideways roll
        /// the tube is long along the vertical axis and lit from the left
        /// instead of from above.
        /// </summary>
        private static void DrawTube(Graphics g, Bitmap shot, RectangleF tube, RectangleF source,
            bool horizontal)
        {
            float radius = Math.Min(tube.Height * 0.5f, tube.Width * 0.5f);
            if (radius < 1f) radius = 1f;
            if (tube.Width < 2 || tube.Height < 2) return;

            float off = Math.Max(2f, radius * 0.35f);
            using (GraphicsPath shadowPath = RoundedRect(
                new RectangleF(horizontal ? tube.X + off : tube.X,
                               horizontal ? tube.Y : tube.Y + off,
                               tube.Width, tube.Height),
                radius))
            {
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(36, 0, 0, 0)))
                    g.FillPath(shadow, shadowPath);
            }

            using (GraphicsPath path = RoundedRect(tube, radius))
            {
                GraphicsState state = g.Save();
                g.SetClip(path, CombineMode.Replace);

                if (source.Width >= 1f && source.Height >= 1f)
                    g.DrawImage(shot, tube, source, GraphicsUnit.Pixel);

                RectangleF shadeRect = horizontal
                    ? new RectangleF(tube.X - 1f, tube.Y, Math.Max(1f, tube.Width), Math.Max(1f, tube.Height))
                    : new RectangleF(tube.X, tube.Y - 1f, Math.Max(1f, tube.Width), Math.Max(1f, tube.Height));
                using (LinearGradientBrush shade = new LinearGradientBrush(shadeRect,
                    Color.Transparent, Color.Transparent,
                    horizontal ? LinearGradientMode.Horizontal : LinearGradientMode.Vertical))
                {
                    ColorBlend blend = new ColorBlend(5);
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(96, 255, 255, 255),
                        Color.FromArgb(26, 255, 255, 255),
                        Color.FromArgb(0, 0, 0, 0),
                        Color.FromArgb(46, 0, 0, 0),
                        Color.FromArgb(126, 0, 0, 0)
                    };
                    blend.Positions = new float[] { 0f, 0.20f, 0.46f, 0.80f, 1f };
                    shade.InterpolationColors = blend;
                    g.FillRectangle(shade, tube);
                }

                g.Restore(state);

                using (Pen edge = new Pen(Color.FromArgb(110, 0, 0, 0), 1f))
                    g.DrawPath(edge, path);
                using (Pen light = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                {
                    if (horizontal)
                    {
                        float lineX = tube.X + tube.Width * 0.26f;
                        if (tube.Height > radius * 2.4f)
                            g.DrawLine(light, lineX, tube.Y + radius * 0.7f,
                                lineX, tube.Bottom - radius * 0.7f);
                    }
                    else
                    {
                        float lineY = tube.Y + tube.Height * 0.26f;
                        if (tube.Width > radius * 2.4f)
                            g.DrawLine(light, tube.X + radius * 0.7f, lineY,
                                tube.Right - radius * 0.7f, lineY);
                    }
                }
            }
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
