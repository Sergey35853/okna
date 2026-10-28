using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Okna
{
    /// <summary>
    /// Draws a bent copy of a window in a topmost layered window. The real window
    /// stays on screen, exactly under the copy (Graphics Capture and PrintWindow
    /// keep returning fresh frames that way), and its content is refreshed from
    /// a background thread. Windows itself cannot bend a foreign window.
    /// </summary>
    internal sealed class BendOverlay : IDisposable
    {
        private const int Margin = 80;
        private const int StripTarget = 110;

        private static readonly string _dumpDir = Environment.GetEnvironmentVariable("OKNA_DUMP");
        private int _dumpIndex;

        private IntPtr _hwnd;
        private IntPtr _memDc;
        private IntPtr _memBmp;
        private IntPtr _memOld;
        private IntPtr _memBits;
        private IntPtr _srcDc;
        private IntPtr _srcBmp;
        private IntPtr _srcOld;
        private IntPtr _srcBits;
        private IntPtr _tmpDc;
        private IntPtr _tmpBmp;
        private IntPtr _tmpOld;
        private IntPtr _tmpBits;
        private IntPtr _liveDc;
        private IntPtr _liveBmp;
        private IntPtr _liveOld;
        private IntPtr _liveBits;
        private readonly object _liveLock = new object();
        private Thread _liveThread;
        private volatile bool _liveRunning;
        private bool _liveReady;
        private IntPtr _liveHwnd;
        private bool _liveCapable;
        private bool _wgcCapable;
        private byte[] _baseFrame;
        private readonly int[] _srcRows = new int[StripTarget + 2];
        private readonly int[] _dstRows = new int[StripTarget + 2];
        private readonly int[] _dxStrips = new int[StripTarget + 2];
        private IntPtr _bandBuf;
        private int _w;
        private int _h;
        private int _ow;
        private int _oh;
        private int _capDy;
        // Where the captured bitmap sits relative to the window rectangle, and
        // where the visible window sits inside that bitmap. PrintWindow draws
        // the whole rectangle, invisible resize border included, so the two
        // differ there; the border has to be kept out of the copy.
        private int _dstDx;
        private int _dstDy;
        private int _inDx;
        private int _inDy;
        private int _cw;
        private int _ch;
        private bool _visible;
        private double _lastDark;
        private double _lastAvg;

        public bool IsVisible
        {
            get { return _visible; }
        }

        public bool LiveCapable
        {
            get { return _liveCapable && _liveDc != IntPtr.Zero; }
        }

        /// <summary>
        /// True when the window can only be captured through Windows Graphics
        /// Capture (Explorer). Such a window has to stay on screen, under the
        /// overlay, or the compositor stops refreshing it.
        /// </summary>
        public bool WgcCapable
        {
            get { return _wgcCapable; }
        }

        public bool Capture(IntPtr target, Native.RECT rect)
        {
            // Explorer's custom title bar/content is not rendered by PrintWindow:
            // it returns a dark, empty client area. Grab it from the screen while
            // it is still visible (the same fallback covers a few GPU windows).
            string cls = Native.GetWindowClass(target);
            bool screenFirst = cls == "CabinetWClass" || cls == "ExploreWClass";

            // GetWindowRect includes the invisible resize border (about seven
            // pixels on every side on Windows 10/11). The DWM extended frame is
            // the part of the window a user actually sees.
            Native.RECT cap = rect;
            int capDx = 0;      // visible frame relative to the window rectangle
            int capDy = 0;
            int dstDx = 0;      // captured bitmap relative to the window rectangle
            int dstDy = 0;
            int inDx = 0;       // visible frame inside the captured bitmap
            int inDy = 0;
            int cw = rect.Width;
            int ch = rect.Height;
            Native.RECT ext;
            if (Native.DwmGetWindowAttribute(target, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out ext,
                    Marshal.SizeOf(typeof(Native.RECT))) == 0 &&
                ext.Width > 0 && ext.Height > 0)
            {
                capDx = ext.Left - rect.Left;
                capDy = ext.Top - rect.Top;
                cw = ext.Width;
                ch = ext.Height;
                if (screenFirst)
                {
                    // A screen grab of the whole rectangle would also catch the
                    // desktop around the visible frame, so grab just the frame.
                    cap = ext;
                    dstDx = capDx;
                    dstDy = capDy;
                }
                else
                {
                    // PrintWindow paints the invisible resize border with the
                    // window's frame colour. It is not part of the window the
                    // user sees, so it is left clear by MaskBorders below;
                    // otherwise it reads as a light band around the copy while
                    // the window is dragged (very visible in browsers).
                    inDx = capDx;
                    inDy = capDy;
                }
            }

            int w = cap.Width;
            int h = cap.Height;
            if (w < 80 || h < 40 || w > 4096 || h > 4096) return false;
            if (!EnsureWindow()) return false;
            if (!EnsureBuffers(w, h)) return false;
            _capDy = capDy;
            _dstDx = dstDx;
            _dstDy = dstDy;
            _inDx = inDx;
            _inDy = inDy;
            _cw = cw;
            _ch = ch;

            bool ok = false;
            bool good = false;
            bool printGood = false;
            _liveCapable = false;
            if (!screenFirst)
            {
                // PrintWindow renders the window's own content, even if it is
                // partly covered. A 32bpp DIB filled with WHITENESS keeps alpha
                // at 255.
                Native.PatBlt(_srcDc, 0, 0, w, h, Native.WHITENESS);
                ok = Native.PrintWindow(target, _srcDc, Native.PW_RENDERFULLCONTENT);
                good = ok && Analyze(false, false);
                printGood = good;
                Log.Debug("Capture PrintWindow ok=" + ok + " good=" + good +
                    " dark=" + _lastDark.ToString("f2") + " avg=" + _lastAvg.ToString("f0"));
            }
            if (!good)
            {
                // Some GPU windows return black from PrintWindow. Fall back to a
                // screen grab of the window rectangle (it is visible right now).
                Native.PatBlt(_srcDc, 0, 0, w, h, Native.BLACKNESS);
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                if (screen != IntPtr.Zero)
                {
                    Native.BitBlt(_srcDc, 0, 0, w, h, screen, cap.Left, cap.Top, Native.SRCCOPY);
                    Native.ReleaseDC(IntPtr.Zero, screen);
                }
                good = Analyze(true, true);
                Log.Debug("Capture screen grab good=" + good +
                    " dark=" + _lastDark.ToString("f2") + " avg=" + _lastAvg.ToString("f0"));
            }
            // A screen grab is a one-off snapshot; PrintWindow can be repeated
            // while the window is on screen, so only it is live.
            _wgcCapable = screenFirst && good;
            _liveCapable = printGood || _wgcCapable;

            // Keep the opaque snapshot: Graphics Capture renders Windows 11's
            // Mica backdrop (Explorer's title bar) as transparent pixels, and
            // the snapshot is the only place where that backdrop was captured.
            if (_wgcCapable)
            {
                int bytes = w * h * 4;
                _baseFrame = new byte[bytes];
                Marshal.Copy(_srcBits, _baseFrame, 0, bytes);
            }
            else
            {
                _baseFrame = null;
            }
            MaskBorders(_srcBits);
            return good;
        }

        /// <summary>
        /// Clears the part of a captured bitmap that lies outside the window's
        /// visible frame. PrintWindow paints the invisible resize border (about
        /// seven pixels on each side) with the window's frame colour; drawn as
        /// part of the bent copy it looked like a light band along the edges of
        /// the window, most noticeable in browsers.
        /// </summary>
        private void MaskBorders(IntPtr bits)
        {
            if (bits == IntPtr.Zero || _w <= 0 || _h <= 0) return;
            int left = _inDx;
            int right = _w - (_inDx + _cw);
            int top = _inDy;
            int bottom = _h - (_inDy + _ch);
            if (left <= 0 && right <= 0 && top <= 0 && bottom <= 0) return;

            int rowBytes = _w * 4;
            for (int y = 0; y < _h; y++)
            {
                IntPtr row = IntPtr.Add(bits, y * rowBytes);
                if (y < top || y >= _h - bottom)
                {
                    Native.ZeroMemory(row, (IntPtr)rowBytes);
                    continue;
                }
                if (left > 0) Native.ZeroMemory(row, (IntPtr)(left * 4));
                if (right > 0) Native.ZeroMemory(IntPtr.Add(row, (_w - right) * 4), (IntPtr)(right * 4));
            }
        }

        /// <summary>
        /// Fills the transparent parts of the warped copy inside the window
        /// rectangle with the colour of the strip's own edge pixel. Only needed
        /// when the real window is still on screen under the copy: otherwise the
        /// desktop shows through those wedges, which is what a bent sheet does.
        /// </summary>
        private void FillVacatedBands(int count)
        {
            int rowBytes = _ow * 4;
            for (int i = 0; i < count; i++)
            {
                int dx = _dxStrips[i];
                if (dx == 0) continue;

                int bandX, width;
                if (dx > 0)
                {
                    bandX = Margin;
                    width = dx;
                }
                else
                {
                    bandX = Margin + _w + dx;
                    width = -dx;
                }
                if (width <= 0 || width > Margin) continue;

                int y0 = Math.Max(Margin, _dstRows[i]);
                int y1 = Math.Min(Margin + _h, _dstRows[i + 1]);
                if (y1 <= y0) continue;

                // One colour per strip is enough: the outermost column of a
                // window is its frame, which does not change down a strip. Skip
                // the near-black non-client pixels some windows leave there.
                int midRow = (y0 + y1) / 2;
                int color = 0;
                for (int k = 0; k < 16; k++)
                {
                    int sx = dx > 0 ? Margin + dx + k : Margin + _w + dx - 1 - k;
                    if (sx < Margin || sx >= Margin + _w) break;
                    color = Marshal.ReadInt32(_memBits, midRow * rowBytes + sx * 4);
                    int lum = (color & 0xFF) + ((color >> 8) & 0xFF) + ((color >> 16) & 0xFF);
                    if (lum > 24) break;
                }
                FillBuffer(_bandBuf, width, color);
                for (int row = y0; row < y1; row++)
                {
                    Native.CopyMemory(IntPtr.Add(_memBits, row * rowBytes + bandX * 4),
                        _bandBuf, (IntPtr)(width * 4));
                }
            }

            // The vertical pass can also leave a gap above the first row and
            // below the last one; repeat the nearest row into it.
            int top = Math.Max(Margin, _dstRows[0]);
            for (int row = Margin; row < top; row++)
                Native.CopyMemory(IntPtr.Add(_memBits, row * rowBytes + Margin * 4),
                    IntPtr.Add(_memBits, top * rowBytes + Margin * 4), (IntPtr)(_w * 4));

            int bottom = Math.Min(Margin + _h, _dstRows[count]);
            for (int row = bottom; row < Margin + _h; row++)
                Native.CopyMemory(IntPtr.Add(_memBits, row * rowBytes + Margin * 4),
                    IntPtr.Add(_memBits, (bottom - 1) * rowBytes + Margin * 4), (IntPtr)(_w * 4));
        }

        /// <summary>Fills a small native buffer with one repeated 32bpp pixel.</summary>
        private static void FillBuffer(IntPtr buffer, int pixels, int color)
        {
            Marshal.WriteInt32(buffer, color);
            int done = 1;
            while (done < pixels)
            {
                int n = Math.Min(done, pixels - done);
                Native.CopyMemory(IntPtr.Add(buffer, done * 4), buffer, (IntPtr)(n * 4));
                done += n;
            }
        }

        public void Show()
        {
            if (_hwnd == IntPtr.Zero) return;
            Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
            // The overlay is created with WS_EX_TOPMOST (the only way the flag
            // sticks here); re-assert it so it sits above the dragged window.
            Native.SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            _visible = true;
        }

        public void Hide()
        {
            StopLive();
            if (_hwnd != IntPtr.Zero)
                Native.ShowWindow(_hwnd, Native.SW_HIDE);
            _visible = false;
            // Free the big DIBs between drags; Capture recreates them.
            ReleaseBuffers();
        }

        public void Render(int x, int y, double bendX, double bendY, int grabX, int grabY, bool opaqueFill)
        {
            if (_hwnd == IntPtr.Zero || _memDc == IntPtr.Zero) return;

            CommitLive();

            // Grab point relative to the captured image (the DWM extended frame
            // for Explorer), not to the larger GetWindowRect rectangle.
            int gy = grabY - _capDy;

            Native.PatBlt(_memDc, 0, 0, _ow, _oh, Native.BLACKNESS);
            Native.PatBlt(_tmpDc, 0, 0, _ow, _oh, Native.BLACKNESS);

            int strips = Math.Min(StripTarget, Math.Max(8, _h / 4));
            int sh = Math.Max(2, (_h + strips - 1) / strips);

            // Pass 1: horizontal inertia. Full-width strips are shifted sideways
            // by an amount that grows with the distance from the grabbed point.
            for (int sy = 0; sy < _h; sy += sh)
            {
                int hh = Math.Min(sh, _h - sy);
                double t = (sy + hh * 0.5 - gy) / (double)_h;
                int dx = (int)Math.Round(bendX * t * t * t);
                if (dx > Margin - 2) dx = Margin - 2;
                else if (dx < -(Margin - 2)) dx = -(Margin - 2);
                _dxStrips[sy / sh] = dx;
                Native.BitBlt(_tmpDc, Margin + dx, Margin + sy, _w, hh, _srcDc, 0, sy, Native.SRCCOPY);
            }

            // Pass 2: vertical inertia. The grabbed edge follows the cursor while
            // the far edge lags behind. Rows are stretched to the next boundary so
            // the window compresses or stretches like a sheet instead of tearing.
            int count = (_h + sh - 1) / sh;
            if (count + 1 > _srcRows.Length) count = _srcRows.Length - 1;
            for (int i = 0; i <= count; i++)
            {
                int sy = Math.Min(_h, i * sh);
                _srcRows[i] = sy;
                double t = (sy - gy) / (double)_h;
                int dy = (int)Math.Round(bendY * t * t * t);
                if (dy > Margin - 2) dy = Margin - 2;
                else if (dy < -(Margin - 2)) dy = -(Margin - 2);
                _dstRows[i] = Margin + sy + dy;
            }
            for (int i = 0; i < count; i++)
            {
                int srcH = _srcRows[i + 1] - _srcRows[i];
                int dstH = _dstRows[i + 1] - _dstRows[i];
                if (srcH <= 0 || dstH <= 0) continue;
                // Copy row by row instead of StretchBlt: GDI's stretch drops the
                // alpha channel, which made the bent part invisible.
                for (int j = 0; j < dstH; j++)
                {
                    int srcRow = _srcRows[i] + (j * srcH) / dstH;
                    if (srcRow >= _srcRows[i + 1]) srcRow = _srcRows[i + 1] - 1;
                    int dstRow = _dstRows[i] + j;
                    if (dstRow < 0 || dstRow >= _oh) continue;
                    int dx = _dxStrips[i];
                    Native.CopyMemory(
                        IntPtr.Add(_memBits, (dstRow * _ow + Margin + dx) * 4),
                        IntPtr.Add(_tmpBits, ((Margin + srcRow) * _ow + Margin + dx) * 4),
                        (IntPtr)(_w * 4));
                }
            }

            // When the real window has to stay visible under the copy (Graphics
            // Capture stops refreshing a hidden window), the warp's vacated bands
            // would let that straight window show through. Fill them with the
            // strip's own edge colour so the copy covers the whole rectangle.
            if (opaqueFill) FillVacatedBands(count);

            Native.POINT dst = new Native.POINT();
            dst.X = x + _dstDx - Margin;
            dst.Y = y + _dstDy - Margin;
            Native.SIZE size = new Native.SIZE();
            size.cx = _ow;
            size.cy = _oh;
            Native.POINT src = new Native.POINT();
            Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
            blend.BlendOp = Native.AC_SRC_OVER;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = Native.AC_SRC_ALPHA;
            Native.UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0,
                ref blend, Native.ULW_ALPHA);

            _dumpIndex++;
            if (_dumpDir != null && (_dumpIndex % 20) == 0) DumpFrames();
        }

        /// <summary>
        /// Debug aid: writes the composed overlay bitmap (with alpha) and the
        /// captured source frame as PNGs, so the copy can be inspected without
        /// going through the screen.
        /// </summary>
        private void DumpFrames()
        {
            try
            {
                if (!System.IO.Directory.Exists(_dumpDir))
                    System.IO.Directory.CreateDirectory(_dumpDir);
                string n = _dumpIndex.ToString("D4");
                using (var bmp = new System.Drawing.Bitmap(_ow, _oh, _ow * 4,
                           System.Drawing.Imaging.PixelFormat.Format32bppPArgb, _memBits))
                {
                    bmp.Save(System.IO.Path.Combine(_dumpDir, "ov" + n + ".png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }
                if (_srcBits != IntPtr.Zero)
                {
                    using (var bmp = new System.Drawing.Bitmap(_w, _h, _w * 4,
                               System.Drawing.Imaging.PixelFormat.Format32bppPArgb, _srcBits))
                    {
                        bmp.Save(System.IO.Path.Combine(_dumpDir, "src" + n + ".png"),
                            System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                if (_liveBits != IntPtr.Zero)
                {
                    using (var bmp = new System.Drawing.Bitmap(_w, _h, _w * 4,
                               System.Drawing.Imaging.PixelFormat.Format32bppPArgb, _liveBits))
                    {
                        bmp.Save(System.IO.Path.Combine(_dumpDir, "live" + n + ".png"),
                            System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                if (_baseFrame != null && _baseFrame.Length == _w * _h * 4)
                {
                    var pin = System.Runtime.InteropServices.GCHandle.Alloc(_baseFrame,
                        System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        using (var bmp = new System.Drawing.Bitmap(_w, _h, _w * 4,
                                   System.Drawing.Imaging.PixelFormat.Format32bppPArgb,
                                   pin.AddrOfPinnedObject()))
                        {
                            bmp.Save(System.IO.Path.Combine(_dumpDir, "base" + n + ".png"),
                                System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    finally { pin.Free(); }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("frame dump failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }


        /// <summary>
        /// Starts refreshing the copied window content from the real window.
        /// PrintWindow-capable windows are refreshed while they sit at the
        /// screen edge with a two-pixel sliver visible; Explorer is refreshed
        /// through Windows Graphics Capture and therefore has to stay on screen
        /// (under the overlay). Returns false when the live source could not be
        /// started, so the caller can fall back to a frozen copy.
        /// </summary>
        public bool StartLive(IntPtr target)
        {
            if (!LiveCapable || target == IntPtr.Zero || _liveThread != null) return false;
            _liveHwnd = target;
            _liveReady = false;
            _liveRunning = true;
            _liveThread = new Thread(_wgcCapable ? (ThreadStart)WgcLiveLoop : LiveLoop);
            _liveThread.IsBackground = true;
            _liveThread.Name = "JellyLiveCapture";
            _liveThread.Start();
            // Never wait for the capture here: creating the D3D device and the
            // Graphics Capture session takes a few hundred milliseconds, and
            // blocking the drag thread for that long leaves the window frozen
            // under a moving cursor, then it jumps to catch up. The copy is
            // already up with the takeover frame; live frames replace it as
            // soon as they arrive.
            Log.Debug("Live capture started for " + target.ToInt64());
            return true;
        }

        public void StopLive()
        {
            _liveRunning = false;
            Thread t = _liveThread;
            _liveThread = null;
            if (t != null)
            {
                try { t.Join(1500); }
                catch { }
                Log.Debug("Live capture stopped");
            }
            _liveReady = false;
            _liveHwnd = IntPtr.Zero;
        }

        private void LiveLoop()
        {
            while (_liveRunning)
            {
                bool need;
                lock (_liveLock) { need = !_liveReady; }
                if (need && _liveHwnd != IntPtr.Zero && Native.IsWindow(_liveHwnd))
                {
                    Native.PatBlt(_liveDc, 0, 0, _w, _h, Native.WHITENESS);
                    bool ok = Native.PrintWindow(_liveHwnd, _liveDc, Native.PW_RENDERFULLCONTENT);
                    if (ok && !LooksBlank())
                    {
                        lock (_liveLock) { _liveReady = true; }
                    }
                }
                // Around fifteen refreshes per second; the drag itself keeps
                // running at full rate because the capture is on this thread.
                for (int i = 0; i < 4 && _liveRunning; i++)
                    Thread.Sleep(10);
            }
        }

        private void WgcLiveLoop()
        {
            WgcCapture capture = null;
            int frames = 0;
            try
            {
                capture = new WgcCapture();
                if (!capture.Start(_liveHwnd, _w, _h))
                {
                    Log.Debug("WGC start failed: " + capture.LastError);
                    return;
                }
                Log.Debug("WGC live capture ok=True hwnd=" + _liveHwnd.ToInt64());
                int noFrame = 0;
                while (_liveRunning)
                {
                    bool need;
                    lock (_liveLock) { need = !_liveReady; }
                    if (need)
                    {
                        bool got = false;
                        try
                        {
                            got = capture.TryCopyTo(_liveBits, _w, _h, _w * 4);
                        }
                        catch (Exception ex)
                        {
                            Log.Debug("WGC copy failed: " + ex.GetType().Name + ": " + ex.Message);
                            return;
                        }
                        if (got)
                        {
                            noFrame = 0;
                            frames++;
                            lock (_liveLock) { _liveReady = true; }
                        }
                        else
                        {
                            noFrame++;
                            if (noFrame == 150)
                                Log.Debug("WGC still no frames, error=" + (capture.LastError ?? "-"));
                        }
                    }
                    for (int i = 0; i < 2 && _liveRunning; i++)
                        Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("WGC live loop failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (capture != null) capture.Dispose();
                Log.Debug("WGC live frames=" + frames);
            }
        }

        private void CommitLive()
        {
            lock (_liveLock)
            {
                if (!_liveReady || _liveDc == IntPtr.Zero) return;
                _liveReady = false;
                // Use the frame exactly as captured, alpha included. A Graphics
                // Capture frame is premultiplied, and the desktop behind the
                // overlay is the real backdrop, so DWM builds the copy the same
                // way it builds the window itself: translucency follows whatever
                // is behind it, and every pixel stays live. Baking the frame
                // over a stored screenshot (the old behaviour) froze the content
                // and glued the takeover backdrop to the copy.
                Native.CopyMemory(_srcBits, _liveBits, (IntPtr)(_w * _h * 4));
                MaskBorders(_srcBits);
            }
        }

        private bool LooksBlank()
        {
            if (_liveBits == IntPtr.Zero || _w <= 0 || _h <= 0) return true;
            int stepX = Math.Max(1, _w / 8);
            int stepY = Math.Max(1, _h / 8);
            int lit = 0;
            for (int y = 0; y < _h; y += stepY)
            {
                for (int x = 0; x < _w; x += stepX)
                {
                    int px = Marshal.ReadInt32(_liveBits, (y * _w + x) * 4);
                    int r = px & 0xFF;
                    int g = (px >> 8) & 0xFF;
                    int b = (px >> 16) & 0xFF;
                    if (r + g + b > 24 && ++lit >= 3) return false;
                }
            }
            return true;
        }

        public void Dispose()
        {
            StopLive();
            ReleaseBuffers();
            if (_hwnd != IntPtr.Zero)
            {
                Native.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }

        private bool EnsureWindow()
        {
            if (_hwnd != IntPtr.Zero) return true;
            // WS_EX_TOPMOST must be present at creation: adding it later with
            // SetWindowPos(HWND_TOPMOST) reports success but the flag never
            // sticks in this shell, so the copy ended up under the real window.
            _hwnd = Native.CreateWindowEx(
                (uint)(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE |
                       Native.WS_EX_TOPMOST),
                "STATIC", "JellyBendOverlay", Native.WS_POPUP,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return _hwnd != IntPtr.Zero;
        }

        private bool EnsureBuffers(int w, int h)
        {
            if (_srcDc != IntPtr.Zero && _memDc != IntPtr.Zero && _w == w && _h == h)
                return true;

            ReleaseBuffers();

            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return false;
            try
            {
                _srcDc = Native.CreateCompatibleDC(screen);
                _memDc = Native.CreateCompatibleDC(screen);
                _tmpDc = Native.CreateCompatibleDC(screen);
                _liveDc = Native.CreateCompatibleDC(screen);
                if (_srcDc == IntPtr.Zero || _memDc == IntPtr.Zero || _tmpDc == IntPtr.Zero) return false;

                Native.BITMAPINFO info = new Native.BITMAPINFO();
                info.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                info.bmiHeader.biWidth = w;
                info.bmiHeader.biHeight = -h;
                info.bmiHeader.biPlanes = 1;
                info.bmiHeader.biBitCount = 32;
                info.bmiHeader.biCompression = 0;

                _srcBmp = Native.CreateDIBSection(screen, ref info, 0, out _srcBits, IntPtr.Zero, 0);
                if (_srcBmp == IntPtr.Zero) return false;
                _srcOld = Native.SelectObject(_srcDc, _srcBmp);

                if (_liveDc != IntPtr.Zero)
                {
                    _liveBmp = Native.CreateDIBSection(screen, ref info, 0, out _liveBits, IntPtr.Zero, 0);
                    if (_liveBmp != IntPtr.Zero)
                        _liveOld = Native.SelectObject(_liveDc, _liveBmp);
                }

                int ow = w + Margin * 2;
                int oh = h + Margin * 2;
                Native.BITMAPINFO oinfo = new Native.BITMAPINFO();
                oinfo.bmiHeader = info.bmiHeader;
                oinfo.bmiHeader.biWidth = ow;
                oinfo.bmiHeader.biHeight = -oh;

                _memBmp = Native.CreateDIBSection(screen, ref oinfo, 0, out _memBits, IntPtr.Zero, 0);
                if (_memBmp == IntPtr.Zero) return false;
                _memOld = Native.SelectObject(_memDc, _memBmp);

                _tmpBmp = Native.CreateDIBSection(screen, ref oinfo, 0, out _tmpBits, IntPtr.Zero, 0);
                if (_tmpBmp == IntPtr.Zero) return false;
                _tmpOld = Native.SelectObject(_tmpDc, _tmpBmp);

                _w = w;
                _h = h;
                _ow = ow;
                _oh = oh;
                if (_bandBuf == IntPtr.Zero)
                    _bandBuf = Marshal.AllocHGlobal(Margin * 4);
                return true;
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        private void ReleaseBuffers()
        {
            if (_srcDc != IntPtr.Zero && _srcOld != IntPtr.Zero)
                Native.SelectObject(_srcDc, _srcOld);
            if (_memDc != IntPtr.Zero && _memOld != IntPtr.Zero)
                Native.SelectObject(_memDc, _memOld);
            if (_tmpDc != IntPtr.Zero && _tmpOld != IntPtr.Zero)
                Native.SelectObject(_tmpDc, _tmpOld);
            if (_liveDc != IntPtr.Zero && _liveOld != IntPtr.Zero)
                Native.SelectObject(_liveDc, _liveOld);
            if (_srcBmp != IntPtr.Zero) Native.DeleteObject(_srcBmp);
            if (_memBmp != IntPtr.Zero) Native.DeleteObject(_memBmp);
            if (_tmpBmp != IntPtr.Zero) Native.DeleteObject(_tmpBmp);
            if (_liveBmp != IntPtr.Zero) Native.DeleteObject(_liveBmp);
            if (_srcDc != IntPtr.Zero) Native.DeleteDC(_srcDc);
            if (_memDc != IntPtr.Zero) Native.DeleteDC(_memDc);
            if (_tmpDc != IntPtr.Zero) Native.DeleteDC(_tmpDc);
            if (_liveDc != IntPtr.Zero) Native.DeleteDC(_liveDc);
            _srcDc = IntPtr.Zero;
            _memDc = IntPtr.Zero;
            _tmpDc = IntPtr.Zero;
            _liveDc = IntPtr.Zero;
            _srcBmp = IntPtr.Zero;
            _memBmp = IntPtr.Zero;
            _tmpBmp = IntPtr.Zero;
            _liveBmp = IntPtr.Zero;
            _srcOld = IntPtr.Zero;
            _memOld = IntPtr.Zero;
            _tmpOld = IntPtr.Zero;
            _liveOld = IntPtr.Zero;
            _srcBits = IntPtr.Zero;
            _memBits = IntPtr.Zero;
            _tmpBits = IntPtr.Zero;
            _liveBits = IntPtr.Zero;
            _baseFrame = null;
            if (_bandBuf != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bandBuf);
                _bandBuf = IntPtr.Zero;
            }
            _w = 0;
            _h = 0;
            _ow = 0;
            _oh = 0;
            _capDy = 0;
            _dstDx = 0;
            _dstDy = 0;
            _inDx = 0;
            _inDy = 0;
            _cw = 0;
            _ch = 0;
        }

        private bool Analyze(bool forceOpaque, bool screenGrab)
        {
            if (_srcBits == IntPtr.Zero) return false;
            int count = _w * _h;
            if (count <= 0) return false;
            byte[] buffer = new byte[count * 4];
            Marshal.Copy(_srcBits, buffer, 0, buffer.Length);
            if (forceOpaque)
            {
                for (int i = 3; i < buffer.Length; i += 4)
                    buffer[i] = 255;
                Marshal.Copy(buffer, 0, _srcBits, buffer.Length);
            }

            // 1. A flat/blank capture is useless.
            int step = Math.Max(1, count / 512);
            int min = 255;
            int max = 0;
            int transparent = 0;
            int samples = 0;
            for (int i = 0; i < count; i += step)
            {
                int o = i * 4;
                int lum = (buffer[o] + buffer[o + 1] + buffer[o + 2]) / 3;
                if (lum < min) min = lum;
                if (lum > max) max = lum;
                if (buffer[o + 3] < 16) transparent++;
                samples++;
            }
            if (max - min < 6) return false;
            if (transparent > samples * 0.5) return false;

            // 2. Explorer and some GPU/Electron windows return a black client
            // area from PrintWindow while the frame is painted. Check the
            // centre: if it is almost entirely black, use the screen grab.
            int x0 = _w / 5, x1 = _w - x0;
            int y0 = _h / 5, y1 = _h - y0;
            int dark = 0, centre = 0;
            long sum = 0;
            for (int y = y0; y < y1; y += 4)
            {
                int row = y * _w;
                for (int x = x0; x < x1; x += 4)
                {
                    int o = (row + x) * 4;
                    int lum = (buffer[o] + buffer[o + 1] + buffer[o + 2]) / 3;
                    if (lum < 8) dark++;
                    sum += lum;
                    centre++;
                }
            }
            _lastDark = centre > 0 ? (double)dark / centre : 0;
            _lastAvg = centre > 0 ? (double)sum / centre : 0;
            // A dark PrintWindow result is usually a failed capture. A screen
            // grab of a genuinely dark window is fine, so only reject it there.
            if (!screenGrab && centre > 0 && (_lastAvg < 20 || dark > centre * 0.6)) return false;

            return true;
        }
    }
}
