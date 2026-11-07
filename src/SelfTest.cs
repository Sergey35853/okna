using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Okna
{
    /// <summary>
    /// Runs the real engine against a real window and drags its edges with the
    /// mouse. This is the automated version of the manual check: it proves
    /// that the resize watcher, the wave and the final rectangle work on the
    /// machine in front of us, without asking the user to drag anything.
    /// </summary>
    internal static class SelfTest
    {
        private const uint LeftDown = 0x0002;
        private const uint LeftUp = 0x0004;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly IntPtr HwndNotTopmost = new IntPtr(-2);
        private const int WmNcHitTest = 0x0084;

        private static readonly List<string> Lines = new List<string>();
        private static int _failed;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

        public static string ReportPath
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
                return Path.Combine(Path.Combine(local, "Okna"), "self-test.txt");
            }
        }

        public static int Run()
        {
            Native.EnablePerMonitorDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppSettings settings = AppSettings.Load();
            // The test must not depend on the user's current switches, and it
            // must never save these values.
            settings.ResetAll();
            settings.Enabled = true;
            settings.Jelly = 70;
            settings.Bend = true;
            settings.Economy = false;
            settings.MaxShake = false;
            settings.Shatter = false;
            settings.Burn = false;
            settings.Roll = false;
            settings.ReleaseWobble = false;
            settings.PauseOnBattery = false;
            settings.PauseOnCall = false;
            settings.RespectReduceMotion = false;
            settings.AutoUpdate = false;

            JellyEngine engine = new JellyEngine(settings);
            engine.Start();

            ResizeProbe form = new ResizeProbe();
            form.Show();
            engine.AllowOwnWindow(form.Handle);
            BringToFront(form.Handle);

            ManualResetEvent done = new ManualResetEvent(false);
            Thread driver = new Thread(new ThreadStart(delegate
            {
                try
                {
                    RunChecks(engine, form, settings);
                }
                catch (Exception ex)
                {
                    Fail("the self-test crashed: " + ex.Message);
                    Log.Error("Self-test crashed: " + ex);
                }
                finally
                {
                    done.Set();
                    try { form.BeginInvoke(new MethodInvoker(form.Close)); } catch { }
                }
            }));
            driver.IsBackground = true;
            driver.Start();

            // The message loop has to stay on this thread so the native resize
            // loop can run while the driver moves the cursor from its own
            // thread. The timer closes the window once the driver is done.
            System.Windows.Forms.Timer closeTimer = new System.Windows.Forms.Timer();
            closeTimer.Interval = 150;
            closeTimer.Tick += delegate
            {
                if (!done.WaitOne(0)) return;
                closeTimer.Stop();
                closeTimer.Dispose();
                try { if (!form.IsDisposed) form.Close(); } catch { }
            };
            closeTimer.Start();

            try { Application.Run(form); }
            finally
            {
                try { engine.DisallowOwnWindow(form.Handle); } catch { }
                engine.Stop();
                if (!done.WaitOne(3000)) Fail("the driver did not finish in time");
            }

            return WriteReport();
        }

        private static void RunChecks(JellyEngine engine, ResizeProbe form, AppSettings settings)
        {
            Thread.Sleep(500);
            Check("the engine is running", engine.IsRunning);
            for (int i = 0; i < 20 && Native.GetForegroundWindow() != form.Handle; i++)
            {
                BringToFront(form.Handle);
                Thread.Sleep(50);
            }
            Native.RECT hitRect;
            Native.POINT hitPoint = new Native.POINT();
            if (Native.GetWindowRect(form.Handle, out hitRect))
            {
                hitPoint.X = (hitRect.Left + hitRect.Right) / 2;
                hitPoint.Y = (hitRect.Top + hitRect.Bottom) / 2;
            }
            IntPtr hit = Native.WindowFromPoint(hitPoint);
            if (hit != IntPtr.Zero) hit = Native.GetAncestor(hit, Native.GA_ROOT);
            Check("the test window is in front",
                hit == form.Handle || Native.GetForegroundWindow() == form.Handle);

            TestEdge(engine, form, "right", 120, 16);
            TestEdge(engine, form, "bottom", 100, 16);
            TestEdge(engine, form, "right", 120, 60);   // slow drag
            TestSuppression(engine, form, settings);
            TestFramelessBorder(form);
        }

        private static void TestEdge(JellyEngine engine, ResizeProbe form,
            string edge, int delta, int stepDelay)
        {
            Native.RECT start;
            if (!Place(form, out start))
            {
                Fail(edge + " edge: the test window has no rectangle");
                return;
            }

            int before = engine.ResizeWaves;
            bool dragged = Drag(form.Handle, start, edge, delta, stepDelay);
            Check(edge + " edge: the drag was delivered", dragged);

            bool waved = WaitForWave(engine, before, 3000);
            Thread.Sleep(1100);

            Native.RECT final;
            if (!Native.GetWindowRect(form.Handle, out final))
            {
                Fail(edge + " edge: the final rectangle is unavailable");
                return;
            }

            int expectedW = start.Width + (edge == "right" ? delta : 0);
            int expectedH = start.Height + (edge == "bottom" ? delta : 0);
            Check(edge + " edge: the size wave ran", waved);
            Check(edge + " edge: width is " + expectedW + " (was " + final.Width + ")",
                Math.Abs(final.Width - expectedW) <= 2);
            Check(edge + " edge: height is " + expectedH + " (was " + final.Height + ")",
                Math.Abs(final.Height - expectedH) <= 2);
            Check(edge + " edge: the position did not drift",
                Math.Abs(final.Left - start.Left) <= 2 &&
                Math.Abs(final.Top - start.Top) <= 2);
        }

        private static void TestSuppression(JellyEngine engine, ResizeProbe form,
            AppSettings settings)
        {
            string exe = Native.GetWindowProcessName(form.Handle);
            if (string.IsNullOrEmpty(exe)) exe = "Okna.exe";

            settings.SetAppProfile(exe, 2, false, false, false);
            Thread.Sleep(1400);

            Native.RECT start;
            if (!Place(form, out start))
            {
                Fail("suppression: the test window has no rectangle");
                settings.RemoveCloseOverride(exe);
                return;
            }

            int before = engine.ResizeWaves;
            Drag(form.Handle, start, "right", 100, 16);
            Thread.Sleep(900);
            Check("a program rule without bend suppresses the wave",
                engine.ResizeWaves == before);

            settings.RemoveCloseOverride(exe);
            Thread.Sleep(1400);
        }

        private static void TestFramelessBorder(ResizeProbe owner)
        {
            FramelessProbe probe = null;
            owner.Invoke(new MethodInvoker(delegate
            {
                probe = new FramelessProbe();
                probe.Show();
                Native.SetForegroundWindow(probe.Handle);
            }));

            try
            {
                Native.RECT rect;
                Native.GetWindowRect(probe.Handle, out rect);
                int midY = rect.Top + rect.Height / 2;
                int midX = rect.Left + rect.Width / 2;

                Native.POINT rightEdge = new Native.POINT();
                rightEdge.X = rect.Right - 5;
                rightEdge.Y = midY;
                Native.POINT middle = new Native.POINT();
                middle.X = midX;
                middle.Y = midY;

                Check("a frameless window reports its right border",
                    JellyEngine.ReportsResizeBorder(probe.Handle, rightEdge));
                Check("a frameless window does not report the middle as a border",
                    !JellyEngine.ReportsResizeBorder(probe.Handle, middle));
            }
            finally
            {
                try
                {
                    owner.Invoke(new MethodInvoker(delegate
                    {
                        if (probe != null && !probe.IsDisposed) probe.Close();
                    }));
                }
                catch { }
            }
        }

        private static bool Place(ResizeProbe form, out Native.RECT rect)
        {
            Native.SetWindowPos(form.Handle, IntPtr.Zero, 320, 240, 820, 620,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Thread.Sleep(250);
            BringToFront(form.Handle);
            Thread.Sleep(150);
            return Native.GetWindowRect(form.Handle, out rect);
        }

        /// <summary>
        /// Raises the test window above normal windows and asks for the
        /// foreground. The terminal that launched the test can be always on
        /// top, and Windows refuses SetForegroundWindow to a process that did
        /// not receive the last input, so the temporary topmost pass is what
        /// makes the automated drag land on the test window.
        /// </summary>
        private static void BringToFront(IntPtr hwnd)
        {
            Native.SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Native.SetWindowPos(hwnd, HwndNotTopmost, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Native.SetForegroundWindow(hwnd);
        }

        private static bool Drag(IntPtr hwnd, Native.RECT rect, string edge,
            int delta, int stepDelay)
        {
            int x;
            int y;
            int dx = 0;
            int dy = 0;
            switch (edge)
            {
                case "right":
                    x = rect.Right - 3;
                    y = rect.Top + rect.Height / 2;
                    dx = 1;
                    break;
                case "bottom":
                    x = rect.Left + rect.Width / 2;
                    y = rect.Bottom - 3;
                    dy = 1;
                    break;
                default:
                    return false;
            }

            SetCursorPos(x, y);
            Thread.Sleep(180);
            mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(120);

            int steps = 20;
            int step = Math.Max(1, delta / steps);
            for (int i = 1; i <= steps; i++)
            {
                SetCursorPos(x + dx * i * step, y + dy * i * step);
                Thread.Sleep(stepDelay);
            }

            Thread.Sleep(120);
            mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(80);
            return true;
        }

        private static bool WaitForWave(JellyEngine engine, int before, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (engine.ResizeWaves > before) return true;
                Thread.Sleep(25);
                waited += 25;
            }
            return false;
        }

        private static void Check(string message, bool ok)
        {
            if (ok) Pass(message);
            else Fail(message);
        }

        private static void Pass(string message)
        {
            Lines.Add("[ok] " + message);
        }

        private static void Fail(string message)
        {
            _failed++;
            Lines.Add("[FAIL] " + message);
        }

        private static int WriteReport()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Okna self-test " + Updater.CurrentLabel);
            text.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            for (int i = 0; i < Lines.Count; i++) text.AppendLine(Lines[i]);
            text.AppendLine();
            text.AppendLine(_failed == 0
                ? "All " + Lines.Count + " checks passed."
                : _failed + " of " + Lines.Count + " checks failed.");
            try
            {
                string folder = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(ReportPath, text.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Log.Error("Self-test report failed: " + ex.Message);
            }
            Log.Info("Self-test: " + (_failed == 0 ? "all passed" : _failed + " failed"));
            return _failed == 0 ? 0 : 1;
        }

        private sealed class ResizeProbe : Form
        {
            public ResizeProbe()
            {
                Text = "Okna self-test";
                ClientSize = new Size(820, 620);
                StartPosition = FormStartPosition.Manual;
                Location = new Point(320, 240);
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = false;
                MinimizeBox = false;
                BackColor = Color.FromArgb(18, 34, 42);
                ForeColor = Color.FromArgb(226, 244, 240);
                Font = new Font("Segoe UI", 10f);
                Controls.Add(new Label
                {
                    Text = "Okna self-test\r\nThis window is resized automatically.",
                    AutoSize = true,
                    Location = new Point(24, 24)
                });
            }
        }

        private sealed class FramelessProbe : Form
        {
            public FramelessProbe()
            {
                Text = "Okna frameless probe";
                ClientSize = new Size(420, 300);
                StartPosition = FormStartPosition.Manual;
                Location = new Point(700, 320);
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                BackColor = Color.FromArgb(20, 40, 50);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmNcHitTest)
                {
                    long lp = m.LParam.ToInt64();
                    int sx = (short)(lp & 0xFFFF);
                    int sy = (short)((lp >> 16) & 0xFFFF);
                    Point p = PointToClient(new Point(sx, sy));
                    if (p.X >= ClientSize.Width - 8) { m.Result = (IntPtr)11; return; }
                    if (p.Y >= ClientSize.Height - 8) { m.Result = (IntPtr)15; return; }
                    if (p.X <= 8) { m.Result = (IntPtr)10; return; }
                    if (p.Y <= 8) { m.Result = (IntPtr)12; return; }
                }
                base.WndProc(ref m);
            }
        }
    }
}
