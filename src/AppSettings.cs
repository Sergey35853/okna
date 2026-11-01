using System;
using Microsoft.Win32;

namespace Okna
{
    internal sealed class AppSettings
    {
        private const string SettingsKey = @"Software\Okna";
        private const string LegacySettingsKey = @"Software\JellyWindows";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "Okna";
        private const string LegacyRunValue = "JellyWindows";

        private volatile bool _enabled = true;
        private volatile int _jelly = 70;
        private volatile bool _releaseWobble = false;
        private volatile bool _bend = true;
        private volatile bool _maxShake = true;
        private volatile bool _shatter = true;
        private volatile bool _burn = false;
        private volatile bool _roll = true;
        private volatile bool _rollHorizontal = false;
        private volatile bool _autoUpdate = true;
        private volatile string[] _exclude = new string[0];

        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        public int Jelly
        {
            get { return _jelly; }
            set
            {
                if (value < 0) value = 0;
                if (value > 100) value = 100;
                _jelly = value;
            }
        }

        public bool ReleaseWobble
        {
            get { return _releaseWobble; }
            set { _releaseWobble = value; }
        }

        public bool Bend
        {
            get { return _bend; }
            set { _bend = value; }
        }

        public bool MaxShake
        {
            get { return _maxShake; }
            set { _maxShake = value; }
        }

        public bool Shatter
        {
            get { return _shatter; }
            set { _shatter = value; }
        }

        /// <summary>
        /// Burn the window instead of shattering it. The two effects are
        /// alternatives; the settings form never turns both on.
        /// </summary>
        public bool Burn
        {
            get { return _burn; }
            set { _burn = value; }
        }

        public bool Roll
        {
            get { return _roll; }
            set { _roll = value; }
        }

        /// <summary>
        /// Roll minimized or closed-into-tray windows sideways into the
        /// taskbar instead of upwards.
        /// </summary>
        public bool RollHorizontal
        {
            get { return _rollHorizontal; }
            set { _rollHorizontal = value; }
        }

        public bool AutoUpdate
        {
            get { return _autoUpdate; }
            set { _autoUpdate = value; }
        }

        /// <summary>
        /// Executable names the user does not want the effects in. The array
        /// is replaced as a whole, so the engine thread can read it without a
        /// lock while the settings form edits it.
        /// </summary>
        public string[] Exclude
        {
            get { return _exclude; }
        }

