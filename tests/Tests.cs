using System;
using System.Drawing;
using System.IO;

namespace Okna.Tests
{
    /// <summary>
    /// Headless checks for the parts of the engine that can be verified
    /// without a desktop: roll direction, roll geometry, taskbar targeting,
    /// exclusion masks, close overrides, the autostart decision and the
    /// portable settings file. Run with test.ps1; the exit code is 1 when
    /// anything fails, so CI notices.
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Okna tests");

            Run("roll direction follows the taskbar edge", RollDirectionFollowsTaskbar);
            Run("forced roll direction is kept", ForcedRollDirection);
            Run("roll geometry is monotone in all four directions", RollGeometryMonotone);
            Run("roll geometry stays inside the window", RollGeometryInsideFrame);
            Run("taskbar target is clamped into the bar", TargetPointClamped);
            Run("exclusion globs match programs", ExclusionGlobs);
            Run("window classes can be excluded", ExclusionClasses);
            Run("exclusion entries are cleaned and deduplicated", ExclusionBookkeeping);
            Run("close overrides pick the first matching mask", CloseOverrides);
            Run("close overrides can be replaced and removed", CloseOverrideEditing);
            Run("economy preset disables the costly effects", EconomyPreset);
            Run("hotkey label is readable", HotkeyLabel);
            Run("sideways setting migrates to the left roll", SidewaysMigration);
            Run("roll direction rejects impossible values", DirectionRange);
            Run("autostart is not stolen by a second copy", AutoStartDecision);
            Run("autostart entry is parsed with quotes and arguments", AutoStartParsing);
            Run("settings survive an INI round-trip", IniRoundTrip);
            Run("broken INI is rejected", IniRejectsGarbage);
            Run("diagnostics report has the important sections", DiagnosticsReport);

