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
    /// Breaks the picture of a window that has just been closed into glass
    /// shards that fly apart, spin and fade. Everything runs on its own thread
    /// with its own topmost layered window, so the engine keeps watching the
    /// desktop while the pieces are still in the air.
    /// </summary>
    internal static class ShatterOverlay
    {
        private const double Duration = 0.90;
        private const double Gravity = 900.0;
        private const double Hold = 0.055;

        private sealed class Shard
        {
            public Bitmap Image;
            public Rectangle Bounds;      // where the crop sits on the surface
            public PointF[] Local;        // polygon relative to Bounds
            public float Cx;              // centroid, surface coordinates
            public float Cy;
            public float Vx;
            public float Vy;
            public float Spin;
        }

        /// <summary>
        /// Starts the animation. <paramref name="shot"/> is the window picture
        /// and is owned (and disposed) by the animation thread.
        /// </summary>
        public static void Play(Bitmap shot, Native.RECT frame, Native.POINT impact)
        {
            if (shot == null) return;
            Thread thread = new Thread(delegate()
            {
                try
                {
                    Run(shot, frame, impact);
                }
                catch (Exception ex)
                {
                    Log.Debug("Shatter failed: " + ex.GetType().Name + ": " + ex.Message);
                    try { shot.Dispose(); } catch { }
                }
            });
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Name = "Okna.Shatter";
            thread.Start();
        }

        private static void Run(Bitmap shot, Native.RECT frame, Native.POINT impact)
        {
            // The virtual desktop can start at negative coordinates and can
            // span several monitors; clipping to the primary one would cut the
            // shards of a window on a second screen.
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int screenW = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            int screenH = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (screenW <= 0 || screenH <= 0) { shot.Dispose(); return; }

            // The pieces need room to fly, but only as much as the screen has.
            const int margin = 400;
            int left = Math.Max(vx, frame.Left - margin);
            int top = Math.Max(vy, frame.Top - margin);
            int right = Math.Min(vx + screenW, frame.Right + margin);
            int bottom = Math.Min(vy + screenH, frame.Bottom + margin);
            int w = right - left;
            int h = bottom - top;
            if (w < 40 || h < 40 || w > 8192 || h > 8192) { shot.Dispose(); return; }

            List<Shard> shards = BuildShards(shot, frame, left, top, impact);

            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) { DisposeShards(shards); return; }
            IntPtr dc = IntPtr.Zero;
            IntPtr bmp = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            IntPtr bits = IntPtr.Zero;
            Bitmap canvas = null;
            IntPtr hwnd = IntPtr.Zero;
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

                hwnd = Native.CreateWindowEx(
                    (uint)(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE |
                           Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT),
                    "STATIC", "OknaShatter", Native.WS_POPUP,
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

                double last = 0;
                while (true)
                {
                    double t = clock.Elapsed.TotalSeconds;
                    if (t >= Duration) break;

                    // The picture is already cracked for a blink before the
                    // pieces are released, which is what makes it read as glass
                    // breaking rather than as a picture exploding.
                    double anim = t - Hold;
                    if (anim < 0) anim = 0;

                    double fadeAll = 1.0 - Math.Pow(anim / Duration, 1.4);
                    if (fadeAll < 0) fadeAll = 0;
                    blend.SourceConstantAlpha = (byte)Math.Round(255 * fadeAll);

                    using (Graphics g = Graphics.FromImage(canvas))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.CompositingQuality = CompositingQuality.HighSpeed;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.HighSpeed;

                        if (anim <= 0)
                        {
                            // Still in one piece: the whole picture, with the
                            // crack mesh drawn over it.
                            g.DrawImage(shot, new Rectangle(frame.Left - left, frame.Top - top,
                                shot.Width, shot.Height));
                            using (Pen crack = new Pen(Color.FromArgb(170, 255, 255, 255), 1.2f))
                            {
                                foreach (Shard s in shards)
                                    g.DrawPolygon(crack, Polygon(s));
                            }
                        }
                        else
                        {
                            // The fade is applied by UpdateLayeredWindow's global
                            // alpha, which costs nothing per shard.
                            using (Matrix m = new Matrix())
                            {
                                foreach (Shard s in shards)
                                {
                                    float dx = s.Vx * (float)anim;
                                    float dy = s.Vy * (float)anim + (float)(0.5 * Gravity * anim * anim);
                                    float angle = s.Spin * (float)anim;

                                    m.Reset();
                                    m.RotateAt(angle * 57.29578f, new PointF(s.Cx, s.Cy));
                                    m.Translate(dx, dy);
                                    g.Transform = m;
                                    g.DrawImage(s.Image, s.Bounds);
                                }
                                g.ResetTransform();
                            }
                        }
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

                    Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, dc, ref src, 0,
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
                    int sleep = dt < 0.012 ? 8 : 1;
                    Thread.Sleep(sleep);
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
                DisposeShards(shards);
                Log.Debug("Shatter done frames=" + frames + " elapsed=" +
                    clock.ElapsedMilliseconds + "ms canvas=" + w + "x" + h);
            }
        }

        private static PointF[] Polygon(Shard s)
        {
            return new PointF[]
            {
                new PointF(s.Bounds.X + s.Local[0].X, s.Bounds.Y + s.Local[0].Y),
                new PointF(s.Bounds.X + s.Local[1].X, s.Bounds.Y + s.Local[1].Y),
                new PointF(s.Bounds.X + s.Local[2].X, s.Bounds.Y + s.Local[2].Y),
                new PointF(s.Bounds.X + s.Local[3].X, s.Bounds.Y + s.Local[3].Y)
            };
        }

        private static List<Shard> BuildShards(Bitmap shot, Native.RECT frame, int left, int top,
            Native.POINT impact)
        {
            var list = new List<Shard>();
            int w = shot.Width;
            int h = shot.Height;
            const int cols = 10;
            const int rows = 8;
            float cw = w / (float)cols;
            float ch = h / (float)rows;

            // A jittered mesh: neighbouring pieces share their corners, so the
            // broken picture still tiles perfectly before it starts to move.
            var rnd = new Random();
            PointF[,] v = new PointF[cols + 1, rows + 1];
            for (int i = 0; i <= cols; i++)
            {
                for (int j = 0; j <= rows; j++)
                {
                    float x = i * cw;
                    float y = j * ch;
                    if (i > 0 && i < cols) x += (float)((rnd.NextDouble() - 0.5) * cw * 0.55);
                    if (j > 0 && j < rows) y += (float)((rnd.NextDouble() - 0.5) * ch * 0.55);
                    v[i, j] = new PointF(
                        Math.Max(0, Math.Min(w, x)),
                        Math.Max(0, Math.Min(h, y)));
                }
            }

            float ix = impact.X - frame.Left;
            float iy = impact.Y - frame.Top;
            if (ix < 0 || ix > w || iy < 0 || iy > h)
            {
                ix = w * 0.85f;
                iy = h * 0.15f;
            }

            for (int i = 0; i < cols; i++)
            {
                for (int j = 0; j < rows; j++)
                {
                    PointF[] quad = { v[i, j], v[i + 1, j], v[i + 1, j + 1], v[i, j + 1] };
                    float minX = float.MaxValue, minY = float.MaxValue;
                    float maxX = float.MinValue, maxY = float.MinValue;
                    float cx = 0, cy = 0;
                    foreach (PointF p in quad)
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                        cx += p.X;
                        cy += p.Y;
                    }
                    cx /= 4;
                    cy /= 4;

                    int bx = (int)Math.Floor(minX) - 2;
                    int by = (int)Math.Floor(minY) - 2;
                    int bw = (int)Math.Ceiling(maxX) + 2 - bx;
                    int bh = (int)Math.Ceiling(maxY) + 2 - by;
                    if (bw < 4 || bh < 4) continue;

                    // The piece is baked once: the picture is clipped to the
                    // shard's polygon and the white edge is drawn into the same
                    // bitmap, so a frame is a single rotated blit per shard
                    // instead of a path clip and a stroke.
                    Bitmap crop = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb);
                    PointF[] local = new PointF[4];
                    for (int k = 0; k < 4; k++)
                        local[k] = new PointF(quad[k].X - bx, quad[k].Y - by);

                    using (Graphics cg = Graphics.FromImage(crop))
                    {
                        cg.SmoothingMode = SmoothingMode.AntiAlias;
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddPolygon(local);
                            cg.SetClip(path);

                            int sx0 = Math.Max(0, bx);
                            int sy0 = Math.Max(0, by);
                            int sx1 = Math.Min(w, bx + bw);
                            int sy1 = Math.Min(h, by + bh);
                            if (sx1 > sx0 && sy1 > sy0)
                            {
                                cg.DrawImage(shot,
                                    new Rectangle(sx0 - bx, sy0 - by, sx1 - sx0, sy1 - sy0),
                                    new Rectangle(sx0, sy0, sx1 - sx0, sy1 - sy0),
                                    GraphicsUnit.Pixel);
                            }

                            cg.ResetClip();
                            using (Pen edge = new Pen(Color.FromArgb(150, 255, 255, 255), 1.1f))
                                cg.DrawPath(edge, path);
                        }
                    }

                    // Every piece is pushed away from where the window was hit.
                    float dx = cx - ix;
                    float dy = cy - iy;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1) { dx = 0; dy = 1; len = 1; }
                    float speed = 70 + (float)rnd.NextDouble() * 250;
                    float outward = 0.55f + (float)rnd.NextDouble() * 0.60f;

                    Shard s = new Shard();
                    s.Image = crop;
                    s.Bounds = new Rectangle(bx + frame.Left - left, by + frame.Top - top, bw, bh);
                    s.Local = new PointF[4];
                    for (int k = 0; k < 4; k++)
                        s.Local[k] = new PointF(quad[k].X - bx, quad[k].Y - by);
                    s.Cx = frame.Left - left + cx;
                    s.Cy = frame.Top - top + cy;
                    s.Vx = dx / len * speed * outward + (float)(rnd.NextDouble() - 0.5) * 60;
                    s.Vy = dy / len * speed * outward - 40 - (float)rnd.NextDouble() * 90;
                    s.Spin = (float)(rnd.NextDouble() - 0.5) * 7.0f;
                    list.Add(s);
                }
            }
            return list;
        }

        private static void DisposeShards(List<Shard> shards)
        {
            foreach (Shard s in shards)
            {
                if (s.Image != null) s.Image.Dispose();
            }
            shards.Clear();
        }
    }
}
