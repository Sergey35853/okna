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
        private volatile bool _autoUpdate = true;

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

        public bool AutoUpdate
        {
            get { return _autoUpdate; }
            set { _autoUpdate = value; }
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
                        v = key.GetValue("AutoUpdate");
                        if (v != null) s.AutoUpdate = Convert.ToInt32(v) != 0;
                        v = key.GetValue("FirstRun");
                        s.FirstRun = v == null || Convert.ToInt32(v) != 0;
                    }
                }

                // The program used to be called "Желе-окна" and kept its
                // settings under another key. Carry them over once.
                if (!loaded) MigrateLegacy(s);

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
                    key.SetValue("AutoUpdate", _autoUpdate ? 1 : 0, RegistryValueKind.DWord);
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

