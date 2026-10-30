using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Okna
{
    /// <summary>
    /// Remembers the window that the jelly effect made invisible (layered,
    /// alpha 1). If the program dies in the middle of the effect, the next
    /// start puts the window back: otherwise it would stay invisible until its
    /// own process was restarted. The record is written before the window is
    /// hidden and removed as soon as it is visible again.
    /// </summary>
    internal static class HiddenWindowGuard
    {
        private static readonly object _lock = new object();
        private static string _file;
        private static bool _installed;

        private static string FilePath
        {
            get
            {
                if (_file == null)
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Okna");
                    _file = Path.Combine(dir, "hidden.txt");
                }
                return _file;
            }
        }

        /// <summary>
        /// Hooks the last moments of the process so a normal exit or a logoff
        /// also restores the window, even if the engine could not do it.
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try { AppDomain.CurrentDomain.ProcessExit += OnProcessExit; }
            catch (Exception ex) { Log.Debug("Hidden guard exit hook failed: " + ex.Message); }
            try { SystemEvents.SessionEnding += OnSessionEnding; }
            catch (Exception ex) { Log.Debug("Hidden guard session hook failed: " + ex.Message); }
        }

        /// <summary>
        /// Must be called before the window is made transparent. The record
        /// carries the window handle, its process and its class so a reused
        /// handle belonging to another window is never touched.
        /// </summary>
        public static void Mark(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            lock (_lock)
            {
                try
                {
                    uint pid;
                    Native.GetWindowThreadProcessId(hwnd, out pid);
                    string text = hwnd.ToInt64().ToString() + "\n" +
                                  pid.ToString() + "\n" +
                                  Native.GetWindowClass(hwnd);
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, text, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Log.Debug("Hidden guard write failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Called once the window is visible again. Removing the file keeps the
        /// next start from doing anything.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                }
                catch (Exception ex)
                {
                    Log.Debug("Hidden guard clear failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Restores a window left hidden by a previous run. Safe to call at any
        /// time: it only acts when the recorded window still exists, belongs to
        /// the recorded process and is still layered at alpha 1.
        /// </summary>
        public static bool Restore()
        {
            lock (_lock)
            {
                string file = FilePath;
                try
                {
                    if (!File.Exists(file)) return false;

                    string[] lines = File.ReadAllLines(file);
                    long raw;
                    uint pid;
                    if (lines.Length < 2 ||
                        !long.TryParse(lines[0], out raw) ||
                        !uint.TryParse(lines[1], out pid))
                    {
                        File.Delete(file);
                        return false;
                    }
                    string cls = lines.Length > 2 ? lines[2] : null;

                    IntPtr hwnd = new IntPtr(raw);
                    if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
                    {
                        File.Delete(file);
                        return false;
                    }

                    uint actualPid;
                    Native.GetWindowThreadProcessId(hwnd, out actualPid);
                    if (actualPid != pid)
                    {
                        File.Delete(file);
                        return false;
                    }
                    if (!string.IsNullOrEmpty(cls) && Native.GetWindowClass(hwnd) != cls)
                    {
                        File.Delete(file);
                        return false;
                    }

                    long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
                    if ((ex & Native.WS_EX_LAYERED) != 0)
                    {
                        byte alpha;
                        bool hasAlpha = Native.TryGetWindowAlpha(hwnd, out alpha);
                        if (!hasAlpha || alpha <= 1)
                            Native.SetWindowAlpha(hwnd, 255);
                        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE,
                            ex & ~Native.WS_EX_LAYERED);
                        Log.Info("Restored window left hidden by a previous run: " +
                                 hwnd.ToInt64() + " '" + Native.GetWindowTitle(hwnd) + "'");
                    }

                    File.Delete(file);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("Hidden guard restore failed: " + ex);
                    return false;
                }
            }
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            Restore();
        }

        private static void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            Restore();
        }
    }
}
