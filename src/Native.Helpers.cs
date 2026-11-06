using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Okna
{
    internal static partial class Native
    {
        public static long GetWindowLongPtr(IntPtr hwnd, int index)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hwnd, index).ToInt64();
            return GetWindowLong32(hwnd, index);
        }

        public static long SetWindowLongPtr(IntPtr hwnd, int index, long value)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hwnd, index, new IntPtr(value)).ToInt64();
            return SetWindowLong32(hwnd, index, (int)value);
        }

        /// <summary>
        /// Makes a foreign window see-through (alpha 0-255). The window keeps
        /// being composited, so the app keeps painting and PrintWindow still
        /// returns its content; it just does not reach the screen.
        /// </summary>
        public static bool SetWindowAlpha(IntPtr hwnd, byte alpha)
        {
            return SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
        }

        /// <summary>
        /// Reads a layered window's alpha. Returns false when the window is not
        /// layered (or has no alpha), which is what a crash recovery check needs.
        /// </summary>
        public static bool TryGetWindowAlpha(IntPtr hwnd, out byte alpha)
        {
            uint key;
            uint flags;
            alpha = 255;
            if (!GetLayeredWindowAttributes(hwnd, out key, out alpha, out flags)) return false;
            return (flags & LWA_ALPHA) != 0;
        }

        public static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            int hr = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, 4);
            return hr == 0 && cloaked != 0;
        }

        public static void EnableDarkTitleBar(IntPtr hwnd)
        {
            int enabled = 1;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, 4);
            if (hr != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref enabled, 4);
        }

        /// <summary>
        /// Turns the DWM open/close/minimize/restore animations of a window on
        /// or off. While the window is rolled up by Okna the system's own
        /// minimize animation would slide a second copy towards the taskbar,
        /// so it is switched off for that moment and switched back on after.
        /// </summary>
        public static bool SetWindowTransitions(IntPtr hwnd, bool enabled)
        {
            try
            {
                int value = enabled ? 0 : 1;
                return DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref value, 4) == 0;
            }
            catch (EntryPointNotFoundException) { return false; }
            catch (DllNotFoundException) { return false; }
        }

        public static void EnablePerMonitorDpi()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                // PROCESS_PER_MONITOR_DPI_AWARE = 2
                if (SetProcessDpiAwareness(2) == 0)
                    return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try { SetProcessDPIAware(); }
            catch { }
        }

        /// <summary>
        /// Converts a 96-DPI design value to physical pixels for the given
        /// window DPI. Kept pure so mixed-DPI behaviour can be tested without
        /// a second monitor.
        /// </summary>
        internal static int ScaleForDpi(int value, int dpi)
        {
            if (dpi < 48) dpi = 96;
            return (value * dpi + 48) / 96;
        }

        public static bool IsProcessElevated(uint processId, out bool elevated)
        {
            elevated = false;
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (process == IntPtr.Zero)
                return false;
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(process, TOKEN_QUERY, out token))
                    return false;
                TOKEN_ELEVATION info;
                int returned;
                if (!GetTokenInformation(token, TokenElevation, out info, Marshal.SizeOf(typeof(TOKEN_ELEVATION)), out returned))
                    return false;
                elevated = info.TokenIsElevated != 0;
                return true;
            }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
                CloseHandle(process);
            }
        }

        public static bool IsOwnProcessWindow(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return pid == GetCurrentProcessId();
        }

        /// <summary>
        /// True when another window is painted on top of <paramref name="rect"/>,
        /// i.e. a screen grab of that rectangle would show that window instead
        /// of the target. Always-on-top windows (some Electron shells) do this
        /// even while the target is the foreground window.
        /// </summary>
        public static bool IsCovered(IntPtr target, RECT rect)
        {
            // A maximized window's frame overlaps the taskbar by a pixel or
            // two, which is not a window painted over it.
            const int Inset = 3;
            int left = rect.Left + Inset;
            int top = rect.Top + Inset;
            int right = rect.Right - Inset;
            int bottom = rect.Bottom - Inset;
            if (right <= left || bottom <= top) return false;

            for (IntPtr h = GetWindow(target, GW_HWNDPREV); h != IntPtr.Zero;
                 h = GetWindow(h, GW_HWNDPREV))
            {
                if (h == target) continue;
                if (!IsWindowVisible(h) || IsIconic(h)) continue;
                if (IsCloaked(h)) continue;
                if (IsOwnProcessWindow(h)) continue;

                RECT r;
                if (!GetWindowRect(h, out r)) continue;
                if (r.Right <= r.Left || r.Bottom <= r.Top) continue;
                if (r.Left >= right || r.Right <= left ||
                    r.Top >= bottom || r.Bottom <= top) continue;

                return true;
            }
            return false;
        }

        public static string GetWindowTitle(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetWindowClass(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags,
            StringBuilder name, ref int size);

        /// <summary>
        /// The file name of the program that owns the window ("chrome.exe"),
        /// or null when it cannot be read (elevated programs).
        /// </summary>
        public static string GetWindowProcessName(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return null;
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (!QueryFullProcessImageName(process, 0, sb, ref size)) return null;
                string path = sb.ToString(0, size);
                try { return System.IO.Path.GetFileName(path); }
                catch { return null; }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        public static bool IsRemoteSession()
        {
            return GetSystemMetrics(SM_REMOTESESSION) != 0;
        }

        public static bool IsBatterySaverOn()
        {
            SYSTEM_POWER_STATUS status;
            if (!GetSystemPowerStatus(out status)) return false;
            return status.SystemStatusFlag == 1;
        }

        /// <summary>True while the machine runs on its battery (no AC power).</summary>
        public static bool IsOnBattery()
        {
            SYSTEM_POWER_STATUS status;
            if (!GetSystemPowerStatus(out status)) return false;
            return status.ACLineStatus == 0;
        }

        /// <summary>
        /// False when the user turned animations off in Windows' accessibility
        /// settings ("Show animations in Windows"). Defaults to true when the
        /// setting cannot be read, so the effects keep working everywhere.
        /// </summary>
        public static bool ClientAreaAnimationEnabled()
        {
            try
            {
                int enabled;
                if (SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out enabled, 0))
                    return enabled != 0;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            return true;
        }

        /// <summary>
        /// True when the window covers its whole monitor, which is how games
        /// and full-screen video look. A merely maximized window leaves the
        /// taskbar visible and is not counted.
        /// </summary>
        public static bool IsFullscreenWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
            if (IsOwnProcessWindow(hwnd)) return false;

            string cls = GetWindowClass(hwnd);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd")
                return false;

            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) return false;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return false;
            var info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref info)) return false;

            const int slack = 2;
            return rect.Left <= info.rcMonitor.Left + slack &&
                   rect.Top <= info.rcMonitor.Top + slack &&
                   rect.Right >= info.rcMonitor.Right - slack &&
                   rect.Bottom >= info.rcMonitor.Bottom - slack;
        }

        public static IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint length,
            StringBuilder buffer);

        /// <summary>
        /// The AppUserModelID of the process that owns the window. Packaged
        /// apps and programs launched through a shortcut report one; classic
        /// programs return APPMODEL_ERROR_NO_APPLICATION and the caller falls
        /// back to the executable name.
        /// </summary>
        public static string GetWindowAppId(IntPtr hwnd)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return null;
                IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (process == IntPtr.Zero) return null;
                try
                {
                    uint length = 512;
                    StringBuilder buffer = new StringBuilder((int)length);
                    if (GetApplicationUserModelId(process, ref length, buffer) != 0) return null;
                    string value = buffer.ToString();
                    return value.Length == 0 ? null : value;
                }
                finally
                {
                    CloseHandle(process);
                }
            }
            catch
            {
                return null;
            }
        }

    }
}