            Console.WriteLine();
            Console.WriteLine(_failed == 0
                ? "All " + _passed + " checks passed."
                : _failed + " of " + (_passed + _failed) + " checks failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("  ok   " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("  FAIL " + name);
                Console.WriteLine("       " + ex.Message);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Near(double actual, double expected, double tolerance, string message)
        {
            if (Math.Abs(actual - expected) > tolerance)
                throw new Exception(message + ": expected " + expected + ", got " + actual);
        }

        /// <summary>
        /// A writable folder next to the test executable. The system temp
        /// folder is not always writable for the sandboxed process.
        /// </summary>
        private static string TempDir
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-tmp");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static Native.RECT Rect(int left, int top, int right, int bottom)
        {
            Native.RECT r = new Native.RECT();
            r.Left = left;
            r.Top = top;
            r.Right = right;
            r.Bottom = bottom;
            return r;
        }

        private static void RollDirectionFollowsTaskbar()
        {
            Native.RECT frame = Rect(400, 200, 1400, 900);
            Native.RECT bottom = Rect(0, 1400, 2560, 1480);
            Native.RECT top = Rect(0, 0, 2560, 48);
            Native.RECT left = Rect(0, 0, 60, 1440);
            Native.RECT right = Rect(2500, 0, 2560, 1440);

            Check(RollOverlay.ResolveDirection(frame, true, bottom, RollDirection.Auto) == RollDirection.Down,
                "bottom bar should roll down");
            Check(RollOverlay.ResolveDirection(frame, true, top, RollDirection.Auto) == RollDirection.Up,
                "top bar should roll up");
            Check(RollOverlay.ResolveDirection(frame, true, left, RollDirection.Auto) == RollDirection.Left,
                "left bar should roll left, towards the bar");
            Check(RollOverlay.ResolveDirection(frame, true, right, RollDirection.Auto) == RollDirection.Right,
                "right bar should roll right, towards the bar");
            Check(RollOverlay.ResolveDirection(frame, false, new Native.RECT(), RollDirection.Auto) == RollDirection.Down,
                "no taskbar should fall back to down");
        }

        private static void ForcedRollDirection()
        {
            Native.RECT frame = Rect(400, 200, 1400, 900);
            Native.RECT bottom = Rect(0, 1400, 2560, 1480);
            RollDirection[] all = new RollDirection[]
            {
                RollDirection.Down, RollDirection.Up, RollDirection.Left, RollDirection.Right
            };
            for (int i = 0; i < all.Length; i++)
                Check(RollOverlay.ResolveDirection(frame, true, bottom, all[i]) == all[i],
                    "forced " + all[i] + " was overridden");
        }

        private static void RollGeometryMonotone()
        {
            Native.RECT frame = Rect(100, 100, 900, 700);
            RollDirection[] all = new RollDirection[]
            {
                RollDirection.Down, RollDirection.Up, RollDirection.Left, RollDirection.Right
            };

            for (int d = 0; d < all.Length; d++)
            {
                RollDirection direction = all[d];
                bool horizontal = RollOverlay.IsHorizontal(direction);
                double previousCentre = 0;
                double previousFlat = double.MaxValue;
                double previousThickness = -1;
                bool first = true;

                for (int step = 0; step <= 50; step++)
                {
                    double p = step / 50.0;
                    RectangleF tube, flat, flatSource, tubeSource;
                    bool isHorizontal;
                    RollOverlay.ComputeRollGeometry(frame, direction, p,
                        out tube, out flat, out flatSource, out tubeSource, out isHorizontal);
                    Check(isHorizontal == horizontal, direction + ": horizontal flag is wrong");

                    double centre = horizontal ? tube.Left + tube.Width * 0.5 : tube.Top + tube.Height * 0.5;
                    double flatSize = horizontal ? flat.Width : flat.Height;
                    double thickness = horizontal ? tube.Width : tube.Height;

                    if (!first)
                    {
                        switch (direction)
                        {
                            case RollDirection.Down:
                                Check(centre >= previousCentre - 0.01, "down: tube should move down");
                                break;
                            case RollDirection.Up:
                                Check(centre <= previousCentre + 0.01, "up: tube should move up");
                                break;
                            case RollDirection.Left:
                                Check(centre <= previousCentre + 0.01, "left: tube should move left");
                                break;
                            case RollDirection.Right:
                                Check(centre >= previousCentre - 0.01, "right: tube should move right");
                                break;
                        }
                        Check(flatSize <= previousFlat + 0.01,
                            direction + ": the flat part should only shrink (p=" + p + ")");
                        Check(thickness >= previousThickness - 0.01,
                            direction + ": the tube should only grow (p=" + p + ")");
                    }

                    if (Math.Abs(p - 1.0) < 0.001)
                        Check(flatSize <= 0.01, direction + ": nothing should be left flat at the end");

                    previousCentre = centre;
                    previousFlat = flatSize;
                    previousThickness = thickness;
                    first = false;
                }
            }
        }

        private static void RollGeometryInsideFrame()
        {
            Native.RECT frame = Rect(100, 100, 900, 700);
            RollDirection[] all = new RollDirection[]
            {
                RollDirection.Down, RollDirection.Up, RollDirection.Left, RollDirection.Right
            };

            for (int d = 0; d < all.Length; d++)
            {
                RollDirection direction = all[d];
                bool horizontal = RollOverlay.IsHorizontal(direction);
                for (int step = 0; step <= 40; step++)
                {
                    double p = step / 40.0;
                    RectangleF tube, flat, flatSource, tubeSource;
                    bool ignored;
                    RollOverlay.ComputeRollGeometry(frame, direction, p,
                        out tube, out flat, out flatSource, out tubeSource, out ignored);

                    Check(tube.Left >= frame.Left - 0.01 && tube.Right <= frame.Right + 0.01,
                        direction + ": tube leaves the frame horizontally at p=" + p);
                    Check(tube.Top >= frame.Top - 0.01 && tube.Bottom <= frame.Bottom + 0.01,
                        direction + ": tube leaves the frame vertically at p=" + p);
                    Check(flat.Left >= frame.Left - 0.01 && flat.Right <= frame.Right + 0.01,
                        direction + ": flat part leaves the frame horizontally at p=" + p);
                    Check(flat.Top >= frame.Top - 0.01 && flat.Bottom <= frame.Bottom + 0.01,
                        direction + ": flat part leaves the frame vertically at p=" + p);
                    Check(flat.Width >= -0.01 && flat.Height >= -0.01,
                        direction + ": flat part has a negative size at p=" + p);

                    if (!horizontal)
                    {
                        bool down = direction == RollDirection.Down;
                        if (down) Check(flat.Top >= tube.Bottom - 0.01, "down: flat overlaps the tube");
                        else Check(flat.Bottom <= tube.Top + 0.01, "up: flat overlaps the tube");
                    }
                    else
                    {
                        bool left = direction == RollDirection.Left;
                        if (left) Check(flat.Right <= tube.Left + 0.01, "left: flat overlaps the tube");
                        else Check(flat.Left >= tube.Right - 0.01, "right: flat overlaps the tube");
                    }
                }
            }
        }

        private static void TargetPointClamped()
        {
            Native.RECT frame = Rect(400, 200, 1400, 900);
            float x, y;

            Native.RECT bar = Rect(0, 1400, 2560, 1480);
            RollOverlay.TargetPoint(IntPtr.Zero, frame, true, bar, RollDirection.Down, out x, out y);
            Check(x >= bar.Left + 23.9 && x <= bar.Right - 23.9, "horizontal target x is outside the bar");
            Check(y >= bar.Top && y <= bar.Bottom, "horizontal target y is outside the bar");
            Near(x, 900, 0.01, "horizontal target should sit under the window centre");
            Near(y, 1440, 0.01, "horizontal target should sit on the bar's centre line");

            Native.RECT farLeft = Rect(0, 200, 400, 900);
            RollOverlay.TargetPoint(IntPtr.Zero, farLeft, true, bar, RollDirection.Down, out x, out y);
            Check(x >= bar.Left + 23.9, "target was not clamped away from the bar's left corner");

            Native.RECT side = Rect(2500, 0, 2560, 1440);
            RollOverlay.TargetPoint(IntPtr.Zero, frame, true, side, RollDirection.Left, out x, out y);
            Check(x >= side.Left && x <= side.Right, "vertical target x is outside the bar");
            Check(y >= side.Top + 23.9 && y <= side.Bottom - 23.9, "vertical target y is outside the bar");

            RollOverlay.TargetPoint(IntPtr.Zero, frame, false, new Native.RECT(), RollDirection.Down, out x, out y);
            Check(y > 0, "without a taskbar the target should still be on screen");
        }

        private static void ExclusionGlobs()
        {
            Check(AppSettings.WildcardMatch("steam*", "steam.exe"), "steam* should match steam.exe");
            Check(AppSettings.WildcardMatch("steam*", "STEAM.EXE"), "globs should ignore case");
            Check(AppSettings.WildcardMatch("*chrome*", "GoogleChrome.exe"), "inner star should match");
            Check(AppSettings.WildcardMatch("a?c.exe", "abc.exe"), "? should match one character");
            Check(!AppSettings.WildcardMatch("steam*", "notsteam.exe"), "prefix should not match in the middle");
            Check(!AppSettings.WildcardMatch("a?c.exe", "ac.exe"), "? should require a character");

            AppSettings s = new AppSettings();
            s.AddExclude("steam*");
            Check(s.IsExcluded("steamwebhelper.exe"), "wildcard exclusion was not applied");
            Check(!s.IsExcluded("chrome.exe"), "unrelated program was excluded");
        }

        private static void ExclusionClasses()
        {
            AppSettings s = new AppSettings();
            s.AddExclude("class:Progman");
            Check(s.IsExcluded("explorer.exe", "Progman"), "window class should be excluded");
            Check(!s.IsExcluded("explorer.exe", "CabinetWClass"), "other classes should not be excluded");
            Check(!s.IsExcluded("progman.exe"), "a class mask must not match an executable name");

            s.AddExclude("class:Shell_*");
            Check(s.IsExcluded(null, "Shell_TrayWnd"), "class globs should work");
        }

        private static void ExclusionBookkeeping()
        {
            AppSettings s = new AppSettings();
            s.AddExclude(@"C:\Program Files\Steam\steam.exe");
            Check(s.Exclude.Length == 1 && s.Exclude[0] == "steam.exe", "full path should become a file name");
            s.AddExclude("STEAM.EXE");
            Check(s.Exclude.Length == 1, "duplicate exclusions should be ignored");
            s.AddExclude("   ");
            Check(s.Exclude.Length == 1, "empty exclusions should be ignored");
            s.AddExclude("class: Progman ");
            Check(s.Exclude.Length == 2 && s.Exclude[1] == "class:Progman", "class entry should be tidied");
            s.RemoveExclude("steam.exe");
            Check(s.Exclude.Length == 1 && s.Exclude[0] == "class:Progman", "removal failed");
        }

        private static void CloseOverrides()
        {
            AppSettings s = new AppSettings();
            Check(s.GetCloseEffect("chrome.exe") == -1, "no override should mean the global effect");
            s.SetCloseOverride("steam.exe", 1);
            Check(s.GetCloseEffect("STEAM.EXE") == 1, "exact override was not found");
            Check(s.GetCloseEffect("steamwebhelper.exe") == -1, "override must not leak to another program");

            s.SetCloseOverride("st*", 2);
            Check(s.GetCloseEffect("steam.exe") == 1, "the first matching mask should win");
            Check(s.GetCloseEffect("studio.exe") == 2, "wildcard override was not applied");

            s.SetCloseOverride("chrome.exe", 9);
            Check(s.GetCloseEffect("chrome.exe") == 2, "effect should be clamped to 2");
        }

        private static void CloseOverrideEditing()
        {
            AppSettings s = new AppSettings();
            s.SetCloseOverride("chrome.exe", 1);
            s.SetCloseOverride("chrome.exe", 2);
            Check(s.CloseOverrides.Length == 1, "replacing an override should not duplicate it");
            Check(s.GetCloseEffect("chrome.exe") == 2, "replacement value was not stored");
            s.RemoveCloseOverride("chrome.exe");
            Check(s.CloseOverrides.Length == 0, "removal failed");
        }

        private static void EconomyPreset()
        {
            AppSettings s = new AppSettings();
            s.Economy = false;
            s.ReleaseWobble = true;
            s.MaxShake = true;
            s.Bend = true;
            s.Jelly = 92;
            s.ApplyEconomyPreset();
            Check(s.Economy, "economy flag was not set");
            Check(!s.ReleaseWobble, "wobble should be off");
            Check(!s.MaxShake, "shake should be off");
            Check(!s.Bend, "bend should be off");
            Check(s.Jelly == 40, "jelly should settle at 40");
        }

        private static void HotkeyLabel()
        {
            int mods = (int)(Native.MOD_CONTROL | Native.MOD_ALT);
            Check(AppSettings.FormatHotkey(mods, Native.VK_J) == "Ctrl+Alt+J", "default hotkey label is wrong");
            Check(AppSettings.FormatHotkey(0, Native.VK_J) == "Нет", "a hotkey without modifiers is off");
            Check(AppSettings.FormatHotkey(mods, 0) == "Нет", "a hotkey without a key is off");
            Check(AppSettings.FormatHotkey((int)(Native.MOD_CONTROL | Native.MOD_SHIFT), 0x70) == "Ctrl+Shift+F1",
                "F-keys should be labelled");
        }

        private static void SidewaysMigration()
        {
            AppSettings s = new AppSettings();
            s.RollDirection = RollDirection.Up;
            s.RollHorizontal = true;
            Check(s.RollDirection == RollDirection.Left, "sideways should become a left roll");
            s.RollHorizontal = false;
            Check(s.RollDirection == RollDirection.Down, "vertical should become a down roll");
        }

        private static void DirectionRange()
        {
            AppSettings s = new AppSettings();
            s.RollDirection = (RollDirection)99;
            Check(s.RollDirection == RollDirection.Auto, "impossible direction should fall back to auto");
            s.RollDirection = (RollDirection)(-4);
            Check(s.RollDirection == RollDirection.Auto, "negative direction should fall back to auto");
        }

        private static void AutoStartDecision()
        {
            string missing = Path.Combine(TempDir, "no-such-folder", "Okna.exe");
            Check(AppSettings.ShouldAdoptAutoStartPath("\"" + missing + "\" --silent", @"C:\Apps\Okna.exe"),
                "a stale autostart entry should be repaired");

            Check(!AppSettings.ShouldAdoptAutoStartPath(
                    "\"C:\\Apps\\Okna.exe\" --silent", "C:\\Apps\\Okna.exe"),
                "the same path should be left alone");

            string existing = Path.Combine(TempDir, "other-copy.exe");
            File.WriteAllText(existing, "x");
            try
            {
                Check(!AppSettings.ShouldAdoptAutoStartPath(
                        "\"" + existing + "\" --silent", @"C:\Apps\Okna.exe"),
                    "a launch of another copy must not take over autostart");
            }
            finally
            {
                try { File.Delete(existing); } catch { }
            }

            Check(AppSettings.ShouldAdoptAutoStartPath(null, @"C:\Apps\Okna.exe"),
                "an empty entry should be adopted");
        }

        private static void AutoStartParsing()
        {
            Check(AppSettings.ParseRunPath("\"C:\\Program Files\\Okna\\Okna.exe\" --silent") ==
                @"C:\Program Files\Okna\Okna.exe", "quoted path was parsed wrongly");
            Check(AppSettings.ParseRunPath("C:\\Okna\\Okna.exe --silent") ==
                @"C:\Okna\Okna.exe", "unquoted path was parsed wrongly");
            Check(AppSettings.ParseRunPath("") == null, "empty value should have no path");
        }

        private static void IniRoundTrip()
        {
            string path = Path.Combine(TempDir, "okna-test-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                AppSettings a = new AppSettings();
                a.Enabled = false;
                a.Jelly = 33;
                a.ReleaseWobble = true;
                a.Bend = false;
                a.MaxShake = false;
                a.Shatter = false;
                a.Burn = true;
                a.Roll = false;
                a.RollDirection = RollDirection.Up;
                a.AutoUpdate = false;
                a.Economy = true;
                a.PauseOnBattery = true;
                a.PauseOnCall = false;
                a.RespectReduceMotion = false;
                a.HotkeyModifiers = (int)(Native.MOD_CONTROL | Native.MOD_SHIFT);
                a.HotkeyKey = 0x70;
                a.AddExclude("steam*");
                a.AddExclude("class:Progman");
                a.SetCloseOverride("chrome.exe", 1);
                a.Export(path);

                AppSettings b = new AppSettings();
                Check(b.Import(path), "import rejected a file written by export");
                Check(!b.Enabled, "enabled flag");
                Check(b.Jelly == 33, "jelly");
                Check(b.ReleaseWobble, "wobble");
                Check(!b.Bend, "bend");
                Check(!b.MaxShake, "shake");
                Check(!b.Shatter, "shatter");
                Check(b.Burn, "burn");
                Check(!b.Roll, "roll");
                Check(b.RollDirection == RollDirection.Up, "direction");
                Check(!b.AutoUpdate, "auto update");
                Check(b.Economy, "economy");
                Check(b.PauseOnBattery, "battery pause");
                Check(!b.PauseOnCall, "call pause");
                Check(!b.RespectReduceMotion, "reduce motion");
                Check(b.HotkeyKey == 0x70, "hotkey key");
                Check(b.HasHotkey, "hotkey should be enabled");
                Check(b.IsExcluded("steamwebhelper.exe"), "exclusion round-trip");
                Check(b.IsExcluded(null, "Progman"), "class exclusion round-trip");
                Check(b.GetCloseEffect("chrome.exe") == 1, "close override round-trip");
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static void IniRejectsGarbage()
        {
            string path = Path.Combine(TempDir, "okna-test-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                File.WriteAllText(path, "not an okna file\n");
                AppSettings s = new AppSettings();
                Check(!s.Import(path), "garbage should be rejected");
                Check(!s.Import(Path.Combine(TempDir, "okna-missing-" + Guid.NewGuid().ToString("N") + ".ini")),
                    "a missing file should be rejected");
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static void DiagnosticsReport()
        {
            AppSettings s = new AppSettings();
            string report = Diagnostics.BuildReport(s, null, "Ctrl+Alt+J — тест");
            Check(report.Contains("Версия сборки:"), "the report should name the build");
            Check(report.Contains("Настройки:"), "the report should list the settings");
            Check(report.Contains("Экраны:"), "the report should list the screens");
            Check(report.Contains("Панель задач:"), "the report should list the taskbars");
            Check(report.Contains("Журнал"), "the report should include the log tail");
            Check(report.Contains("Ctrl+Alt+J — тест"), "the report should include the hotkey status");
        }
    }
}
