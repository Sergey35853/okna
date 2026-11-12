using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace Okna
{
    /// <summary>
    /// Fades the picture of a just-closed window out to transparency. Unlike
    /// shards or fire, fade leaves no debris on screen — the window simply
    /// dissolves. Runs on its own thread with a topmost layered window, so the
    /// engine keeps watching the desktop while the fade works.
    /// </summary>
    internal static class FadeOverlay
    {
        private const double Duration = 0.85;

        /// <summary>
        /// Starts the animation. <paramref name="shot"/> is the window picture
        /// and is owned (and disposed) by the animation thread.
        /// </summary>
        public static void Play(Bitmap shot, Native.RECT frame, double speed)
        {
            if (shot == null) return;
            if (speed < 0.5) speed = 0.5;
            if (speed > 1.5) speed = 1.5;
            Thread thread = new Thread(delegate()
            {
                try
                {
                    Run(shot, frame, speed);
                }
                catch (Exception ex)
                {
                    Log.Debug("Fade failed: " + ex.GetType().Name + ": " + ex.Message);
                    try { shot.Dispose(); } catch { }
                }
            });
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Name = "Okna.Fade";
            thread.Start();
        }

        private static void Run(Bitmap shot, Native.RECT frame, double speed)
        {
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int screenW = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            int screenH = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (screenW <= 0 || screenH <= 0) { shot.Dispose(); return; }

            // A small margin around the window so the fade stays on screen.
            const int margin = 20;
            int left = Math.Max(vx, frame.Left - margin);
            int top = Math.Max(vy, frame.Top - margin);
            int right = Math.Min(vx + screenW, frame.Right + margin);
            int bottom = Math.Min(vy + screenH, frame.Bottom + margin);
            int w = right - left;
            int h = bottom - top;
            if (w < 40 || h < 40 || w > 8192 || h > 8192) { shot.Dispose(); return; }

            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) { shot.Dispose(); return; }
            IntPtr dc = IntPtr.Zero;
            IntPtr bmp = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            IntPtr bits = IntPtr.Zero;
            Bitmap canvas = null;
            IntPtr hwnd = IntPtr.Zero;
            Stopwatch clock = Stopwatch.StartNew();
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

                hwnd = Native.CreateWindowEx(
                    (uint)(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE |
                           Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT),
                    "STATIC", "OknaFade", Native.WS_POPUP,
                    left, top, w, h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) return;
                Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
                Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0,
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

                float originX = frame.Left - left;
                float originY = frame.Top - top;

                double last = 0;
                while (true)
                {
                    // Effect speed scales the animation clock, so 150% fades
                    // out 1.5x faster and 50% twice as slow.
                    double t = clock.Elapsed.TotalSeconds * speed;
                    if (t >= Duration) break;

                    double dt = t - last;
                    last = t;

                    // Linear fade from opaque to transparent over Duration.
                    double fade = 1.0 - t / Duration;
                    if (fade < 0) fade = 0;
                    blend.SourceConstantAlpha = (byte)Math.Round(255 * fade);

                    using (Graphics g = Graphics.FromImage(canvas))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.DrawImageUnscaled(shot, (int)originX, (int)originY);
                    }

                    BitmapData data = canvas.LockBits(new Rectangle(0, 0, w, h),
                        ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    try
                    {
                        int rowBytes = w * 4;
                        if (data.Stride == rowBytes)
                        {
                            Native.CopyMemory(bits, data.Scan0, (IntPtr)(rowBytes * h));
                        }
                        else
                        {
                            for (int y = 0; y < h; y++)
                            {
                                Native.CopyMemory(IntPtr.Add(bits, y * rowBytes),
                                    IntPtr.Add(data.Scan0, y * data.Stride), (IntPtr)rowBytes);
                            }
                        }
                    }
                    finally
                    {
                        canvas.UnlockBits(data);
                    }

                    Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, dc, ref src, 0,
                        ref blend, Native.ULW_ALPHA);

                    Native.MSG msg;
                    while (Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
                    {
                        Native.TranslateMessage(ref msg);
                        Native.DispatchMessage(ref msg);
                    }

                    Thread.Sleep(16); // ~60 fps
                }
            }
            finally
            {
                if (hwnd != IntPtr.Zero)
                {
                    Native.ShowWindow(hwnd, Native.SW_HIDE);
                    Native.DestroyWindow(hwnd);
                }
                if (dc != IntPtr.Zero && old != IntPtr.Zero) Native.SelectObject(dc, old);
                if (bmp != IntPtr.Zero) Native.DeleteObject(bmp);
                if (dc != IntPtr.Zero) Native.DeleteDC(dc);
                Native.ReleaseDC(IntPtr.Zero, screen);
                if (canvas != null) canvas.Dispose();
                if (shot != null) shot.Dispose();
                Log.Debug("Fade done elapsed=" + clock.ElapsedMilliseconds + "ms canvas=" + w + "x" + h);
            }
        }
    }
}