        public bool IsExcluded(string executableName)
        {
            if (string.IsNullOrEmpty(executableName)) return false;
            string[] list = _exclude;
            for (int i = 0; i < list.Length; i++)
                if (string.Equals(list[i], executableName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public void AddExclude(string executableName)
        {
            string name = CleanExecutableName(executableName);
            if (name == null || IsExcluded(name)) return;
            string[] list = _exclude;
            string[] next = new string[list.Length + 1];
            Array.Copy(list, next, list.Length);
            next[list.Length] = name;
            _exclude = next;
        }

        public void RemoveExclude(string executableName)
        {
            string[] list = _exclude;
            int index = -1;
            for (int i = 0; i < list.Length; i++)
                if (string.Equals(list[i], executableName, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            if (index < 0) return;
            string[] next = new string[list.Length - 1];
            Array.Copy(list, 0, next, 0, index);
            Array.Copy(list, index + 1, next, index, list.Length - index - 1);
            _exclude = next;
        }

        private static string CleanExecutableName(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string name = value.Trim();
            if (name.Length == 0) return null;
            try { name = System.IO.Path.GetFileName(name); }
            catch { }
            if (string.IsNullOrEmpty(name)) return null;
            return name;
        }

        public bool FirstRun { get; set; }
        public bool StartWithWindows { get; set; }

        public double SpringK
        {
            get { return 500.0 - 400.0 * (_jelly / 100.0); }
        }

        public double SpringC
        {
            get { return 32.0 - 20.0 * (_jelly / 100.0); }
        }

        public string JellyLabel
        {
            get
            {
                if (_jelly < 35) return "Сдержанно";
                if (_jelly < 78) return "Упруго";
                return "Очень желейно";
            }
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                bool loaded = false;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey, false))
                {
                    if (key != null)
                    {
                        loaded = true;
                        object v = key.GetValue("Enabled");
                        if (v != null) s.Enabled = Convert.ToInt32(v) != 0;
                        v = key.GetValue("Jelly");
                        if (v != null) s.Jelly = Convert.ToInt32(v);
                        v = key.GetValue("ReleaseWobble");
                        if (v != null) s.ReleaseWobble = Convert.ToInt32(v) != 0;
                        v = key.GetValue("Bend");
                        if (v != null) s.Bend = Convert.ToInt32(v) != 0;
                        v = key.GetValue("MaxShake");
                        if (v != null) s.MaxShake = Convert.ToInt32(v) != 0;
                        v = key.GetValue("Shatter");
                        if (v != null) s.Shatter = Convert.ToInt32(v) != 0;
                        v = key.GetValue("Burn");
                        if (v != null) s.Burn = Convert.ToInt32(v) != 0;
                        v = key.GetValue("Roll");
                        if (v != null) s.Roll = Convert.ToInt32(v) != 0;
                        v = key.GetValue("RollHorizontal");
                        if (v != null) s.RollHorizontal = Convert.ToInt32(v) != 0;
                        v = key.GetValue("AutoUpdate");
                        if (v != null) s.AutoUpdate = Convert.ToInt32(v) != 0;
                        v = key.GetValue("Exclude");
                        if (v is string[])
                        {
                            string[] names = (string[])v;
                            for (int i = 0; i < names.Length; i++)
                                s.AddExclude(names[i]);
                        }
                        v = key.GetValue("FirstRun");
                        s.FirstRun = v == null || Convert.ToInt32(v) != 0;
                    }
                }

                // The program used to be called "Желе-окна" and kept its
                // settings under another key. Carry them over once.
                if (!loaded) MigrateLegacy(s);
                if (s.Burn) s.Shatter = false;

                s.StartWithWindows = IsAutoStartEnabled();
            }
            catch (Exception ex)
            {
                Log.Error("Settings load failed: " + ex.Message);
            }
            return s;
        }

        private static void MigrateLegacy(AppSettings s)
        {
            try
            {
                using (RegistryKey old = Registry.CurrentUser.OpenSubKey(LegacySettingsKey, false))
                {
                    if (old == null) return;

                    object v = old.GetValue("Enabled");
                    if (v != null) s.Enabled = Convert.ToInt32(v) != 0;
                    v = old.GetValue("Jelly");
                    if (v != null) s.Jelly = Convert.ToInt32(v);
                    v = old.GetValue("ReleaseWobble");
                    if (v != null) s.ReleaseWobble = Convert.ToInt32(v) != 0;
                    v = old.GetValue("Bend");
                    if (v != null) s.Bend = Convert.ToInt32(v) != 0;
                    // Someone who used the old build has already seen the
                    // first-run hint; do not show it again.
                    s.FirstRun = false;
                }

                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (run != null)
                    {
                        object legacy = run.GetValue(LegacyRunValue);
                        if (legacy != null)
                        {
                            string path = legacy as string;
                            if (!string.IsNullOrEmpty(path))
                            {
                                int cut = path.IndexOf('"', 1);
                                if (path.StartsWith("\"") && cut > 1) path = path.Substring(1, cut - 1);
                            }
                            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                                path = System.Windows.Forms.Application.ExecutablePath;
                            run.SetValue(RunValue, "\"" + path + "\" --silent", RegistryValueKind.String);
                            run.DeleteValue(LegacyRunValue, false);
                        }
                    }
                }

                s.Save();
                Log.Info("Settings migrated from the previous name");
            }
            catch (Exception ex)
            {
                Log.Error("Settings migration failed: " + ex);
            }
        }

        public void Save()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (key == null) return;
                    key.SetValue("Enabled", _enabled ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Jelly", _jelly, RegistryValueKind.DWord);
                    key.SetValue("ReleaseWobble", _releaseWobble ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Bend", _bend ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("MaxShake", _maxShake ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Shatter", _shatter ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Burn", _burn ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Roll", _roll ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("RollHorizontal", _rollHorizontal ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("AutoUpdate", _autoUpdate ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Exclude", _exclude, RegistryValueKind.MultiString);
                    key.SetValue("FirstRun", 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Settings save failed: " + ex.Message);
            }
        }

        public void MarkConfigured()
        {
            FirstRun = false;
            Save();
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return key != null && key.GetValue(RunValue) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetAutoStart(bool enabled, string executablePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (enabled)
                        key.SetValue(RunValue, "\"" + executablePath + "\" --silent", RegistryValueKind.String);
                    else
                        key.DeleteValue(RunValue, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Autostart update failed: " + ex.Message);
            }
        }

        public static void RefreshAutoStartPath(string executablePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    object value = key.GetValue(RunValue);
                    if (value == null) return;
                    key.SetValue(RunValue, "\"" + executablePath + "\" --silent", RegistryValueKind.String);
                }
            }
            catch { }
        }
    }
}

