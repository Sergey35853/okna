using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Okna
{
    /// <summary>
    /// Where a minimized window rolls to. Auto follows the edge of the screen
    /// the taskbar is docked to; the other values force one of the four sides.
    /// </summary>
    internal enum RollDirection
    {
        Auto = 0,
        Down = 1,
        Up = 2,
        Left = 3,
        Right = 4
    }

    internal sealed class AppSettings
    {
        private const string SettingsKey = @"Software\Okna";
        private const string LegacySettingsKey = @"Software\JellyWindows";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "Okna";
        private const string LegacyRunValue = "JellyWindows";
        private const string PortableFileName = "okna.ini";

        private volatile bool _enabled = true;
        private volatile int _jelly = 70;
        private volatile bool _releaseWobble = false;
        private volatile bool _bend = true;
        private volatile bool _maxShake = true;
        private volatile bool _shatter = true;
        private volatile bool _burn = false;
        private volatile bool _roll = true;
        private volatile int _rollDirection = (int)RollDirection.Auto;
        private volatile bool _autoUpdate = true;
        private volatile bool _economy = false;
        private volatile bool _pauseOnBattery = false;
        private volatile bool _pauseOnCall = true;
        private volatile bool _respectReduceMotion = true;
        private volatile int _hotkeyModifiers = (int)(Native.MOD_CONTROL | Native.MOD_ALT);
        private volatile int _hotkeyKey = Native.VK_J;
        private volatile string[] _exclude = new string[0];
        private volatile string[] _closeOverrides = new string[0];
        private bool _portable;
        private string _portablePath;

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
                if (value > 200) value = 200;
                _jelly = value;
            }
        }

        /// <summary>
        /// How strongly the jelly effects react at the current slider value.
        /// 1.1 at the default 70 and 2.4 at the new maximum 200, so the top
        /// half of the slider keeps making windows noticeably more viscous.
        /// </summary>
        public double JellyFactor
        {
            get { return 0.4 + _jelly / 100.0; }
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
        /// Where a minimized or closed-into-tray window rolls to. Auto picks
        /// the screen edge the taskbar lives on.
        /// </summary>
        public RollDirection RollDirection
        {
            get { return (RollDirection)_rollDirection; }
            set
            {
                int v = (int)value;
                if (v < 0 || v > 4) v = 0;
                _rollDirection = v;
            }
        }

        /// <summary>Compatibility with the 1.5.x setting: sideways == left.</summary>
        public bool RollHorizontal
        {
            get { return RollDirection == RollDirection.Left; }
            set { RollDirection = value ? RollDirection.Left : RollDirection.Down; }
        }

        public bool AutoUpdate
        {
            get { return _autoUpdate; }
            set { _autoUpdate = value; }
        }

        /// <summary>
        /// The "Экономно" preset: no live captures, no shake. The engine keeps
        /// the inexpensive drag spring, but skips the per-frame screen grabs.
        /// </summary>
        public bool Economy
        {
            get { return _economy; }
            set { _economy = value; }
        }

        public bool PauseOnBattery
        {
            get { return _pauseOnBattery; }
            set { _pauseOnBattery = value; }
        }

        public bool PauseOnCall
        {
            get { return _pauseOnCall; }
            set { _pauseOnCall = value; }
        }

        public bool RespectReduceMotion
        {
            get { return _respectReduceMotion; }
            set { _respectReduceMotion = value; }
        }

        public int HotkeyModifiers
        {
            get { return _hotkeyModifiers; }
            set { _hotkeyModifiers = value; }
        }

        public int HotkeyKey
        {
            get { return _hotkeyKey; }
            set { _hotkeyKey = value; }
        }

        public bool HasHotkey
        {
            get { return _hotkeyKey != 0 && _hotkeyModifiers != 0; }
        }

        public string HotkeyLabel
        {
            get { return FormatHotkey(_hotkeyModifiers, _hotkeyKey); }
        }

        /// <summary>
        /// Executable names, glob masks ("steam*", "*.exe") and window classes
        /// ("class:Progman") the user does not want the effects in. The array
        /// is replaced as a whole, so the engine thread can read it without a
        /// lock while the settings form edits it.
        /// </summary>
        public string[] Exclude
        {
            get { return _exclude; }
        }

        /// <summary>
        /// Per-program close effects: "name.exe=0|1|2". Programs without an
        /// entry use the global Shatter/Burn choice.
        /// </summary>
        public string[] CloseOverrides
        {
            get { return _closeOverrides; }
        }

        public bool IsPortable
        {
            get { return _portable; }
        }

        public string PortablePath
        {
            get { return _portablePath; }
        }

        /// <summary>
        /// Switches between the registry and an okna.ini next to the
        /// executable. Returns false when the folder cannot be written.
        /// </summary>
        public bool SetPortable(bool enabled)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(dir)) return false;
                string path = Path.Combine(dir, PortableFileName);
                if (enabled)
                {
                    _portable = true;
                    _portablePath = path;
                    WriteIni(path);
                    return File.Exists(path);
                }

                // Write the current options to the registry first, so turning
                // portable mode off does not lose them.
                _portable = false;
                _portablePath = null;
                Save();
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Portable mode switch failed: " + ex.Message);
                return false;
            }
        }

        public bool IsExcluded(string executableName)
        {
            return IsExcluded(executableName, null);
        }

        public bool IsExcluded(string executableName, string windowClass)
        {
            string[] list = _exclude;
            if (list.Length == 0) return false;
            for (int i = 0; i < list.Length; i++)
            {
                string pattern = list[i];
                if (string.IsNullOrEmpty(pattern)) continue;
                if (pattern.StartsWith("class:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(windowClass) &&
                        WildcardMatch(pattern.Substring(6), windowClass))
                        return true;
                    continue;
                }
                if (!string.IsNullOrEmpty(executableName) &&
                    WildcardMatch(pattern, executableName))
                    return true;
            }
            return false;
        }

        /// <summary>Case-insensitive glob match supporting * and ?.</summary>
        internal static bool WildcardMatch(string pattern, string text)
        {
            if (pattern == null) return false;
            if (text == null) text = "";
            int p = 0, t = 0;
            int star = -1, mark = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length &&
                    (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
                {
                    p++;
                    t++;
                }
                else if (p < pattern.Length && pattern[p] == '*')
                {
                    star = p++;
                    mark = t;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    t = ++mark;
                }
                else
                {
                    return false;
                }
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        public void AddExclude(string pattern)
        {
            string name = CleanPattern(pattern);
            if (name == null || ContainsIgnoreCase(_exclude, name)) return;
            string[] list = _exclude;
            string[] next = new string[list.Length + 1];
            Array.Copy(list, next, list.Length);
            next[list.Length] = name;
            _exclude = next;
        }

        public void RemoveExclude(string pattern)
        {
            string[] list = _exclude;
            int index = IndexOfIgnoreCase(list, pattern);
            if (index < 0) return;
            string[] next = new string[list.Length - 1];
            Array.Copy(list, 0, next, 0, index);
            Array.Copy(list, index + 1, next, index, list.Length - index - 1);
            _exclude = next;
        }

        private static string CleanPattern(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string name = value.Trim();
            if (name.Length == 0) return null;
            if (name.StartsWith("class:", StringComparison.OrdinalIgnoreCase))
            {
                string cls = name.Substring(6).Trim();
                return cls.Length == 0 ? null : "class:" + cls;
            }
            try
            {
                string file = Path.GetFileName(name);
                if (!string.IsNullOrEmpty(file)) name = file;
            }
            catch { }
            return name;
        }

        private static bool ContainsIgnoreCase(string[] list, string value)
        {
            return IndexOfIgnoreCase(list, value) >= 0;
        }

        private static int IndexOfIgnoreCase(string[] list, string value)
        {
            if (list == null || value == null) return -1;
            for (int i = 0; i < list.Length; i++)
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>
        /// -1 when the program has no override and the global close effect
        /// applies; otherwise 0 = none, 1 = shards, 2 = fire.
        /// </summary>
        public int GetCloseEffect(string executableName)
        {
            if (string.IsNullOrEmpty(executableName)) return -1;
            string[] list = _closeOverrides;
            for (int i = 0; i < list.Length; i++)
            {
                int eq = list[i].LastIndexOf('=');
                if (eq <= 0) continue;
                string mask = list[i].Substring(0, eq);
                int effect;
                if (!int.TryParse(list[i].Substring(eq + 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out effect))
                    continue;
                if (WildcardMatch(mask, executableName))
                    return effect < 0 ? 0 : (effect > 2 ? 2 : effect);
            }
            return -1;
        }

        public void SetCloseOverride(string executableName, int effect)
        {
            string name = CleanPattern(executableName);
            if (name == null || name.StartsWith("class:", StringComparison.OrdinalIgnoreCase)) return;
            if (effect < 0) effect = 0;
            if (effect > 2) effect = 2;

            string[] list = _closeOverrides;
            List<string> next = new List<string>();
            bool replaced = false;
            for (int i = 0; i < list.Length; i++)
            {
                int eq = list[i].LastIndexOf('=');
                string mask = eq > 0 ? list[i].Substring(0, eq) : list[i];
                if (string.Equals(mask, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (!replaced) next.Add(name + "=" + effect);
                    replaced = true;
                }
                else
                {
                    next.Add(list[i]);
                }
            }
            if (!replaced) next.Add(name + "=" + effect);
            _closeOverrides = next.ToArray();
        }

        public void RemoveCloseOverride(string executableName)
        {
            string[] list = _closeOverrides;
            List<string> next = new List<string>();
            for (int i = 0; i < list.Length; i++)
            {
                int eq = list[i].LastIndexOf('=');
                string mask = eq > 0 ? list[i].Substring(0, eq) : list[i];
                if (!string.Equals(mask, executableName, StringComparison.OrdinalIgnoreCase))
                    next.Add(list[i]);
            }
            _closeOverrides = next.ToArray();
        }

        public void ClearCloseOverrides()
        {
            _closeOverrides = new string[0];
        }

        public bool FirstRun { get; set; }
        public bool StartWithWindows { get; set; }

        public string JellyLabel
        {
            get
            {
                if (_jelly < 35) return "Сдержанно";
                if (_jelly < 78) return "Упруго";
                if (_jelly < 130) return "Очень желейно";
                return "Максимально вязко";
            }
        }

        public static string FormatHotkey(int modifiers, int key)
        {
            if (key == 0 || modifiers == 0) return "Нет";
            StringBuilder sb = new StringBuilder();
            if ((modifiers & Native.MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((modifiers & Native.MOD_ALT) != 0) sb.Append("Alt+");
            if ((modifiers & Native.MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((modifiers & Native.MOD_WIN) != 0) sb.Append("Win+");
            sb.Append(KeyName(key));
            return sb.ToString();
        }

        private static string KeyName(int key)
        {
            if (key >= 0x70 && key <= 0x87) return "F" + (key - 0x6F);
            if (key >= '0' && key <= '9') return ((char)key).ToString();
            if (key >= 'A' && key <= 'Z') return ((char)key).ToString();
            switch (key)
            {
                case 0x20: return "Space";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                default: return "0x" + key.ToString("X2");
            }
        }

        public void ApplyEconomyPreset()
        {
            _economy = true;
            _releaseWobble = false;
            _maxShake = false;
            _bend = false;
            _jelly = 40;
        }

        /// <summary>Restores every option to its factory value.</summary>
        public void ResetAll()
        {
            _enabled = true;
            _jelly = 70;
            _releaseWobble = false;
            _bend = true;
            _maxShake = true;
            _shatter = true;
            _burn = false;
            _roll = true;
            _rollDirection = (int)RollDirection.Auto;
            _autoUpdate = true;
            _economy = false;
            _pauseOnBattery = false;
            _pauseOnCall = true;
            _respectReduceMotion = true;
            _hotkeyModifiers = (int)(Native.MOD_CONTROL | Native.MOD_ALT);
            _hotkeyKey = Native.VK_J;
            _exclude = new string[0];
            _closeOverrides = new string[0];
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                string portable = FindPortableFile();
                if (portable != null)
                {
                    s._portable = true;
                    s._portablePath = portable;
                    s.LoadPortable(portable);
                    s.StartWithWindows = IsAutoStartEnabled();
                    return s;
                }

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
                        v = key.GetValue("RollDirection");
                        if (v != null)
                        {
                            s.RollDirection = (RollDirection)Convert.ToInt32(v);
                        }
                        else
                        {
                            // 1.5.x only knew "sideways".
                            v = key.GetValue("RollHorizontal");
                            if (v != null) s.RollHorizontal = Convert.ToInt32(v) != 0;
                        }
                        v = key.GetValue("AutoUpdate");
                        if (v != null) s.AutoUpdate = Convert.ToInt32(v) != 0;
                        v = key.GetValue("Economy");
                        if (v != null) s.Economy = Convert.ToInt32(v) != 0;
                        v = key.GetValue("PauseOnBattery");
                        if (v != null) s.PauseOnBattery = Convert.ToInt32(v) != 0;
                        v = key.GetValue("PauseOnCall");
                        if (v != null) s.PauseOnCall = Convert.ToInt32(v) != 0;
                        v = key.GetValue("RespectReduceMotion");
                        if (v != null) s.RespectReduceMotion = Convert.ToInt32(v) != 0;
                        v = key.GetValue("HotkeyModifiers");
                        if (v != null) s.HotkeyModifiers = Convert.ToInt32(v);
                        v = key.GetValue("HotkeyKey");
                        if (v != null) s.HotkeyKey = Convert.ToInt32(v);
                        v = key.GetValue("Exclude");
                        if (v is string[])
                        {
                            string[] names = (string[])v;
                            for (int i = 0; i < names.Length; i++)
                                s.AddExclude(names[i]);
                        }
                        v = key.GetValue("CloseOverrides");
                        if (v is string[])
                            s.SetCloseOverrides((string[])v);
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

        private void SetCloseOverrides(string[] values)
        {
            if (values == null) return;
            List<string> next = new List<string>();
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (string.IsNullOrEmpty(value)) continue;
                int eq = value.LastIndexOf('=');
                if (eq <= 0) continue;
                string name = CleanPattern(value.Substring(0, eq));
                int effect;
                if (name == null || !int.TryParse(value.Substring(eq + 1), out effect)) continue;
                if (effect < 0) effect = 0;
                if (effect > 2) effect = 2;
                next.Add(name + "=" + effect);
            }
            _closeOverrides = next.ToArray();
        }

        private static string FindPortableFile()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(dir)) return null;
                string path = Path.Combine(dir, PortableFileName);
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        private void LoadPortable(string path)
        {
            Dictionary<string, string> map = ReadIni(path);
            if (map.Count == 0) return;

            Enabled = GetBool(map, "Enabled", true);
            Jelly = GetInt(map, "Jelly", 70);
            ReleaseWobble = GetBool(map, "ReleaseWobble", false);
            Bend = GetBool(map, "Bend", true);
            MaxShake = GetBool(map, "MaxShake", true);
            Shatter = GetBool(map, "Shatter", true);
            Burn = GetBool(map, "Burn", false);
            Roll = GetBool(map, "Roll", true);
            RollDirection = (RollDirection)GetInt(map, "RollDirection", (int)RollDirection.Auto);
            AutoUpdate = GetBool(map, "AutoUpdate", true);
            Economy = GetBool(map, "Economy", false);
            PauseOnBattery = GetBool(map, "PauseOnBattery", false);
            PauseOnCall = GetBool(map, "PauseOnCall", true);
            RespectReduceMotion = GetBool(map, "RespectReduceMotion", true);
            HotkeyModifiers = GetInt(map, "HotkeyModifiers", (int)(Native.MOD_CONTROL | Native.MOD_ALT));
            HotkeyKey = GetInt(map, "HotkeyKey", Native.VK_J);

            string exclusions;
            if (map.TryGetValue("Exclude", out exclusions) && !string.IsNullOrEmpty(exclusions))
                foreach (string item in exclusions.Split('|'))
                    AddExclude(item);

            string overrides;
            if (map.TryGetValue("CloseOverrides", out overrides) && !string.IsNullOrEmpty(overrides))
                SetCloseOverrides(overrides.Split('|'));

            FirstRun = GetBool(map, "FirstRun", false);
            if (Burn) Shatter = false;
        }

        private static Dictionary<string, string> ReadIni(string path)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("[") || line.StartsWith(";") || line.StartsWith("#"))
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Settings INI read failed: " + ex.Message);
            }
            return map;
        }

        private static bool GetBool(Dictionary<string, string> map, string key, bool fallback)
        {
            string value;
            if (!map.TryGetValue(key, out value)) return fallback;
            int number;
            if (int.TryParse(value, out number)) return number != 0;
            bool flag;
            if (bool.TryParse(value, out flag)) return flag;
            return fallback;
        }

        private static int GetInt(Dictionary<string, string> map, string key, int fallback)
        {
            string value;
            int number;
            return map.TryGetValue(key, out value) && int.TryParse(value, out number) ? number : fallback;
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
                            if (string.IsNullOrEmpty(path) || !File.Exists(path))
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
            if (_portable && !string.IsNullOrEmpty(_portablePath))
            {
                WriteIni(_portablePath);
                return;
            }

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
                    key.SetValue("RollDirection", _rollDirection, RegistryValueKind.DWord);
                    // Kept so an older build sharing the key still understands
                    // the choice.
                    key.SetValue("RollHorizontal", RollHorizontal ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("AutoUpdate", _autoUpdate ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Economy", _economy ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("PauseOnBattery", _pauseOnBattery ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("PauseOnCall", _pauseOnCall ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("RespectReduceMotion", _respectReduceMotion ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("HotkeyModifiers", _hotkeyModifiers, RegistryValueKind.DWord);
                    key.SetValue("HotkeyKey", _hotkeyKey, RegistryValueKind.DWord);
                    key.SetValue("Exclude", _exclude, RegistryValueKind.MultiString);
                    key.SetValue("CloseOverrides", _closeOverrides, RegistryValueKind.MultiString);
                    key.SetValue("FirstRun", 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Settings save failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Writes the current options as a portable okna.ini. Used by the
        /// export button and by the portable mode of Save.
        /// </summary>
        public void Export(string path)
        {
            WriteIni(path);
        }

        /// <summary>Reads an exported okna.ini and applies it to the running app.</summary>
        public bool Import(string path)
        {
            Dictionary<string, string> map = ReadIni(path);
            if (map.Count == 0) return false;

            Enabled = GetBool(map, "Enabled", Enabled);
            Jelly = GetInt(map, "Jelly", Jelly);
            ReleaseWobble = GetBool(map, "ReleaseWobble", ReleaseWobble);
            Bend = GetBool(map, "Bend", Bend);
            MaxShake = GetBool(map, "MaxShake", MaxShake);
            Shatter = GetBool(map, "Shatter", Shatter);
            Burn = GetBool(map, "Burn", Burn);
            Roll = GetBool(map, "Roll", Roll);
            RollDirection = (RollDirection)GetInt(map, "RollDirection", (int)RollDirection);
            AutoUpdate = GetBool(map, "AutoUpdate", AutoUpdate);
            Economy = GetBool(map, "Economy", Economy);
            PauseOnBattery = GetBool(map, "PauseOnBattery", PauseOnBattery);
            PauseOnCall = GetBool(map, "PauseOnCall", PauseOnCall);
            RespectReduceMotion = GetBool(map, "RespectReduceMotion", RespectReduceMotion);
            HotkeyModifiers = GetInt(map, "HotkeyModifiers", HotkeyModifiers);
            HotkeyKey = GetInt(map, "HotkeyKey", HotkeyKey);

            _exclude = new string[0];
            string exclusions;
            if (map.TryGetValue("Exclude", out exclusions) && !string.IsNullOrEmpty(exclusions))
                foreach (string item in exclusions.Split('|'))
                    AddExclude(item);

            _closeOverrides = new string[0];
            string overrides;
            if (map.TryGetValue("CloseOverrides", out overrides) && !string.IsNullOrEmpty(overrides))
                SetCloseOverrides(overrides.Split('|'));

            if (Burn) Shatter = false;
            return true;
        }

        private void WriteIni(string path)
        {
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; Окна — настройки. Скопируйте этот файл рядом с Okna.exe,");
                sb.AppendLine("; чтобы программа хранила настройки здесь, а не в реестре.");
                sb.AppendLine("[okna]");
                sb.AppendLine("Version=1");
                sb.AppendLine("Enabled=" + (_enabled ? 1 : 0));
                sb.AppendLine("Jelly=" + _jelly);
                sb.AppendLine("ReleaseWobble=" + (_releaseWobble ? 1 : 0));
                sb.AppendLine("Bend=" + (_bend ? 1 : 0));
                sb.AppendLine("MaxShake=" + (_maxShake ? 1 : 0));
                sb.AppendLine("Shatter=" + (_shatter ? 1 : 0));
                sb.AppendLine("Burn=" + (_burn ? 1 : 0));
                sb.AppendLine("Roll=" + (_roll ? 1 : 0));
                sb.AppendLine("RollDirection=" + _rollDirection);
                sb.AppendLine("AutoUpdate=" + (_autoUpdate ? 1 : 0));
                sb.AppendLine("Economy=" + (_economy ? 1 : 0));
                sb.AppendLine("PauseOnBattery=" + (_pauseOnBattery ? 1 : 0));
                sb.AppendLine("PauseOnCall=" + (_pauseOnCall ? 1 : 0));
                sb.AppendLine("RespectReduceMotion=" + (_respectReduceMotion ? 1 : 0));
                sb.AppendLine("HotkeyModifiers=" + _hotkeyModifiers);
                sb.AppendLine("HotkeyKey=" + _hotkeyKey);
                sb.AppendLine("Exclude=" + string.Join("|", _exclude));
                sb.AppendLine("CloseOverrides=" + string.Join("|", _closeOverrides));
                sb.AppendLine("FirstRun=0");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Log.Error("Settings export failed: " + ex.Message);
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

        /// <summary>
        /// Keeps the autostart entry pointing at the copy the user actually
        /// installed. A second copy that is merely launched (a test build, a
        /// portable stick) must not take the entry over; only an entry that
        /// points at a file which no longer exists is repaired.
        /// </summary>
        public static void RefreshAutoStartPath(string executablePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    object raw = key.GetValue(RunValue);
                    string value = raw as string;
                    if (value == null) return;
                    if (!ShouldAdoptAutoStartPath(value, executablePath)) return;
                    key.SetValue(RunValue, "\"" + executablePath + "\" --silent", RegistryValueKind.String);
                    Log.Info("Autostart entry repaired to " + executablePath);
                }
            }
            catch { }
        }

        /// <summary>Pure decision used by RefreshAutoStartPath and the tests.</summary>
        internal static bool ShouldAdoptAutoStartPath(string existingValue, string executablePath)
        {
            string existing = ParseRunPath(existingValue);
            if (string.IsNullOrEmpty(existing)) return true;
            if (string.Equals(existing, executablePath, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                return !File.Exists(existing);
            }
            catch
            {
                return false;
            }
        }

        internal static string ParseRunPath(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string path = value.Trim();
            if (path.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = path.IndexOf('"', 1);
                return end > 1 ? path.Substring(1, end - 1) : null;
            }
            int space = path.IndexOf(' ');
            return space > 0 ? path.Substring(0, space) : path;
        }
    }
}
