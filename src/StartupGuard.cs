using System;
using System.IO;

namespace Okna
{
    /// <summary>
    /// Counts consecutive starts that never reached the healthy marker. Two
    /// failed starts in a row put the next run into safe mode: the effects are
    /// switched off in memory only, so a broken effect cannot lock the user out
    /// of the program. Turning effects back on clears the counter; otherwise
    /// three consecutive healthy safe-mode starts do so automatically.
    /// </summary>
    internal static class StartupGuard
    {
        private const int FailureLimit = 2;
        private const string FileName = "startup.txt";
        private const string HealthyFileName = "startup-healthy.txt";
        private const int AutoClearLimit = 3;

        private static int _failures;
        private static bool _safeMode;
        private static int _healthyStarts;

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

        private static string HealthyStatePath
        {
            get { return Path.Combine(Folder, HealthyFileName); }
        }

        /// <summary>
        /// Called once at the very beginning of a normal start. Returns true
        /// when this run must keep the effects off.
        /// </summary>
        public static bool Begin()
        {
            int previous = ReadFailures();
            _safeMode = Decide(previous);
            int savedFailures;
            int savedHealthyStarts = ReadHealthyStarts(out savedFailures);
            _healthyStarts = HealthyStartsForRun(previous, savedFailures, savedHealthyStarts, _safeMode);
            _failures = previous + 1;
            Write(_failures);
            if (!_safeMode) WriteHealthyStarts(0, 0);
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
        /// The engine and tray are up. A normal start resets the counter; safe
        /// mode remains active for the next run until three healthy starts.
        /// </summary>
        public static void MarkHealthy()
        {
            if (!_safeMode)
            {
                _failures = 0;
                _healthyStarts = 0;
                Write(0);
                WriteHealthyStarts(0, 0);
                return;
            }

            _healthyStarts++;
            if (_healthyStarts >= AutoClearLimit)
            {
                _safeMode = false;
                _failures = 0;
                _healthyStarts = 0;
                Write(0);
                WriteHealthyStarts(0, 0);
                Log.Info("Startup guard: safe mode cleared after " + AutoClearLimit + " healthy starts");
                return;
            }

            _failures = Math.Max(FailureLimit, _failures);
            Write(_failures);
            WriteHealthyStarts(_healthyStarts, _failures);
        }

        internal static int HealthyStartsForRun(int failuresBeforeStart,
            int savedFailures, int savedHealthyStarts, bool safeMode)
        {
            if (!safeMode || failuresBeforeStart != savedFailures) return 0;
            if (savedHealthyStarts < 0) return 0;
            return Math.Min(AutoClearLimit - 1, savedHealthyStarts);
        }

        /// <summary>The user turned the effects on: leave safe mode for good.</summary>
        public static void NoteEffectsEnabled()
        {
            bool wasSafe = _safeMode;
            _safeMode = false;
            _failures = 0;
            _healthyStarts = 0;
            Write(0);
            WriteHealthyStarts(0, 0);
            if (wasSafe) Log.Info("Startup guard: safe mode cleared by the user");
        }

        /// <summary>Removes the state file; used by the reset command and tests.</summary>
        public static void Clear()
        {
            _failures = 0;
            _safeMode = false;
            _healthyStarts = 0;
            try
            {
                if (File.Exists(StatePath)) File.Delete(StatePath);
                if (File.Exists(HealthyStatePath)) File.Delete(HealthyStatePath);
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

        private static int ReadHealthyStarts(out int failures)
        {
            failures = 0;
            try
            {
                if (!File.Exists(HealthyStatePath)) return 0;
                string[] values = File.ReadAllText(HealthyStatePath).Trim().Split('|');
                int starts;
                if (values.Length == 2 &&
                    int.TryParse(values[0], out starts) &&
                    int.TryParse(values[1], out failures) &&
                    starts >= 0 && failures >= 0)
                    return starts;
            }
            catch (Exception ex)
            {
                Log.Debug("Startup guard healthy count read failed: " + ex.Message);
            }
            failures = 0;
            return 0;
        }

        private static void WriteHealthyStarts(int starts, int failures)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(HealthyStatePath, starts.ToString() + "|" + failures.ToString());
            }
            catch (Exception ex)
            {
                Log.Debug("Startup guard healthy count write failed: " + ex.Message);
            }
        }
    }
}
