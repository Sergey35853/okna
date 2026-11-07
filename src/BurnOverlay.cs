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
    /// Burns the picture of a window that has just been closed: a ragged
    /// front of fire eats it from the bottom edge upwards, leaving a charred
    /// rim, embers and smoke. The real window is already gone; this is a
    /// topmost layered window drawing a copy, so the engine keeps watching
    /// the desktop while the fire works.
    /// </summary>
    internal static class BurnOverlay
    {
        private const double Ignition = 0.10;
        private const double Tail = 0.62;

        private sealed class Ember
        {
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public float Size;
            public double Born;
            public double Life;
        }

        private sealed class Puff
        {
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public float Size;
            public double Born;
            public double Life;
        }

        private sealed class Sprites : IDisposable
        {
            public Bitmap[] Back;
            public Bitmap[] Halo;
            public Bitmap[] Warm;
            public Bitmap[] Core;
            public Bitmap[] Ember;
            public Bitmap[] Smoke;

            public void Dispose()
            {
                DisposeAll(Back);
                DisposeAll(Halo);
                DisposeAll(Warm);
                DisposeAll(Core);
                DisposeAll(Ember);
                DisposeAll(Smoke);
            }

            private static void DisposeAll(Bitmap[] list)
            {
                if (list == null) return;
                for (int i = 0; i < list.Length; i++)
                    if (list[i] != null) list[i].Dispose();
            }
        }

        /// <summary>
        /// Starts the animation. <paramref name="shot"/> is the window picture
        /// and is owned (and disposed) by the animation thread.
        /// </summary>
        public static void Play(Bitmap shot, Native.RECT frame)
        {
            if (shot == null) return;
            Thread thread = new Thread(delegate()
            {
                try
                {
                    Run(shot, frame);
                }
                catch (Exception ex)
                {
                    Log.Debug("Burn failed: " + ex.GetType().Name + ": " + ex.Message);
                    try { shot.Dispose(); } catch { }
                }
            });
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Name = "Okna.Burn";
            thread.Start();
        }

        private static void Run(Bitmap shot, Native.RECT frame)
        {
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int screenW = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            int screenH = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (screenW <= 0 || screenH <= 0) { shot.Dispose(); return; }

            int fw = shot.Width;
            int fh = shot.Height;
            if (fw < 40 || fh < 40) { shot.Dispose(); return; }

            // Fire needs room above the window for smoke and embers.
            const int side = 90;
            const int above = 190;
            const int below = 40;
            int left = Math.Max(vx, frame.Left - side);
            int top = Math.Max(vy, frame.Top - above);
            int right = Math.Min(vx + screenW, frame.Left + fw + side);
            int bottom = Math.Min(vy + screenH, frame.Top + fh + below);
            int w = right - left;
            int h = bottom - top;
            if (w < 40 || h < 40 || w > 8192 || h > 8192 || (long)w * h > 40000000L)
            {
                shot.Dispose();
                return;
            }

            Sprites sprites = CreateSprites();
            Random rnd = new Random();
            float phase0 = (float)(rnd.NextDouble() * 6.2832);
            float phase1 = (float)(rnd.NextDouble() * 6.2832);
            float phase2 = (float)(rnd.NextDouble() * 6.2832);

            List<Ember> embers = new List<Ember>();
            List<Puff> puffs = new List<Puff>();
            double emberDebt = 0;
            double puffDebt = 0;

            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) { sprites.Dispose(); shot.Dispose(); return; }
            IntPtr dc = IntPtr.Zero;
            IntPtr bmp = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            IntPtr bits = IntPtr.Zero;
            Bitmap canvas = null;
            Graphics g = null;
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
                    "STATIC", "OknaBurn", Native.WS_POPUP,
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
                double rise = 1.15 + Math.Min(0.55, fh / 2600.0);
                double duration = Ignition + rise + Tail;
                double fadeFrom = duration - 0.45;

                // Tongues are not laid out in a row of equal cells: their
                // spacing, width, height and lean are all uneven, and the
                // heights follow a slow noise field so they clump into
                // sheets of fire with low flames between them. That is what
                // keeps the front from reading as a row of candles.
                // Large windows need fewer flames: the per-frame GDI+ cost is
                // the main source of stutter, so cap the count well below the
                // old limit.
                int flameCount = Math.Max(10, Math.Min(70, fw / 24));
                float cellW = fw / (float)flameCount;
                float[] flameX = new float[flameCount];
                float[] flameHeight = new float[flameCount];
                float[] flameWidth = new float[flameCount];
                float[] flameLean = new float[flameCount];
                float[] flamePhase = new float[flameCount];
                float[] flameSpeed = new float[flameCount];
                int[] flameVariant = new int[flameCount];
                float flameScale = (float)Math.Max(0.75, Math.Min(1.5, fh / 520.0));
                for (int i = 0; i < flameCount; i++)
                {
                    float x = (i + 0.5f) * cellW + (float)(rnd.NextDouble() - 0.5) * cellW * 0.85f;
                    if (x < 0) x = 0;
                    if (x > fw) x = fw;
                    flameX[i] = x;

                    float field = HeightField(x, phase0, phase1, phase2);
                    flameHeight[i] = (12f + 96f * field) * flameScale *
                        (0.72f + 0.56f * (float)rnd.NextDouble());
                    // Most tongues are narrow; a few are wide, squat sheets
                    // that bridge the gaps and make the base look molten.
                    flameWidth[i] = rnd.NextDouble() < 0.20
                        ? 0.68f + 0.37f * (float)rnd.NextDouble()
                        : 0.30f + 0.28f * (float)rnd.NextDouble();
                    flameLean[i] = (float)(rnd.NextDouble() - 0.5) * 0.30f;
                    flamePhase[i] = (float)(rnd.NextDouble() * 6.2832);
                    flameSpeed[i] = 5.0f + 3.6f * (float)rnd.NextDouble();
                    flameVariant[i] = rnd.Next(3);
                }

                double last = 0;
                // Reuse one Graphics object for every frame instead of creating
                // and throwing away a new one each time: GDI+ allocation is a
                // measurable part of the per-frame cost on large canvases.
                g = Graphics.FromImage(canvas);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                while (true)
                {
                    double t = clock.Elapsed.TotalSeconds;
                    if (t >= duration) break;

                    double dt = t - last;
                    last = t;

                    // The front starts just under the bottom edge and climbs
                    // to a little above the top, so the last sliver of the
                    // picture is really consumed by the fire.
                    double u = (t - Ignition) / rise;
                    if (u < 0) u = 0;
                    if (u > 1) u = 1;
                    double q = u * u * (3 - 2 * u);
                    float frontBase = (float)(fh + 6 - q * (fh + 42));
                    float amp = 9f + 4f * (float)Math.Sin(t * 2.7);
                    int cols = Math.Max(8, fw / 12);
                    if (cols > 400) cols = 400;
                    PointF[] front = new PointF[cols + 1];
                    float stepX = fw / (float)cols;
                    for (int i = 0; i <= cols; i++)
                    {
                        float x = i * stepX;
                        float y = frontBase + amp * (float)(
                            0.55 * Math.Sin(x * 0.019 + phase0 + t * 2.4) +
                            0.30 * Math.Sin(x * 0.043 - phase1 + t * 4.1) +
                            0.15 * Math.Sin(x * 0.007 + phase2 - t * 1.3) +
                            0.22 * Math.Sin(x * 0.095 - phase0 + t * 5.7));
                        front[i] = new PointF(x, y);
                    }

                    // Embers and smoke keep being born while the front is
                    // inside the picture, then die down with it.
                    if (t > Ignition && q < 0.995)
                    {
                        emberDebt += dt * 100.0;
                        int guard = 0;
                        while (emberDebt >= 1 && guard < 10)
                        {
                            emberDebt -= 1;
                            guard++;
                            Ember e = new Ember();
                            e.X = (float)rnd.NextDouble() * fw;
                            float fy = FrontAt(front, e.X);
                            e.Y = Math.Min(fy, fh) - 2;
                            e.Vx = (float)(rnd.NextDouble() - 0.5) * 100;
                            e.Vy = 30 + (float)rnd.NextDouble() * 95;
                            e.Size = 5 + (float)rnd.NextDouble() * 8;
                            e.Born = t;
                            e.Life = 0.5 + rnd.NextDouble() * 0.85;
                            embers.Add(e);
                        }
                        if (embers.Count > 180) embers.RemoveRange(0, embers.Count - 180);

                        puffDebt += dt * 26.0;
                        guard = 0;
                        while (puffDebt >= 1 && guard < 5)
                        {
                            puffDebt -= 1;
                            guard++;
                            Puff p = new Puff();
                            p.X = (float)rnd.NextDouble() * fw;
                            float fy = FrontAt(front, p.X);
                            p.Y = Math.Min(fy, fh) - 4;
                            p.Vx = (float)(rnd.NextDouble() - 0.5) * 60;
                            p.Vy = 52 + (float)rnd.NextDouble() * 74;
                            p.Size = 38 + (float)rnd.NextDouble() * 40;
                            p.Born = t;
                            p.Life = 1.3 + rnd.NextDouble() * 1.1;
                            puffs.Add(p);
                        }
                        if (puffs.Count > 90) puffs.RemoveRange(0, puffs.Count - 90);
                    }

                    double fade = 1.0;
                    if (t > fadeFrom) fade = Math.Max(0, 1 - (t - fadeFrom) / (duration - fadeFrom));
                    blend.SourceConstantAlpha = (byte)Math.Round(255 * fade);

                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;

                    if (frontBase < fh + 8)
                        {
                            using (GraphicsPath path = RemainingPath(front, fw, originX, originY))
                            {
                                g.SetClip(path);

                                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                                g.DrawImageUnscaled(shot, (int)originX, (int)originY);
                                g.InterpolationMode = InterpolationMode.HighQualityBilinear;

                                // Scorch above the fire: the paper browns
                                // before it chars. It reaches down to the
                                // front so no clean strip is left between
                                // the tint and the crust.
                                RectangleF scorchRect = new RectangleF(originX,
                                    originY + frontBase - 104, fw, 114);
                                ColorBlend scorchBlend = new ColorBlend(4);
                                scorchBlend.Colors = new Color[]
                                {
                                    Color.FromArgb(0, 84, 46, 18),
                                    Color.FromArgb(20, 76, 38, 14),
                                    Color.FromArgb(44, 58, 28, 11),
                                    Color.FromArgb(70, 40, 20, 8)
                                };
                                scorchBlend.Positions = new float[] { 0f, 0.45f, 0.80f, 1f };
                                using (LinearGradientBrush scorch = new LinearGradientBrush(scorchRect,
                                    Color.Black, Color.Black, LinearGradientMode.Vertical))
                                {
                                    scorch.InterpolationColors = scorchBlend;
                                    g.FillRectangle(scorch, scorchRect);
                                }

                                // Charred rim: a ragged crust that follows
                                // the front. It is nearly black at the fire
                                // and fades into brown further up, with a
                                // few glowing cracks.
                                PointF[] charTop = new PointF[front.Length];
                                PointF[] charBottom = new PointF[front.Length];
                                float deepest = 0;
                                for (int i = 0; i < front.Length; i++)
                                {
                                    float x = front[i].X;
                                    float depth = 5f
                                        + 7f * (0.5f + 0.5f * (float)Math.Sin(x * 0.026 + phase1 * 1.7))
                                        + 4f * (0.5f + 0.5f * (float)Math.Sin(x * 0.083 + phase2 + t * 1.1))
                                        + 2.5f * (0.5f + 0.5f * (float)Math.Sin(x * 0.29 + t * 3.7))
                                        + 1.5f * (0.5f + 0.5f * (float)Math.Sin(x * 0.71 - t * 5.9));
                                    charTop[i] = new PointF(originX + x, originY + front[i].Y - depth);
                                    charBottom[i] = new PointF(originX + x, originY + front[i].Y);
                                    if (depth > deepest) deepest = depth;
                                }
                                float charTopY = originY + frontBase - deepest;
                                using (GraphicsPath charPath = BandPath(charTop, charBottom))
                                {
                                    ColorBlend charBlend = new ColorBlend(4);
                                    charBlend.Colors = new Color[]
                                    {
                                        Color.FromArgb(0, 40, 24, 13),
                                        Color.FromArgb(100, 30, 16, 9),
                                        Color.FromArgb(200, 12, 7, 4),
                                        Color.FromArgb(240, 4, 3, 2)
                                    };
                                    charBlend.Positions = new float[] { 0f, 0.45f, 0.82f, 1f };
                                    using (LinearGradientBrush charBrush = new LinearGradientBrush(
                                        new RectangleF(originX, charTopY, fw, deepest + 8),
                                        Color.Black, Color.Black, LinearGradientMode.Vertical))
                                    {
                                        charBrush.InterpolationColors = charBlend;
                                        g.FillPath(charBrush, charPath);
                                    }
                                }

                                // A sheet of heat and low flame right at
                                // the edge: it unifies the separate tongues
                                // into one wall of fire.
                                PointF[] heatTop = new PointF[front.Length];
                                PointF[] heatBottom = new PointF[front.Length];
                                for (int i = 0; i < front.Length; i++)
                                {
                                    float d = 20f + 12f * (0.5f + 0.5f * (float)Math.Sin(
                                        front[i].X * 0.041 + t * 5.3 + phase0));
                                    heatTop[i] = new PointF(originX + front[i].X,
                                        originY + front[i].Y - d);
                                    heatBottom[i] = new PointF(originX + front[i].X,
                                        originY + front[i].Y);
                                }
                                using (GraphicsPath heatPath = BandPath(heatTop, heatBottom))
                                using (LinearGradientBrush heat = new LinearGradientBrush(
                                    new RectangleF(originX, originY + frontBase - 36, fw, 44),
                                    Color.FromArgb(0, 255, 150, 34),
                                    Color.FromArgb(96, 255, 102, 14),
                                    LinearGradientMode.Vertical))
                                {
                                    g.FillPath(heat, heatPath);
                                }

                                // Glowing cracks in the crust: short bright
                                // slivers that wink in and out.
                                int cracks = Math.Max(4, (int)(fw / 34));
                                for (int i = 0; i < cracks; i++)
                                {
                                    float x = (i + 0.5f) * fw / cracks + (float)Math.Sin(
                                        i * 2.3 + phase0) * 9f;
                                    float y = FrontAt(front, x);
                                    float depth = 5f + 8f * (0.5f + 0.5f * (float)Math.Sin(
                                        t * 3.7 + i * 1.9 + phase1));
                                    int a = (int)(150 * (0.45f + 0.55f * (float)Math.Sin(
                                        t * 5.1 + i * 2.7)));
                                    if (a < 20) a = 20;
                                    using (Pen crack = new Pen(Color.FromArgb(a, 255, 132, 34), 1.3f))
                                    {
                                        g.DrawLine(crack, originX + x, originY + y,
                                            originX + x + 1.5f, originY + y - depth);
                                    }
                                }

                                g.ResetClip();
                            }
                        }

                        // Smoke drifts up, grows and thins out.
                        for (int i = 0; i < puffs.Count; i++)
                        {
                            Puff p = puffs[i];
                            double age = t - p.Born;
                            if (age < 0 || age > p.Life) continue;
                            float f = (float)(age / p.Life);
                            float px = p.X + p.Vx * (float)age;
                            float py = p.Y - p.Vy * (float)age - 14f * (float)(age * age);
                            float sz = p.Size * (1.0f + 2.4f * f);
                            int which = f < 0.22f ? 0 : (f < 0.45f ? 1 : (f < 0.72f ? 2 : 3));
                            g.DrawImage(sprites.Smoke[which],
                                new RectangleF(originX + px - sz * 0.5f, originY + py - sz * 0.5f, sz, sz));
                        }

                        float intensity = (float)Math.Min(1.0, (t - 0.01) / 0.20);
                        if (t > Ignition + rise)
                            intensity *= (float)Math.Max(0, 1 - (t - Ignition - rise) / 0.42);

                        if (intensity > 0.01f)
                        {
                            // Four passes keep the bright cores on top of the
                            // warm haloes instead of being painted over by the
                            // next flame's glow.
                            for (int pass = 0; pass < 4; pass++)
                            {
                                for (int i = 0; i < flameCount; i++)
                                {
                                    float x = flameX[i];
                                    float baseY = Math.Min(FrontAt(front, x), fh + 2);
                                    if (baseY > fh + 12 || baseY < -50) continue;

                                    float flick = 0.5f + 0.5f * (float)Math.Sin(
                                        t * flameSpeed[i] + flamePhase[i]);
                                    float hgt = flameHeight[i] * (0.60f + 0.40f * flick) * intensity;
                                    if (hgt < 5) continue;
                                    if (pass == 0 && (i & 1) == 1) continue;

                                    float sway = (float)Math.Sin(t * 4.6 + flamePhase[i] * 1.7) *
                                        (2f + hgt * 0.06f);
                                    float cx = originX + x + sway;
                                    float by = originY + baseY;

                                    Bitmap sprite;
                                    float sw;
                                    float sh;
                                    if (pass == 0)
                                    {
                                        sprite = sprites.Back[flameVariant[i]];
                                        sh = hgt * 1.26f;
                                        sw = hgt * flameWidth[i] * 1.55f;
                                        cx -= 5f;
                                    }
                                    else if (pass == 1)
                                    {
                                        sprite = sprites.Halo[flameVariant[i]];
                                        sh = hgt * 1.12f;
                                        sw = hgt * flameWidth[i] * 1.24f;
                                    }
                                    else if (pass == 2)
                                    {
                                        sprite = sprites.Warm[flameVariant[i]];
                                        sh = hgt;
                                        sw = hgt * flameWidth[i];
                                    }
                                    else
                                    {
                                        sprite = sprites.Core[flameVariant[i]];
                                        sh = hgt * 0.50f;
                                        sw = hgt * flameWidth[i] * 0.46f;
                                    }

                                    using (Matrix m = new Matrix())
                                    {
                                        m.Translate(cx, by);
                                        m.Shear(flameLean[i] * (0.55f + 0.45f * flick), 0);
                                        g.Transform = m;
                                        g.DrawImage(sprite, -sw * 0.5f, -sh, sw, sh);
                                        g.ResetTransform();
                                    }

                                    // A small tongue in the gap between the
                                    // big ones keeps the base a continuous
                                    // flickering band instead of a row of
                                    // separate candles.
                                    if (pass == 1 || pass == 2)
                                    {
                                        float x2 = x + cellW * (0.34f + 0.34f * (float)Math.Sin(
                                            i * 2.1 + flamePhase[i]));
                                        float base2 = Math.Min(FrontAt(front, x2), fh + 2);
                                        if (base2 <= fh + 12 && base2 > -50)
                                        {
                                            float flick2 = 0.5f + 0.5f * (float)Math.Sin(
                                                t * 9.1 + flamePhase[i] * 2.7 + 1.3);
                                            float h2 = hgt * (0.22f + 0.34f * flick2);
                                            if (h2 > 4)
                                            {
                                                float sway2 = (float)Math.Sin(t * 6.4 + i * 2.3) * 3f;
                                                float c2 = originX + x2 + sway2;
                                                float w2 = h2 * (0.44f + 0.34f * flameWidth[i]);
                                                using (Matrix m2 = new Matrix())
                                                {
                                                    m2.Translate(c2, originY + base2);
                                                    m2.Shear(flameLean[i] * 0.6f, 0);
                                                    g.Transform = m2;
                                                    if (pass == 1)
                                                        g.DrawImage(sprites.Halo[flameVariant[i]], -w2 * 0.5f, -h2 * 1.05f,
                                                            w2, h2 * 1.05f);
                                                    else
                                                        g.DrawImage(sprites.Warm[flameVariant[i]], -w2 * 0.34f, -h2,
                                                            w2 * 0.68f, h2);
                                                    g.ResetTransform();
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (frontBase < fh + 12)
                            DrawFront(g, front, originX, originY, t);

                        // Embers: bright specks that cool from yellow to red.
                        for (int i = 0; i < embers.Count; i++)
                        {
                            Ember e = embers[i];
                            double age = t - e.Born;
                            if (age < 0 || age > e.Life) continue;
                            float f = (float)(age / e.Life);
                            float ex = e.X + e.Vx * (float)age;
                            float ey = e.Y - e.Vy * (float)age + 48f * (float)(age * age);
                            float sz = e.Size * (1.0f - 0.45f * f);
                            int which = f < 0.32f ? 0 : (f < 0.66f ? 1 : 2);
                            g.DrawImage(sprites.Ember[which],
                                new RectangleF(originX + ex - sz * 0.5f, originY + ey - sz * 0.5f, sz, sz));
                        }

                        BitmapData data = canvas.LockBits(new Rectangle(0, 0, w, h),
                            ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                        try
                        {
                            int rowBytes = w * 4;
                            // A single block copy is far faster than one CopyMemory
                            // call per scanline; fall back row by row only if the
                            // DIB stride is padded and does not match the bitmap row.
                            if (data.Stride == rowBytes)
                            {
                                Native.CopyMemory(bits, data.Scan0,
                                    (IntPtr)(rowBytes * h));
                            }
                            else
                            {
                                for (int y = 0; y < h; y++)
                                {
                                    Native.CopyMemory(IntPtr.Add(bits, y * rowBytes),
                                        IntPtr.Add(data.Scan0, y * data.Stride),
                                        (IntPtr)rowBytes);
                                }
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

                        // Cap the frame rate (~40 fps): the old code ran as fast as
                        // possible (sleep 1 ms), which burned CPU on large canvases
                        // and caused visible stutters. The animation still looks
                        // smooth at this rate.
                        Thread.Sleep(25);
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
                if (g != null) g.Dispose();
                if (canvas != null) canvas.Dispose();
                if (shot != null) shot.Dispose();
                sprites.Dispose();
                Log.Debug("Burn done frames=" + frames + " elapsed=" +
                    clock.ElapsedMilliseconds + "ms canvas=" + w + "x" + h);
            }
        }

        /// <summary>
        /// The part of the picture the fire has not eaten yet: everything
        /// above the ragged burn line.
        /// </summary>
        private static GraphicsPath RemainingPath(PointF[] front, float fw, float ox, float oy)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddLine(ox, oy, ox, oy + front[0].Y);
            PointF[] points = new PointF[front.Length];
            for (int i = 0; i < front.Length; i++)
                points[i] = new PointF(ox + front[i].X, oy + front[i].Y);
            path.AddLines(points);
            path.AddLine(ox + fw, oy + front[front.Length - 1].Y, ox + fw, oy);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// The burning edge itself: a thick, glowing band whose top follows
        /// a ragged line, so the fire has a molten base instead of a thin
        /// drawn string.
        /// </summary>
        private static void DrawFront(Graphics g, PointF[] front, float ox, float oy, double t)
        {
            PointF[] points = new PointF[front.Length];
            for (int i = 0; i < front.Length; i++)
                points[i] = new PointF(ox + front[i].X, oy + front[i].Y);

            float pulse = 0.85f + 0.15f * (float)Math.Sin(t * 11.0);
            int segs = 36;
            using (Pen glow = new Pen(Color.FromArgb(36, 255, 74, 6), 13f))
            using (Pen hot = new Pen(Color.FromArgb(96, 255, 124, 18), 5.5f))
            using (Pen core = new Pen(Color.FromArgb(180, 255, 244, 200), 1.8f))
            {
                glow.LineJoin = hot.LineJoin = core.LineJoin = LineJoin.Round;
                glow.StartCap = glow.EndCap = LineCap.Round;
                hot.StartCap = hot.EndCap = LineCap.Round;
                core.StartCap = core.EndCap = LineCap.Round;
                for (int s = 0; s < segs; s++)
                {
                    int a = s * (points.Length - 1) / segs;
                    int b = (s + 1) * (points.Length - 1) / segs;
                    if (b <= a) continue;
                    PointF[] part = new PointF[b - a + 1];
                    Array.Copy(points, a, part, 0, part.Length);

                    // The edge is not equally hot all along: some stretches
                    // glow white, others are nearly out.
                    float mod = 0.35f + 0.65f * (float)Math.Sin(s * 1.9 + t * 6.7);
                    if (mod < 0.08f) mod = 0.08f;
                    glow.Color = Color.FromArgb((int)(40 * mod * pulse), 255, 74, 6);
                    hot.Color = Color.FromArgb((int)(125 * mod * pulse), 255, 124, 18);
                    core.Color = Color.FromArgb((int)(215 * mod * pulse), 255, 244, 200);
                    g.DrawLines(glow, part);
                    g.DrawLines(hot, part);
                    g.DrawLines(core, part);
                }
            }

            // The molten strip: a thin filled band, hottest at the edge.
            PointF[] top = new PointF[front.Length];
            for (int i = 0; i < front.Length; i++)
            {
                float d = 2.5f + 5.0f * (0.5f + 0.5f * (float)Math.Sin(
                    front[i].X * 0.07 + t * 6.1));
                top[i] = new PointF(ox + front[i].X, oy + front[i].Y - d);
            }
            using (GraphicsPath band = BandPath(top, points))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(195, 255, 148, 40)))
            {
                g.FillPath(brush, band);
            }
        }

        /// <summary>
        /// A band between two lines that share the same x positions, both in
        /// canvas coordinates: the top line first, the bottom line reversed.
        /// </summary>
        private static GraphicsPath BandPath(PointF[] top, PointF[] bottom)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddLines(top);
            PointF[] lower = new PointF[bottom.Length];
            for (int i = 0; i < bottom.Length; i++)
                lower[i] = bottom[bottom.Length - 1 - i];
            path.AddLines(lower);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Slow, uneven height of the fire along the window, made of three
        /// waves so neighbouring tongues rise and fall in clumps.
        /// </summary>
        private static float HeightField(float x, float p0, float p1, float p2)
        {
            float v = 0.42f
                + 0.30f * (float)Math.Sin(x * 0.011 + p0)
                + 0.18f * (float)Math.Sin(x * 0.033 + p1)
                + 0.10f * (float)Math.Sin(x * 0.071 + p2);
            if (v < 0.10f) v = 0.10f;
            if (v > 1f) v = 1f;
            return v;
        }

        private static float FrontAt(PointF[] front, float x)
        {
            if (front.Length < 2) return 0;
            float fw = front[front.Length - 1].X;
            if (fw <= 0) return front[0].Y;
            float pos = x / fw * (front.Length - 1);
            int i = (int)pos;
            if (i < 0) i = 0;
            if (i > front.Length - 2) i = front.Length - 2;
            float f = pos - i;
            return front[i].Y + (front[i + 1].Y - front[i].Y) * f;
        }

        private static Sprites CreateSprites()
        {
            Sprites s = new Sprites();
            s.Back = new Bitmap[3];
            s.Halo = new Bitmap[3];
            s.Warm = new Bitmap[3];
            s.Core = new Bitmap[3];
            for (int v = 0; v < 3; v++)
            {
                s.Back[v] = Flame(96, 192, Color.FromArgb(110, 255, 58, 4),
                    Color.FromArgb(0, 160, 10, 0), 0.20f, 0.12f, v);
                s.Halo[v] = Flame(96, 192, Color.FromArgb(180, 255, 86, 8),
                    Color.FromArgb(0, 190, 20, 0), 0.16f, 0.10f, v);
                s.Warm[v] = Flame(96, 192, Color.FromArgb(235, 255, 150, 30),
                    Color.FromArgb(0, 255, 64, 6), 0.16f, 0.10f, v);
                s.Core[v] = Flame(72, 144, Color.FromArgb(255, 255, 244, 196),
                    Color.FromArgb(0, 255, 196, 48), 0.18f, 0.12f, v);
            }

            s.Ember = new Bitmap[3];
            s.Ember[0] = Glow(24, Color.FromArgb(255, 255, 242, 190), Color.FromArgb(0, 255, 140, 30), 0.25f);
            s.Ember[1] = Glow(24, Color.FromArgb(220, 255, 186, 84), Color.FromArgb(0, 255, 92, 10), 0.25f);
            s.Ember[2] = Glow(24, Color.FromArgb(150, 240, 92, 30), Color.FromArgb(0, 160, 30, 5), 0.25f);

            int[] alphas = new int[] { 58, 48, 38, 28 };
            s.Smoke = new Bitmap[4];
            for (int i = 0; i < alphas.Length; i++)
                s.Smoke[i] = Glow(96, Color.FromArgb(alphas[i], 150, 144, 136),
                    Color.FromArgb(0, 120, 114, 106), 0.1f);
            return s;
        }

        private static Bitmap Glow(int size, Color center, Color edge, float focus)
        {
            Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(1, 1, size - 2, size - 2);
                    using (PathGradientBrush brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = center;
                        brush.SurroundColors = new Color[] { edge };
                        brush.FocusScales = new PointF(focus, focus);
                        g.FillPath(brush, path);
                    }
                }
            }
            return b;
        }

        private static Bitmap Flame(int w, int h, Color center, Color edge,
            float focusX, float focusY, int variant)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = new GraphicsPath())
                {
                    float cx = w * 0.5f;
                    // Each variant leans and bulges differently, so a row of
                    // tongues does not look like one stamp repeated.
                    float tip = w * (0.42f + 0.08f * variant);
                    float left = w * (0.04f + 0.02f * ((variant + 1) % 3));
                    float right = w * (0.90f + 0.02f * variant);
                    float waist = h * (0.44f + 0.05f * variant);
                    path.StartFigure();
                    path.AddBezier(cx, h - 3, left, h * 0.80f, w * 0.10f, waist, tip, 2);
                    path.AddBezier(tip, 2, right, waist, w * 0.90f, h * 0.80f, cx, h - 3);
                    path.CloseFigure();
                    using (PathGradientBrush brush = new PathGradientBrush(path))
                    {
                        brush.CenterPoint = new PointF(cx, h * 0.78f);
                        brush.CenterColor = center;
                        brush.SurroundColors = new Color[] { edge };
                        brush.FocusScales = new PointF(focusX, focusY);
                        g.FillPath(brush, path);
                    }
                }
            }
            return b;
        }
    }
}
