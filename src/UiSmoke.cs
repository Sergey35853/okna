using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Okna
{
    /// <summary>
    /// Opens the real settings window and clicks through the real tray menu
    /// without saving anything. Catches missing controls, broken handlers and
    /// crashes that the headless checks cannot see.
    /// </summary>
    internal static class UiSmoke
    {
        public static string ReportPath
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
                return Path.Combine(Path.Combine(local, "Okna"), "ui-smoke.txt");
            }
        }

        public static int Run()
        {
            Native.EnablePerMonitorDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppSettings settings = AppSettings.Load();
            settings.ResetAll();
            settings.Enabled = true;
            settings.Jelly = 70;
            settings.Bend = true;
            settings.Economy = false;
            settings.MaxShake = false;
            settings.Shatter = true;
            settings.Burn = false;
            settings.Roll = false;
            settings.AutoUpdate = false;

            Icon icon = LoadIcon();
            JellyEngine engine = new JellyEngine(settings);
            StringBuilder report = new StringBuilder();
            report.AppendLine("Okna UI smoke " + Updater.CurrentLabel);
            report.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            int failed = 0;
            try
            {
                using (SettingsForm form = new SettingsForm(settings, engine, icon, null))
                {
                    // Mark the window before any timer or close handler can
                    // run, so the smoke values never reach the registry.
                    form.SmokeMode = true;
                    form.Show();
                    Application.DoEvents();
                    string settingsReport = form.RunSmokeTest();
                    report.AppendLine(settingsReport);
                    failed += CountFailures(settingsReport);
                    form.ReallyExit = true;
                    form.Close();
                    Application.DoEvents();
                }
            }
            catch (Exception ex)
            {
                failed++;
                report.AppendLine("[FAIL] the settings window crashed: " + ex.Message);
                report.AppendLine(ex.ToString());
                Log.Error("UI smoke settings: " + ex);
            }

            try
            {
                TrayApp tray = new TrayApp(settings, engine, icon, false);
                tray.SmokeMode = true;
                string trayReport = tray.RunSmokeTest();
                report.AppendLine(trayReport);
                failed += CountFailures(trayReport);
                tray.DisposeForSmoke();
            }
            catch (Exception ex)
            {
                failed++;
                report.AppendLine("[FAIL] the tray menu crashed: " + ex.Message);
                Log.Error("UI smoke tray: " + ex);
            }

            try { icon.Dispose(); } catch { }
            try { engine.Dispose(); } catch { }

            report.AppendLine();
            report.AppendLine(failed == 0
                ? "UI smoke passed."
                : failed + " UI smoke checks failed.");

            try
            {
                string folder = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(ReportPath, report.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Log.Error("UI smoke report failed: " + ex.Message);
            }
            Log.Info("UI smoke: " + (failed == 0 ? "all passed" : failed + " failed"));
            return failed == 0 ? 0 : 1;
        }

        private static int CountFailures(string text)
        {
            int count = 0;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].TrimStart().StartsWith("[FAIL]", StringComparison.Ordinal))
                    count++;
            return count;
        }

        private static Icon LoadIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
