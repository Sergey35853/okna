using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

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

        /// <summary>
        /// Starts the animation. <paramref name="shot"/> is the window picture
        /// and is owned (and disposed) by the animation thread.
        /// </summary>
        public static void Play(Bitmap shot, Native.RECT frame, IntPtr hwnd)
        {
            if (shot == null) return;
            if (shot.Width < 60 || shot.Height < 60) { shot.Dispose(); return; }

            Thread thread = new Thread(delegate()
            {
                try
                {
                    Run(shot, frame, hwnd);
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

        private static void Run(Bitmap shot, Native.RECT frame, IntPtr hwnd)
        {
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int screenW = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            int screenH = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (screenW <= 0 || screenH <= 0) { shot.Dispose(); return; }

            Native.RECT bar;
            bool hasBar = TaskbarFinder.TryGet(frame, out bar);

            // The canvas has to hold the window, the bar and the little arc the
            // tube takes on its way down.
            const int margin = 90;
            int barLeft = hasBar ? bar.Left : frame.Left;
            int barTop = hasBar ? bar.Top : frame.Bottom;
            int barRight = hasBar ? bar.Right : frame.Right;
            int barBottom = hasBar ? bar.Bottom : frame.Bottom;
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
            TargetPoint(frame, hasBar, bar, out targetX, out targetY);

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
                    double t = clock.Elapsed.TotalSeconds;
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
                            DrawRoll(g, shot, frame, left, top, t / RollTime);
                        else
                            DrawSlide(g, shot, frame, left, top, targetX, targetY,
                                (t - RollTime) / SlideTime, ref blend);
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
                    clock.ElapsedMilliseconds + "ms canvas=" + w + "x" + h);
            }
        }

        /// <summary>
        /// Where the tube should end up: inside the taskbar, as close as the
        /// geometry allows to the window's own centre.
        /// </summary>
        private static void TargetPoint(Native.RECT frame, bool hasBar, Native.RECT bar,
            out float x, out float y)
        {
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

        /// <summary>
        /// Phase one: the picture is rolled from the top down. The flat part
        /// keeps its scale (the material disappears into the tube), the tube
        /// grows as more of the window is gathered.
        /// </summary>
        private static void DrawRoll(Graphics g, Bitmap shot, Native.RECT frame,
            int originX, int originY, double p)
        {
            if (p < 0) p = 0;
            if (p > 1) p = 1;
            double s = p * p * (3 - 2 * p);

            float maxRadius = Math.Min(frame.Height * 0.5f, Math.Max(10f, frame.Height * 0.03f));
            float radius = 3f + (maxRadius - 3f) * (float)Math.Pow(s, 0.85);
            float centerY = frame.Top + radius + (frame.Height - 2 * radius) * (float)s;
            float flatTop = centerY + radius;

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            float flatHeight = frame.Bottom - flatTop;
            if (flatHeight > 1)
            {
                g.DrawImage(shot,
                    new RectangleF(frame.Left - originX, flatTop - originY, frame.Width, flatHeight),
                    new RectangleF(0, flatTop - frame.Top, shot.Width, flatHeight),
                    GraphicsUnit.Pixel);
            }

            g.InterpolationMode = InterpolationMode.Bilinear;
            // The tube shows only the outermost layer of the roll: a thin strip
            // of the picture stretched into a band, so it reads as rolled paper
            // instead of a squashed screenshot.
            float stripY = Math.Max(0f, centerY - radius - frame.Top - 4f);
            float stripH = Math.Min(8f, shot.Height - stripY);
            DrawTube(g, shot,
                new RectangleF(frame.Left - originX, centerY - radius - originY,
                    frame.Width, radius * 2),
                new RectangleF(0, stripY, shot.Width, Math.Max(1f, stripH)));
        }

        /// <summary>
        /// Phase two: the tube travels to the taskbar in a shallow arc,
        /// shrinking to the size of a taskbar button and fading into it.
        /// </summary>
        private static void DrawSlide(Graphics g, Bitmap shot, Native.RECT frame,
            int originX, int originY, float targetX, float targetY, double q,
            ref Native.BLENDFUNCTION blend)
        {
            if (q < 0) q = 0;
            if (q > 1) q = 1;
            double e = q * q * (3 - 2 * q);

            float maxRadius = Math.Min(frame.Height * 0.5f, Math.Max(10f, frame.Height * 0.03f));
            float startW = frame.Width;
            float startH = maxRadius * 2;
            float startX = frame.Left + startW * 0.5f;
            float startY = frame.Bottom - maxRadius;

            const float endW = 44f;
            const float endH = 20f;

            // A quadratic Bezier with the control point lifted a little makes
            // the tube leave the window with a soft hop instead of sliding in
            // a straight line.
            float lift = Math.Min(70f, Math.Abs(targetY - startY) * 0.18f + 18f);
            float controlX = startX + (targetX - startX) * 0.35f;
            float controlY = Math.Min(startY, targetY) - lift;
            float u = 1f - (float)e;
            float cx = u * u * startX + 2 * u * (float)e * controlX + (float)e * (float)e * targetX;
            float cy = u * u * startY + 2 * u * (float)e * controlY + (float)e * (float)e * targetY;

            float width = startW + (endW - startW) * (float)e;
            float height = startH + (endH - startH) * (float)e;

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
            float stripY = Math.Max(0f, shot.Height - 8f);
            DrawTube(g, shot,
                new RectangleF(cx - width * 0.5f - originX, cy - height * 0.5f - originY,
                    width, height),
                new RectangleF(0, stripY, shot.Width, shot.Height - stripY));

            g.Restore(state);
        }

        /// <summary>
        /// Draws a rolled tube: the picture squashed into a rounded bar, lit
        /// from above like a cylinder, with a soft shadow underneath.
        /// </summary>
        private static void DrawTube(Graphics g, Bitmap shot, RectangleF tube, RectangleF source)
        {
            float radius = Math.Min(tube.Height * 0.5f, tube.Width * 0.5f);
            if (radius < 1f) radius = 1f;
            if (tube.Width < 2 || tube.Height < 2) return;

            using (GraphicsPath shadowPath = RoundedRect(
                new RectangleF(tube.X, tube.Y + Math.Max(2f, radius * 0.35f), tube.Width, tube.Height),
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

                RectangleF shadeRect = new RectangleF(tube.X, tube.Y - 1f,
                    Math.Max(1f, tube.Width), tube.Height + 2f);
                using (LinearGradientBrush shade = new LinearGradientBrush(shadeRect,
                    Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
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
                    float lineY = tube.Y + tube.Height * 0.26f;
                    if (tube.Width > radius * 2.4f)
                        g.DrawLine(light, tube.X + radius * 0.7f, lineY,
                            tube.Right - radius * 0.7f, lineY);
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
