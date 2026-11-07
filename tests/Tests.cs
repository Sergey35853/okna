using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

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
            Run("program profiles parse old and new entries", ProfileParsing);
            Run("program profiles gate the effects per program", ProfileGates);
            Run("program profiles are edited as one rule", ProfileEditing);
            Run("the current preset is derived from the values", PresetIndex);
            Run("the taskbar edge is read from the work area", RollBarEdge);
            Run("DPI scaling and monitor coordinates stay exact", MonitorGeometry);
            Run("an edge press is told apart from a drag", ResizeEdgeDetection);
            Run("frameless windows report their resize borders", ResizeHitCodes);
            Run("the resize wave follows distance as well as speed", ResizeWaveStrength);
            Run("the bend strip grows only for large windows", StripHeightBudget);
            Run("the rotated log keeps its name readable", LogRotationName);
            Run("economy preset disables the costly effects", EconomyPreset);
            Run("presets leave a working effect after economy", PresetsKeepTheEffectWorking);
            Run("jelly slider reaches the extended maximum", JellyRange);
            Run("hotkey label is readable", HotkeyLabel);
            Run("sideways setting migrates to the left roll", SidewaysMigration);
            Run("roll direction rejects impossible values", DirectionRange);
            Run("autostart is not stolen by a second copy", AutoStartDecision);
            Run("autostart entry is parsed with quotes and arguments", AutoStartParsing);
            Run("settings survive an INI round-trip", IniRoundTrip);
            Run("broken INI is rejected", IniRejectsGarbage);
            Run("diagnostics report has the important sections", DiagnosticsReport);
            Run("safe mode starts after two failed starts", SafeModeDecision);
            Run("localization switches between Russian and English", LocalizationSwitch);
            Run("adaptive quality reduces and restores strip detail", AdaptiveQualityLevels);
            Run("snapshot buffers are reused", SnapshotPoolReuse);
            Run("update skip and language survive an INI round-trip", UpdateAndLanguageRoundTrip);
            Run("settings backup path follows the store", BackupPathLayout);

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

        private static void ProfileParsing()
        {
            string mask;
            int effect;
            bool bend;
            bool shake;
            bool roll;

            Check(AppSettings.ParseProfile("steam.exe=1", out mask, out effect,
                    out bend, out shake, out roll), "a legacy entry should parse");
            Check(mask == "steam.exe" && effect == 1 && bend && shake && roll,
                "a legacy entry should allow every effect");

            Check(AppSettings.ParseProfile("chrome.exe=2,1,0,0", out mask, out effect,
                    out bend, out shake, out roll), "a new entry should parse");
            Check(mask == "chrome.exe" && effect == 2 && bend && !shake && !roll,
                "the three flags were read wrongly");

            Check(AppSettings.ParseProfile("game.exe=1,0", out mask, out effect,
                    out bend, out shake, out roll), "a partial entry should parse");
            Check(!bend && shake && roll, "missing flags should default to allowed");

            Check(AppSettings.ParseProfile("x=9", out mask, out effect,
                    out bend, out shake, out roll) && effect == 2, "effect should clamp to 2");
            Check(AppSettings.ParseProfile("x=-3", out mask, out effect,
                    out bend, out shake, out roll) && effect == 0, "negative effect should clamp to 0");

            Check(!AppSettings.ParseProfile("", out mask, out effect, out bend, out shake, out roll),
                "an empty entry should be rejected");
            Check(!AppSettings.ParseProfile("=1", out mask, out effect, out bend, out shake, out roll),
                "a missing name should be rejected");
            Check(!AppSettings.ParseProfile("x=abc", out mask, out effect, out bend, out shake, out roll),
                "a non-numeric effect should be rejected");
            Check(!AppSettings.ParseProfile("no equals sign", out mask, out effect,
                    out bend, out shake, out roll), "an entry without = should be rejected");
        }

        private static void ProfileGates()
        {
            AppSettings s = new AppSettings();
            int effect;
            bool bend;
            bool shake;
            bool roll;

            Check(!s.GetAppProfile("chrome.exe", out effect, out bend, out shake, out roll),
                "no rule should mean the global settings");
            Check(s.GetCloseEffect("chrome.exe") == -1, "no rule should report -1");

            s.SetAppProfile("steam*", 2, true, false, false);
            Check(s.GetAppProfile("STEAM.EXE", out effect, out bend, out shake, out roll),
                "a wildcard rule should match");
            Check(effect == 2 && bend && !shake && !roll, "the stored flags were not applied");
            Check(s.GetCloseEffect("steamwebhelper.exe") == 2, "the wildcard should cover helpers");
            Check(!s.GetAppProfile("chrome.exe", out effect, out bend, out shake, out roll),
                "the rule must not leak to another program");

            s.SetAppProfile("chrome.exe", 0, false, true, true);
            Check(s.GetAppProfile("chrome.exe", out effect, out bend, out shake, out roll),
                "an exact rule should match");
            Check(effect == 0 && !bend && shake && roll, "the exact rule was read wrongly");
        }

        private static void ProfileEditing()
        {
            AppSettings s = new AppSettings();
            s.SetAppProfile("chrome.exe", 1, true, true, true);
            s.SetAppProfile("chrome.exe", 2, false, false, false);
            Check(s.AppProfiles.Length == 1, "replacing a rule should not duplicate it");
            Check(s.AppProfiles[0] == "chrome.exe=2,0,0,0", "the whole rule was not stored");

            // The old API writes a rule that allows every effect, so an old
            // caller cannot leave a program with the effects silently off.
            s.SetCloseOverride("chrome.exe", 1);
            Check(s.AppProfiles[0] == "chrome.exe=1,1,1,1", "the legacy setter should allow every effect");

            s.SetAppProfile(@"C:\Program Files\Steam\steam.exe", 1, true, true, true);
            Check(s.AppProfiles.Length == 2 && s.AppProfiles[1] == "steam.exe=1,1,1,1",
                "a full path should become a file name");

            s.SetAppProfile("class:Progman", 1, true, true, true);
            Check(s.AppProfiles.Length == 2, "a window class must not become a program rule");

            s.RemoveCloseOverride("chrome.exe");
            Check(s.AppProfiles.Length == 1 && s.AppProfiles[0].StartsWith("steam.exe="),
                "removal failed");
        }

        private static void PresetIndex()
        {
            AppSettings s = new AppSettings();
            s.ApplyPreset(0);
            Check(s.CurrentPresetIndex == 0, "calm preset index");
            s.ApplyPreset(1);
            Check(s.CurrentPresetIndex == 1, "balance preset index");
            s.ApplyPreset(2);
            Check(s.CurrentPresetIndex == 2, "jelly preset index");
            s.ApplyPreset(3);
            Check(s.CurrentPresetIndex == 3, "economy preset index");

            // Economy wins even at a jelly value that would otherwise be "jelly".
            s.Jelly = 130;
            Check(s.CurrentPresetIndex == 3, "economy should stay checked");
            s.LeaveEconomy();
            Check(s.CurrentPresetIndex == 2, "leaving economy should show the matching preset");
        }

        private static void RollBarEdge()
        {
            RollDirection direction;

            Native.RECT bottomWork = Rect(0, 0, 1920, 1040);
            Native.RECT bottomBar = Rect(0, 1040, 1920, 1080);
            Check(RollOverlay.ResolveBarEdge(bottomBar, bottomWork, out direction) &&
                  direction == RollDirection.Down, "a bottom bar should roll down");

            Native.RECT topWork = Rect(0, 40, 1920, 1080);
            Native.RECT topBar = Rect(0, 0, 1920, 40);
            Check(RollOverlay.ResolveBarEdge(topBar, topWork, out direction) &&
                  direction == RollDirection.Up, "a top bar should roll up");

            Native.RECT rightWork = Rect(0, 0, 1880, 1080);
            Native.RECT rightBar = Rect(1880, 0, 1920, 1080);
            Check(RollOverlay.ResolveBarEdge(rightBar, rightWork, out direction) &&
                  direction == RollDirection.Right, "a right bar should roll right");

            Native.RECT leftWork = Rect(40, 0, 1920, 1080);
            Native.RECT leftBar = Rect(0, 0, 40, 1080);
            Check(RollOverlay.ResolveBarEdge(leftBar, leftWork, out direction) &&
                  direction == RollDirection.Left, "a left bar should roll left");

            Native.RECT floating = Rect(700, 500, 1200, 540);
            Check(!RollOverlay.ResolveBarEdge(floating, bottomWork, out direction),
                "a floating bar has no edge");
            Check(!RollOverlay.ResolveBarEdge(bottomBar, new Native.RECT(), out direction),
                "an empty work area should be rejected");

            // The monitor-aware overload should use the work area, not the
            // distance between centres: this window sits far above the bar.
            Native.RECT frame = Rect(100, 100, 900, 700);
            Check(RollOverlay.ResolveDirection(frame, true, bottomBar, bottomWork,
                    RollDirection.Auto) == RollDirection.Down,
                "the work area should decide the direction");
            Check(RollOverlay.ResolveDirection(frame, true, topBar, topWork,
                    RollDirection.Auto) == RollDirection.Up,
                "a top bar with a work area should roll up");
        }

        private static void MonitorGeometry()
        {
            Check(Native.ScaleForDpi(8, 96) == 8, "96 DPI should keep 8 px");
            Check(Native.ScaleForDpi(8, 120) == 10, "125% should scale to 10 px");
            Check(Native.ScaleForDpi(8, 144) == 12, "150% should scale to 12 px");
            Check(Native.ScaleForDpi(8, 192) == 16, "200% should scale to 16 px");
            Check(Native.ScaleForDpi(8, 0) == 8, "a missing DPI should fall back to 96");

            // A monitor left of the primary one has negative coordinates.
            Native.RECT leftMonitor = Rect(-1920, 0, 0, 1080);
            Check(JellyEngine.IsResizeEdge(new Native.POINT { X = -1915, Y = 500 },
                    leftMonitor, 8), "a negative-coordinate left edge should work");
            Check(JellyEngine.IsResizeEdge(new Native.POINT { X = -5, Y = 500 },
                    leftMonitor, 8), "a negative-coordinate right edge should work");
            Check(!JellyEngine.IsResizeEdge(new Native.POINT { X = -960, Y = 500 },
                    leftMonitor, 8), "the middle of a left monitor is not an edge");

            // A taskbar on the second monitor, which starts at x=1920.
            Native.RECT secondWork = Rect(1920, 0, 3840, 1040);
            Native.RECT secondBar = Rect(1920, 1040, 3840, 1080);
            RollDirection direction;
            Check(RollOverlay.ResolveBarEdge(secondBar, secondWork, out direction) &&
                  direction == RollDirection.Down,
                "a bar on the second monitor should roll down");
            Native.RECT secondLeftWork = Rect(1920, 0, 3840, 1080);
            Native.RECT secondLeftBar = Rect(1920, 0, 1960, 1080);
            Check(RollOverlay.ResolveBarEdge(secondLeftBar, secondLeftWork, out direction) &&
                  direction == RollDirection.Left,
                "a vertical bar on the second monitor should roll left");
        }

        private static void ResizeEdgeDetection()
        {
            Native.RECT rect = Rect(100, 100, 500, 400);
            Check(JellyEngine.IsResizeEdge(new Native.POINT { X = 105, Y = 250 }, rect, 10),
                "the left border should count as an edge");
            Check(JellyEngine.IsResizeEdge(new Native.POINT { X = 495, Y = 250 }, rect, 10),
                "the right border should count as an edge");
            Check(JellyEngine.IsResizeEdge(new Native.POINT { X = 250, Y = 105 }, rect, 10),
                "the top border should count as an edge");
            Check(JellyEngine.IsResizeEdge(new Native.POINT { X = 250, Y = 395 }, rect, 10),
                "the bottom border should count as an edge");
            Check(JellyEngine.IsResizeEdge(new Native.POINT { X = 105, Y = 105 }, rect, 10),
                "a corner should count as an edge");
            Check(!JellyEngine.IsResizeEdge(new Native.POINT { X = 300, Y = 250 }, rect, 10),
                "the middle of the window is a drag, not a resize");
            Check(!JellyEngine.IsResizeEdge(new Native.POINT { X = 50, Y = 250 }, rect, 10),
                "a point outside the margin is not an edge");
            Check(!JellyEngine.IsResizeEdge(new Native.POINT { X = 105, Y = 250 },
                    new Native.RECT(), 10), "a degenerate rectangle should be rejected");
        }

        private static void ResizeHitCodes()
        {
            for (int hit = 10; hit <= 17; hit++)
                Check(JellyEngine.IsResizeHit(hit), "hit " + hit + " should be a resize border");
            Check(!JellyEngine.IsResizeHit(1), "HTCLIENT is not a resize border");
            Check(!JellyEngine.IsResizeHit(2), "HTCAPTION is not a resize border");
            Check(!JellyEngine.IsResizeHit(0), "HTNOWHERE is not a resize border");
            Check(!JellyEngine.IsResizeHit(18), "an unknown hit is not a resize border");
        }

        private static void ResizeWaveStrength()
        {
            double slowLong = JellyEngine.ResizeAmplitude(10, 300, 1.1);
            double fastShort = JellyEngine.ResizeAmplitude(400, 40, 1.1);
            Check(slowLong > 6.0, "a slow long drag should still be visible");
            Check(slowLong >= fastShort - 0.01,
                "distance should not lose to a short flick");
            Check(JellyEngine.ResizeAmplitude(200, 100, 1.1) >
                  JellyEngine.ResizeAmplitude(200, 50, 1.1),
                "a longer drag should be stronger");
            Check(JellyEngine.ResizeAmplitude(200, 100, 2.4) >
                  JellyEngine.ResizeAmplitude(200, 100, 1.1),
                "the jelly slider should scale the wave");
            Check(JellyEngine.ResizeAmplitude(4000, 4000, 2.4) <= 14.0,
                "the wave should stay inside the amplitude cap");
        }

        private static void StripHeightBudget()
        {
            Check(BendOverlay.ComputeStripHeight(1920, 1200) == 1,
                "a 1920x1200 window should keep the pixel-perfect bend");
            Check(BendOverlay.ComputeStripHeight(2560, 1440) == 2,
                "a 2560x1440 window should use two-pixel strips");
            Check(BendOverlay.ComputeStripHeight(3840, 2160) == 4,
                "a 4K window should use four-pixel strips");
            Check(BendOverlay.ComputeStripHeight(7680, 4320) <= 4,
                "an 8K window should stay inside the strip cap");
            Check(BendOverlay.ComputeStripHeight(800, 600) == 1,
                "a small window should keep one-pixel strips");
            Check(BendOverlay.ComputeStripHeight(1920, 0) == 1,
                "a zero height should not divide by zero");
        }

        private static void LogRotationName()
        {
            Check(Log.OldPathFor(@"C:\Users\me\AppData\Local\Okna\okna.log") ==
                @"C:\Users\me\AppData\Local\Okna\okna.old.log",
                "the rotated log should sit next to the current one");
            Check(Log.OldPathFor("okna.log") == "okna.old.log",
                "a relative log name should stay relative");
            Check(Log.OldPathFor("other.txt") == "other.old.txt",
                "the extension should be kept");
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

        private static void PresetsKeepTheEffectWorking()
        {
            AppSettings s = new AppSettings();

            // "Экономно" turns the live effects off on purpose.
            s.ApplyPreset(3);
            Check(s.Economy, "economy preset should be on");
            Check(!s.Bend && !s.MaxShake && !s.ReleaseWobble, "economy should switch the effects off");

            // Choosing "Желе" afterwards must bring them back: the window used
            // to stay rigid while the slider said "Максимально вязко".
            s.ApplyPreset(2);
            Check(!s.Economy, "jelly preset should leave economy");
            Check(s.Bend, "jelly preset should enable the bend");
            Check(s.MaxShake, "jelly preset should enable the shake");
            Check(s.ReleaseWobble, "jelly preset should enable the release wobble");
            Check(s.Jelly == 130, "jelly preset value");

            // Dragging the slider is a manual override of economy.
            s.ApplyPreset(3);
            s.LeaveEconomy();
            Check(!s.Economy, "the slider should leave economy");
            Check(s.Bend && s.MaxShake, "the slider should restore the live effects");
            Check(s.Jelly == 40, "leaving economy should not change the value");

            s.ApplyPreset(0);
            Check(!s.Economy && s.Bend && s.MaxShake && !s.ReleaseWobble, "calm preset should work");
            Check(s.Jelly == 18, "calm preset value");

            s.ApplyPreset(1);
            Check(!s.Economy && s.Bend && s.MaxShake && !s.ReleaseWobble, "balance preset should work");
            Check(s.Jelly == 70, "balance preset value");
        }

        private static void JellyRange()
        {
            AppSettings s = new AppSettings();
            s.Jelly = 200;
            Check(s.Jelly == 200, "the extended maximum should be allowed");
            Check(s.JellyLabel == "Максимально вязко", "the top of the range should be labelled");
            s.Jelly = 250;
            Check(s.Jelly == 200, "values above the maximum should clamp");
            s.Jelly = -5;
            Check(s.Jelly == 0, "negative values should clamp to zero");
            s.Jelly = 70;
            Check(s.JellyLabel == "Упруго", "the default should keep its label");
            Check(s.JellyFactor > 1.0 && s.JellyFactor < 1.2, "the default factor should stay near 1.1");
            s.Jelly = 200;
            Check(s.JellyFactor > 2.0, "the top of the range should be noticeably stronger");
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

        private static void SafeModeDecision()
        {
            Check(!StartupGuard.Decide(0), "a clean start is not safe mode");
            Check(!StartupGuard.Decide(1), "one failed start is not enough");
            Check(StartupGuard.Decide(2), "two failed starts enter safe mode");
            Check(StartupGuard.Decide(9), "more failures stay in safe mode");
        }

        private static void LocalizationSwitch()
        {
            Loc.Configure((int)UiLanguage.Russian);
            Check(Loc.T("Готово") == "Готово", "Russian keeps the source text");
            Check(Loc.T("Желеобразные окна: вкл") == "Желеобразные окна: вкл",
                "Russian keeps the tray text");

            Loc.Configure((int)UiLanguage.English);
            Check(Loc.T("Готово") == "Done", "button text is translated");
            Check(Loc.T("Активно") == "Active", "status text is translated");
            Check(Loc.T("Желеобразные окна: вкл") == "Jelly windows: on",
                "tray text is translated");
            Check(Loc.Status("Исключение: steam.exe") == "Excluded: steam.exe",
                "the exclusion prefix is translated");
            Check(Loc.T("Неизвестная строка") == "Неизвестная строка",
                "unknown text stays readable instead of disappearing");

            using (Label label = new Label())
            {
                label.Text = "Готово";
                Loc.Apply(label);
                Check(label.Text == "Done", "a control tree is translated");
                Loc.Configure((int)UiLanguage.Russian);
                Loc.Apply(label);
                Check(label.Text == "Готово", "the original text comes back");
            }
            Loc.Configure((int)UiLanguage.Russian);
        }

        private static void AdaptiveQualityLevels()
        {
            AdaptiveQuality.Reset();
            Check(AdaptiveQuality.Level == AdaptiveQuality.Best, "quality starts at best");
            Check(AdaptiveQuality.StripHeight(1, AdaptiveQuality.Best) == 1,
                "the best level keeps the base strip");
            Check(AdaptiveQuality.StripHeight(1, AdaptiveQuality.Reduced) == 2,
                "the reduced level doubles the strip");
            Check(AdaptiveQuality.StripHeight(1, AdaptiveQuality.Lowest) == 4,
                "the lowest level uses four rows");
            Check(AdaptiveQuality.StripHeight(4, AdaptiveQuality.Lowest) == 12,
                "the strip never grows past the hard limit");

            for (int i = 0; i < 30; i++) AdaptiveQuality.Sample(40, i * 0.1);
            Check(AdaptiveQuality.Level == AdaptiveQuality.Lowest,
                "sustained slow frames drop to the lowest level");

            for (int i = 0; i < 90; i++) AdaptiveQuality.Sample(5, 3.0 + i * 0.1);
            Check(AdaptiveQuality.Level == AdaptiveQuality.Best,
                "fast frames restore the best level");
            AdaptiveQuality.Reset();
        }

        private static void SnapshotPoolReuse()
        {
            ShotPool.Clear();
            Bitmap first = ShotPool.Rent(64, 48, PixelFormat.Format32bppRgb);
            Check(first.Width == 64 && first.Height == 48, "the rented bitmap has the requested size");
            ShotPool.Return(first);

            Bitmap second = ShotPool.Rent(64, 48, PixelFormat.Format32bppRgb);
            Check(ReferenceEquals(first, second), "a returned buffer is reused");
            Bitmap other = ShotPool.Rent(32, 32, PixelFormat.Format32bppRgb);
            Check(!ReferenceEquals(second, other), "a different size gets a fresh buffer");

            ShotPool.Return(second);
            ShotPool.Return(other);
            ShotPool.Clear();
        }

        private static void UpdateAndLanguageRoundTrip()
        {
            string path = Path.Combine(TempDir, "okna-test-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                AppSettings a = new AppSettings();
                a.SkippedVersion = "1.8.0";
                a.Language = (int)UiLanguage.English;
                a.Export(path);

                AppSettings b = new AppSettings();
                Check(b.Import(path), "import rejected a file written by export");
                Check(b.SkippedVersion == "1.8.0", "the skipped version round-trips");
                Check(b.Language == (int)UiLanguage.English, "the language round-trips");
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static void BackupPathLayout()
        {
            Check(AppSettings.BackupPathFor(null) == AppSettings.BackupPath,
                "without a store the global backup path is used");
            string portable = Path.Combine(TempDir, "okna.ini");
            Check(AppSettings.BackupPathFor(portable) == Path.Combine(TempDir, "okna.bak.ini"),
                "a portable store keeps its backup next to the INI");
        }
    }
}
