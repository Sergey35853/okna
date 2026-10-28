using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Okna
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly AppSettings _settings;
        private readonly JellyEngine _engine;
        private readonly Icon _icon;
        private readonly NotifyIcon _notify;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly bool _showOnStart;
        private SettingsForm _form;
        private bool _exiting;
        private Timer _startTimer;

        private ToolStripMenuItem _updateItem;
        private ToolStripMenuItem _checkItem;
        private ToolStripMenuItem _autoUpdateItem;
        private Timer _updateTimer;
        private Timer _firstCheck;
        private Timer _applyTimer;
        private UpdateInfo _ready;
        private bool _checking;
        private int _applyAttempts;

        public DispatcherForm Dispatcher { get; set; }

        public TrayApp(AppSettings settings, JellyEngine engine, Icon icon, bool showOnStart)
        {
            _settings = settings;
            _engine = engine;
            _icon = icon;
            _showOnStart = showOnStart;

            _menu = new ContextMenuStrip();
            _menu.Font = Theme.FontBody;
            _menu.BackColor = Theme.Card;
            _menu.ForeColor = Theme.Text;
            _menu.ShowImageMargin = false;
            _menu.Padding = new Padding(2);
            _menu.Renderer = new ToolStripProfessionalRenderer(new JellyColorTable());

            ToolStripLabel header = new ToolStripLabel("ОКНА");
            header.Font = new Font(Theme.FontSmall, FontStyle.Bold);
            header.ForeColor = Theme.Accent;
            header.Margin = new Padding(6, 3, 6, 3);
            _menu.Items.Add(header);
            _menu.Items.Add(new ToolStripSeparator());

            _toggleItem = NewItem("");
            _toggleItem.Click += delegate
            {
                _settings.Enabled = !_settings.Enabled;
                _settings.Save();
                UpdateMenu();
                if (_form != null && !_form.IsDisposed) _form.SyncFromSettings();
            };
            _menu.Items.Add(_toggleItem);

            ToolStripMenuItem settingsItem = NewItem("Настройки…");
            settingsItem.Click += delegate { ShowSettings(); };
            _menu.Items.Add(settingsItem);

            ToolStripMenuItem shakeItem = NewItem("Потрясти активное окно");
            shakeItem.Click += delegate { _engine.RequestShake(); };
            _menu.Items.Add(shakeItem);

            _menu.Items.Add(new ToolStripSeparator());

            _startupItem = NewItem("");
            _startupItem.Click += delegate
            {
                bool enable = !AppSettings.IsAutoStartEnabled();
                AppSettings.SetAutoStart(enable, Application.ExecutablePath);
                _settings.StartWithWindows = enable;
                _settings.Save();
                UpdateMenu();
                if (_form != null && !_form.IsDisposed) _form.SyncFromSettings();
            };
            _menu.Items.Add(_startupItem);

            _menu.Items.Add(new ToolStripSeparator());

            _updateItem = NewItem("");
            _updateItem.Visible = false;
            _updateItem.ForeColor = Theme.Accent;
            _updateItem.Click += delegate { ApplyUpdateNow(); };
            _menu.Items.Add(_updateItem);

            _checkItem = NewItem("Проверить обновления…");
            _checkItem.Click += delegate { CheckUpdates(true); };
            _menu.Items.Add(_checkItem);

            _autoUpdateItem = NewItem("");
            _autoUpdateItem.Click += delegate
            {
                _settings.AutoUpdate = !_settings.AutoUpdate;
                _settings.Save();
                UpdateMenu();
                if (_settings.AutoUpdate && _ready != null) ScheduleApply();
            };
            _menu.Items.Add(_autoUpdateItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = NewItem("Выход");
            exitItem.Click += delegate { ExitApp(); };
            _menu.Items.Add(exitItem);

            _menu.Opening += delegate { UpdateMenu(); };

            _notify = new NotifyIcon();
            _notify.Icon = icon;
            _notify.Text = "Окна";
            _notify.ContextMenuStrip = _menu;
            _notify.Visible = true;
            _notify.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowSettings();
            };
            _notify.DoubleClick += delegate { ShowSettings(); };
            _notify.BalloonTipClicked += delegate
            {
                if (_ready != null) ApplyUpdateNow();
            };

            UpdateMenu();
            StartUpdater();

            if (_showOnStart)
            {
                _startTimer = new Timer();
                _startTimer.Interval = 140;
                _startTimer.Tick += delegate
                {
                    _startTimer.Stop();
                    _startTimer.Dispose();
                    _startTimer = null;
                    ShowSettings();
                    if (_settings.FirstRun)
                    {
                        _settings.MarkConfigured();
                        _notify.BalloonTipTitle = "Окна";
                        _notify.BalloonTipText = "Перетаскивайте окна за заголовок. Shift — обычное перетаскивание.";
                        _notify.BalloonTipIcon = ToolTipIcon.Info;
                        _notify.ShowBalloonTip(4500);
                    }
                };
                _startTimer.Start();
            }
        }

        private static ToolStripMenuItem NewItem(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = Theme.Text;
            item.Padding = new Padding(4, 3, 4, 3);
            return item;
        }

        public void ShowSettings()
        {
            if (_exiting) return;
            if (_form == null || _form.IsDisposed)
            {
                _form = new SettingsForm(_settings, _engine, _icon);
                _form.FormClosed += delegate
                {
                    if (_form != null && _form.ReallyExit)
                        _form = null;
                };
            }
            _form.ShowSettings();
        }

        private void UpdateMenu()
        {
            _settings.StartWithWindows = AppSettings.IsAutoStartEnabled();
            _toggleItem.Text = _settings.Enabled ? "Эффект включён" : "Эффект выключен";
            _toggleItem.ForeColor = _settings.Enabled ? Theme.Accent : Theme.Muted;
            _startupItem.Text = _settings.StartWithWindows ? "Автозапуск включён" : "Автозапуск выключен";
            _notify.Text = _settings.Enabled ? "Окна — включено" : "Окна — пауза";

            if (_autoUpdateItem != null)
                _autoUpdateItem.Text = _settings.AutoUpdate ? "Автообновление включено" : "Автообновление выключено";

            if (_updateItem != null)
            {
                _updateItem.Visible = _ready != null;
                if (_ready != null) _updateItem.Text = "Обновить до v" + Updater.Format(_ready.Version);
            }
        }

        private void StartUpdater()
        {
            if (Updater.HasPending)
            {
                Version pending = Updater.PendingVersion();
                if (pending != null)
                {
                    _ready = new UpdateInfo();
                    _ready.Version = pending;
                    UpdateMenu();
                    if (_settings.AutoUpdate) ScheduleApply();
                }
            }

            _firstCheck = new Timer();
            _firstCheck.Interval = 20000;
            _firstCheck.Tick += delegate
            {
                _firstCheck.Stop();
                _firstCheck.Dispose();
                _firstCheck = null;
                CheckUpdates(false);
            };
            _firstCheck.Start();

            _updateTimer = new Timer();
            _updateTimer.Interval = 6 * 60 * 60 * 1000;
            _updateTimer.Tick += delegate { CheckUpdates(false); };
            _updateTimer.Start();
        }

        private void CheckUpdates(bool manual)
        {
            if (_checking) return;
            _checking = true;
            if (_checkItem != null) _checkItem.Enabled = false;

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo found = null;
                string error = null;
                try
                {
                    found = Updater.Check();
                    if (found != null) Updater.Download(found);
                }
                catch (Exception ex)
                {
                    found = null;
                    error = ex.Message;
                    Log.Debug("Update check failed: " + ex);
                }

                Post(delegate { OnUpdateChecked(manual, found, error); });
            });
        }

        private void OnUpdateChecked(bool manual, UpdateInfo found, string error)
        {
            if (_exiting) return;
            _checking = false;
            if (_checkItem != null) _checkItem.Enabled = true;

            if (error != null)
            {
                if (manual) Balloon("Не удалось проверить обновления", error);
                return;
            }

            if (found == null)
            {
                if (manual) Balloon("Обновлений нет", "У вас последняя версия — " + Updater.CurrentLabel + ".");
                return;
            }

            _ready = found;
            UpdateMenu();

            if (_settings.AutoUpdate)
            {
                ScheduleApply();
            }
            else
            {
                Balloon("Доступно обновление v" + Updater.Format(found.Version),
                        "Нажмите, чтобы установить, или выберите «Обновить» в меню.");
            }
        }

        private void ScheduleApply()
        {
            if (_applyTimer == null)
            {
                _applyTimer = new Timer();
                _applyTimer.Interval = 2000;
                _applyTimer.Tick += delegate { TryApplyUpdate(); };
            }
            _applyAttempts = 0;
            _applyTimer.Start();
        }

        private void ApplyUpdateNow()
        {
            _applyAttempts = 1000;
            TryApplyUpdate();
        }

        private void TryApplyUpdate()
        {
            if (_exiting) return;
            _applyAttempts++;

            bool busy = _engine.Status != "Готово" || (_form != null && !_form.IsDisposed && _form.Visible);
            if (busy && _applyAttempts < 150) return;

            if (_applyTimer != null) _applyTimer.Stop();

            if (Updater.Apply())
            {
                ExitApp();
                return;
            }

            _ready = null;
            UpdateMenu();
            Balloon("Не удалось обновить", "Подробности в журнале. Новую версию можно скачать с GitHub вручную.");
        }

        private void Balloon(string title, string text)
        {
            if (_notify == null) return;
            _notify.BalloonTipTitle = title;
            _notify.BalloonTipText = text;
            _notify.BalloonTipIcon = ToolTipIcon.Info;
            _notify.ShowBalloonTip(6000);
        }

        private void Post(MethodInvoker action)
        {
            try
            {
                if (Dispatcher != null && Dispatcher.IsHandleCreated && !Dispatcher.IsDisposed)
                    Dispatcher.BeginInvoke(action);
                else
                    action();
            }
            catch { }
        }

        public void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            _settings.Save();

            if (_firstCheck != null) { _firstCheck.Stop(); _firstCheck.Dispose(); _firstCheck = null; }
            if (_updateTimer != null) { _updateTimer.Stop(); _updateTimer.Dispose(); _updateTimer = null; }
            if (_applyTimer != null) { _applyTimer.Stop(); _applyTimer.Dispose(); _applyTimer = null; }

            if (_notify != null)
            {
                _notify.Visible = false;
                _notify.Dispose();
            }
            if (_menu != null) _menu.Dispose();

            SettingsForm form = _form;
            _form = null;
            if (form != null && !form.IsDisposed)
            {
                form.ReallyExit = true;
                form.Close();
                form.Dispose();
            }
            if (Dispatcher != null && !Dispatcher.IsDisposed)
            {
                Dispatcher.Close();
                Dispatcher.Dispose();
                Dispatcher = null;
            }
            ExitThread();
        }
    }

    internal sealed class JellyColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Color.FromArgb(42, 64, 80); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(42, 64, 80); } }
        public override Color MenuItemSelected { get { return Theme.CardAlt; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardAlt; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardAlt; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color ButtonSelectedHighlight { get { return Theme.CardAlt; } }
        public override Color ButtonSelectedHighlightBorder { get { return Theme.Border; } }
        public override Color ButtonPressedHighlight { get { return Theme.CardAlt; } }
        public override Color ButtonPressedHighlightBorder { get { return Theme.Border; } }
    }

    internal sealed class DispatcherForm : Form
    {
        public TrayApp Tray { get; set; }

        public DispatcherForm()
        {
            Text = "Okna Dispatcher";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;
            Visible = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_APP_SHOW)
            {
                if (Tray != null) Tray.ShowSettings();
                return;
            }
            base.WndProc(ref m);
        }
    }
}
