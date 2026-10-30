using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Okna
{
    internal static class Program
    {
        private const string MutexName = @"Local\Okna.SingleInstance";
        private const string DispatcherTitle = "Okna Dispatcher";

        [STAThread]
        private static void Main(string[] args)
        {
            Native.EnablePerMonitorDpi();
            Log.Info("Application start. " + Updater.CurrentLabel + " args=" + string.Join(" ", args));

            bool silent = false;
            bool restart = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--silent", StringComparison.OrdinalIgnoreCase))
                    silent = true;
                else if (string.Equals(args[i], "--restart", StringComparison.OrdinalIgnoreCase))
                    restart = true;
            }

            bool created;
            Mutex mutex = new Mutex(true, MutexName, out created);

            // A freshly updated copy starts while the old process is still
            // shutting down and still owns the mutex. Wait for it instead of
            // quietly handing over to the instance that is about to exit.
            if (!created && restart)
            {
                for (int i = 0; i < 150 && !created; i++)
                {
                    Thread.Sleep(100);
                    mutex.Dispose();
                    mutex = new Mutex(true, MutexName, out created);
                }
            }

            if (!created)
            {
                mutex.Dispose();
                ActivateExisting(silent);
                return;
            }

            try
            {
                Updater.Cleanup();

                // A previous run may have died while the jelly effect had a
                // foreign window hidden (layered, alpha 1). Put it back before
                // doing anything else, and hook the exit paths that bypass the
                // engine's own cleanup.
                HiddenWindowGuard.Install();
                HiddenWindowGuard.Restore();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnDomainException;

                AppSettings settings = AppSettings.Load();
                AppSettings.RefreshAutoStartPath(Application.ExecutablePath);

                Icon icon = LoadIcon();
                JellyEngine engine = new JellyEngine(settings);
                engine.Start();

                bool show = !silent || settings.FirstRun;
                TrayApp tray = new TrayApp(settings, engine, icon, show);

                DispatcherForm dispatcher = new DispatcherForm();
                dispatcher.Tray = tray;
                tray.Dispatcher = dispatcher;
                dispatcher.Show();

                try
                {
                    Application.Run(tray);
                }
                finally
                {
                    engine.Stop();
                    Log.Info("Application exit.");
                    try { if (dispatcher != null && !dispatcher.IsDisposed) dispatcher.Dispose(); } catch { }
                    try { icon.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Fatal: " + ex);
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch { }
                try { mutex.Dispose(); } catch { }
            }
        }

        private static void ActivateExisting(bool silent)
        {
            if (silent) return;
            for (int i = 0; i < 20; i++)
            {
                IntPtr hwnd = Native.FindWindow(null, DispatcherTitle);
                if (hwnd != IntPtr.Zero)
                {
                    Native.PostMessage(hwnd, Native.WM_APP_SHOW, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Thread.Sleep(100);
            }
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

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ReportError(e.Exception);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportError(e.ExceptionObject as Exception);
        }

        private static void ReportError(Exception ex)
        {
            try
            {
                Log.Error("Unhandled: " + (ex == null ? "unknown" : ex.ToString()));
            }
            catch { }
            try
            {
                MessageBox.Show(
                    "Окна столкнулись с ошибкой.\n\n" +
                    (ex == null ? "Неизвестная ошибка." : ex.Message) +
                    "\n\nПодробности записаны в %LOCALAPPDATA%\\Okna\\okna.log",
                    "Окна", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }
    }
}
