using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Okna
{
    internal static class Native
    {
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_CANCELMODE = 0x001F;
        public const int WM_APP = 0x8000;
        public const int WM_APP_SHOW = WM_APP + 1;

        public const int HTCAPTION = 2;
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_CAPTION = 0x00C00000L;
        public const long WS_THICKFRAME = 0x00040000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;
        public const long WS_EX_TRANSPARENT = 0x00000020L;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_NOSENDCHANGING = 0x0400;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;
        public const long WS_EX_LAYERED = 0x00080000L;
        public const long WS_EX_TOPMOST = 0x00000008L;
        public const uint WS_POPUP = 0x80000000;

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;

        public const uint ULW_ALPHA = 2;
        public const uint LWA_ALPHA = 2;
        public const byte AC_SRC_OVER = 0;
        public const byte AC_SRC_ALPHA = 1;
        public const uint SRCCOPY = 0x00CC0020;
        public const uint BLACKNESS = 0x00000042;
        public const uint WHITENESS = 0x00FF0062;
        public const uint PW_RENDERFULLCONTENT = 2;
        public const uint PM_REMOVE = 1;

        public const uint SMTO_BLOCK = 0x0001;
        public const uint SMTO_ABORTIFHUNG = 0x0002;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint TOKEN_QUERY = 0x0008;
        public const int TokenElevation = 20;

        public const int VK_LBUTTON = 0x01;
        public const int VK_SHIFT = 0x10;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_MENU = 0x12;
        public const int VK_F4 = 0x73;
        public const int SM_CXSCREEN = 0;

        public const int DWMWA_CLOAKED = 14;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_ELEVATION
        {
            public int TokenIsElevated;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowEnabled(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hwnd);

        public const int SM_CYSCREEN = 1;
        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out UIntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName,
            uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size,
            IntPtr srcDc, ref POINT src, uint colorKey, ref BLENDFUNCTION blend, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);

        [DllImport("user32.dll")]
        public static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        public static extern bool PatBlt(IntPtr dc, int x, int y, int width, int height, uint rop);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr dst, int x, int y, int width, int height,
            IntPtr src, int srcX, int srcY, uint rop);

        [DllImport("msimg32.dll")]
        public static extern bool AlphaBlend(IntPtr dst, int x, int y, int width, int height,
            IntPtr src, int srcX, int srcY, int srcW, int srcH, BLENDFUNCTION blend);

        [DllImport("gdi32.dll")]
        public static extern bool StretchBlt(IntPtr dst, int x, int y, int width, int height,
            IntPtr src, int srcX, int srcY, int srcW, int srcH, uint rop);

        [DllImport("gdi32.dll")]
        public static extern int SetStretchBltMode(IntPtr dc, int mode);

        public const int HALFTONE = 4;

        [DllImport("dwmapi.dll")]
        public static extern int DwmFlush();

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, IntPtr count);

        [DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory", SetLastError = false)]
        public static extern void ZeroMemory(IntPtr dest, IntPtr count);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint period);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, out TOKEN_ELEVATION info,
            int infoLength, out int returnLength);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

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

        public static IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
        }
    }

    internal static class Log
    {
        private static readonly object Sync = new object();
        private static readonly string FilePath = ResolvePath();
        private static readonly string ExeFallback =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Okna.log");
        private static readonly string TempFallback =
            Path.Combine(Path.GetTempPath(), "Okna.log");
        private static readonly bool DebugEnabled =
            string.Equals(Environment.GetEnvironmentVariable("OKNA_DEBUG"), "1", StringComparison.Ordinal);

        public static string LogPath
        {
            get { return FilePath; }
        }

        private static string ResolvePath()
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                    return Path.Combine(Path.Combine(local, "Okna"), "okna.log");
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Okna.log");
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Debug(string message)
        {
            if (DebugEnabled)
                Write("DEBUG", message);
        }

        private static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                          " [" + level + "] " + message + Environment.NewLine;
            if (TryWrite(FilePath, line)) return;
            if (TryWrite(ExeFallback, line)) return;
            TryWrite(TempFallback, line);
        }

        private static bool TryWrite(string path, string line)
        {
            try
            {
                lock (Sync)
                {
                    string folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder))
                        Directory.CreateDirectory(folder);
                    FileInfo info = new FileInfo(path);
                    if (info.Exists && info.Length > 1024 * 1024)
                    {
                        string old = path + ".old";
                        try { if (File.Exists(old)) File.Delete(old); } catch { }
                        try { File.Move(path, old); } catch { }
                    }
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
