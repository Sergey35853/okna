using System;
using System.IO;

namespace Okna
{
    /// <summary>
    /// Counts consecutive starts that never reached the healthy marker. Two
    /// failed starts in a row put the next run into safe mode: the effects are
    /// switched off in memory only, so a broken effect cannot lock the user out
    /// of the program. Turning the effects back on (tray, hotkey or settings)
    /// clears the counter and leaves safe mode for good.
    /// </summary>
    internal static class StartupGuard
    {
        private const int FailureLimit = 2;
        private const string FileName = "startup.txt";

        private static int _failures;
        private static bool _safeMode;
        private static int _healthyStarts;
        private const int AutoClearLimit = 3;

        public static bool SafeMode
        {
            get { return _safeMode; }
        }

        public static int Failures
        {
            get { return _failures; }
        }

        public static string Folder
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
                return Path.Combine(local, "Okna");
            }
        }

        public static string StatePath
        {
            get { return Path.Combine(Folder, FileName); }
        }

        /// <summary>
        /// Called once at the very beginning of a normal start. Returns true
        /// when this run must keep the effects off.
        /// </summary>
        public static bool Begin()
        {
            int previous = ReadFailures();
            _safeMode = Decide(previous);
            _failures = previous + 1;
            Write(_failures);
            Log.Info("Startup guard: previous failures=" + previous +
                     " safe mode=" + _safeMode);
            return _safeMode;
        }

        /// <summary>
        /// Pure rule used by Begin and the tests: two failed starts in a row
        /// are enough to start the next run in safe mode.
        /// </summary>
        internal static bool Decide(int previousFailures)
        {
            return previousFailures >= FailureLimit;
        }

        /// <summary>
        /// The engine and the tray are up. A normal start resets the counter;
        /// a safe-mode start keeps it so the next run is safe as well until
        /// the user deliberately enables the effects again.
        /// </summary>
        public static void MarkHealthy()
        {
            _failures = 0;
            Write(0);
            if (_safeMode)
            {
                // Several healthy starts in a row clear safe mode automatically
                // so the user is not stuck until they manually re-enables effects.
                _healthyStarts++;
                if (_healthyStarts >= AutoClearLimit)
                {
                    _safeMode = false;
                    _healthyStarts = 0;
                    Log.Info("Startup guard: safe mode auto-cleared after " + AutoClearLimit + " healthy starts");
                }
            }
            else
            {
                _healthyStarts = 0;
            }
        }

        /// <summary>The user turned the effects on: leave safe mode for good.</summary>
        public static void NoteEffectsEnabled()
        {
            bool wasSafe = _safeMode;
            _safeMode = false;
            _failures = 0;
            Write(0);
            if (wasSafe) Log.Info("Startup guard: safe mode cleared by the user");
        }

        /// <summary>Removes the state file; used by the reset command and tests.</summary>
        public static void Clear()
        {
            _failures = 0;
            _safeMode = false;
            try
            {
                if (File.Exists(StatePath)) File.Delete(StatePath);
            }
            catch (Exception ex)
            {
                Log.Debug("Startup guard clear failed: " + ex.Message);
            }
        }

        private static int ReadFailures()
        {
            try
            {
                if (!File.Exists(StatePath)) return 0;
                int value;
                if (int.TryParse(File.ReadAllText(StatePath).Trim(), out value))
                    return value < 0 ? 0 : value;
            }
            catch (Exception ex)
            {
                Log.Debug("Startup guard read failed: " + ex.Message);
            }
            return 0;
        }

        private static void Write(int value)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(StatePath, value.ToString());
            }
            catch (Exception ex)
            {
                Log.Debug("Startup guard write failed: " + ex.Message);
            }
        }
    }
}
