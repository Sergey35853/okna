using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _jellyItem;
        private readonly ToolStripMenuItem _rollItem;
        private ToolStripMenuItem[] _presetItems;
        private readonly ToolStripMenuItem _closeNoneItem;
        private ToolStripMenuItem _activeWindowItem;
        private readonly ToolStripMenuItem _closeShatterItem;
        private readonly ToolStripMenuItem _closeBurnItem;
        private readonly bool _showOnStart;
        private SettingsForm _form;
        private bool _exiting;
        private Timer _startTimer;

        private ToolStripMenuItem _pauseItem;
        private Timer _pauseTimer;
        private DateTime _pauseUntil;

        private ToolStripMenuItem _updateItem;
        private ToolStripMenuItem _notesItem;
        private ToolStripMenuItem _skipItem;
        private ToolStripMenuItem _checkItem;
        private ToolStripMenuItem _autoUpdateItem;
        private ToolStripMenuItem _autoStartElevatedItem;
        private ToolStripMenuItem _darkThemeItem;
        private ToolStripMenuItem _hotkeyChangeItem;
        private ToolStripMenuItem _lockPositionItem;
        private ToolStripMenuItem _lockSizeItem;
        private Timer _updateTimer;
        private Timer _firstCheck;
        private Timer _applyTimer;
        private UpdateInfo _ready;
        private bool _checking;
        private bool _downloading;
        private int _downloadProgress;
        private int _applyAttempts;
        private string _hotkeyStatus = "не проверена";

        /// <summary>Set by --ui-smoke so menu clicks never reach the real settings.</summary>
        internal bool SmokeMode;

        public DispatcherForm Dispatcher { get; set; }
        internal WindowBoundsLockManager BoundsLocks { get; set; }

        public string HotkeyStatus
        {
            get { return Loc.HotkeyStatus(_hotkeyStatus); }
        }

        public TrayApp(AppSettings settings, JellyEngine engine, Icon icon, bool showOnStart)
        {
            _settings = settings;
            _engine = engine;
            _icon = icon;
            _showOnStart = showOnStart;

            _menu = new ContextMenuStrip();
            _menu.Font = Theme.FontBody;
            _menu.BackColor = Color.FromArgb(17, 28, 36);
            _menu.ForeColor = Color.FromArgb(232, 242, 243);
            _menu.ShowImageMargin = false;
            _menu.Padding = new Padding(2);
            _menu.Renderer = new TrayMenuRenderer();

            ToolStripLabel header = new ToolStripLabel("ОКНА");
            header.Font = new Font(Theme.FontSmall, FontStyle.Bold);
            header.ForeColor = TrayAccent;
            header.Margin = new Padding(6, 3, 6, 3);
            _menu.Items.Add(header);
            _menu.Items.Add(new ToolStripSeparator());

            _statusItem = NewItem("");
            _statusItem.Enabled = false;
            _statusItem.Font = new Font(Theme.FontSmall, FontStyle.Bold);
            _menu.Items.Add(_statusItem);

            _toggleItem = NewItem("");
            _toggleItem.Click += delegate { TogglePause(false); };
            _menu.Items.Add(_toggleItem);

            _pauseItem = NewItem("Пауза на 30 минут");
            _pauseItem.Click += delegate { ToggleTimedPause(); };
            _pauseItem.DropDownItems.Add(NewPauseChoice("15 минут", 15));
            _pauseItem.DropDownItems.Add(NewPauseChoice("30 минут", 30));
            _pauseItem.DropDownItems.Add(NewPauseChoice("1 час", 60));
            _pauseItem.DropDownItems.Add(NewPauseChoice("3 часа", 180));
            ToolStripMenuItem untilTomorrow = NewItem("До завтра, 09:00");
            untilTomorrow.Click += delegate { PauseUntilTomorrow(true); };
            _pauseItem.DropDownItems.Add(untilTomorrow);
            ToolStripMenuItem untilRestart = NewItem("До перезапуска");
            untilRestart.Click += delegate { PauseFor(TimeSpan.Zero, true); };
            _pauseItem.DropDownItems.Add(untilRestart);
            _menu.Items.Add(_pauseItem);

            _menu.Items.Add(new ToolStripSeparator());

            _jellyItem = NewItem("Желеобразные окна");
            _jellyItem.Click += delegate
            {
                _settings.Bend = !_settings.Bend;
                Persist();
                UpdateMenu();
                SyncForm();
            };
            _menu.Items.Add(_jellyItem);

            _rollItem = NewItem("Скатывать в трубочку");
            _rollItem.Click += delegate
            {
                _settings.Roll = !_settings.Roll;
                Persist();
                UpdateMenu();
                SyncForm();
            };
            _menu.Items.Add(_rollItem);

            ToolStripMenuItem presetMenu = NewItem("Режим");
            _presetItems = new ToolStripMenuItem[4];
            string[] presetNames = new string[] { "Спокойно", "Баланс", "Желе", "Экономно" };
            for (int i = 0; i < presetNames.Length; i++)
            {
                int index = i;
                ToolStripMenuItem item = NewItem(presetNames[i]);
                item.Click += delegate
                {
                    _settings.ApplyPreset(index);
                    Persist();
                    UpdateMenu();
                    SyncForm();
                };
                _presetItems[i] = item;
                presetMenu.DropDownItems.Add(item);
            }
            _menu.Items.Add(presetMenu);

            ToolStripMenuItem closeMenu = NewItem("Эффект при закрытии");
            _closeNoneItem = NewItem("Без эффекта");
            _closeShatterItem = NewItem("Осколки");
            _closeBurnItem = NewItem("Сгорание");
            _closeNoneItem.Click += delegate { SetCloseEffect(0); };
            _closeShatterItem.Click += delegate { SetCloseEffect(1); };
            _closeBurnItem.Click += delegate { SetCloseEffect(2); };
            closeMenu.DropDownItems.Add(_closeNoneItem);
            closeMenu.DropDownItems.Add(_closeShatterItem);
            closeMenu.DropDownItems.Add(_closeBurnItem);
            _menu.Items.Add(closeMenu);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem settingsItem = NewItem("Настройки…");
            settingsItem.Click += delegate { ShowSettings(); };
            _menu.Items.Add(settingsItem);

            _menu.Items.Add(new ToolStripSeparator());

            _startupItem = NewItem("");
            _startupItem.Click += delegate
            {
                bool enable = !AppSettings.IsAutoStartEnabled();
                AppSettings.SetAutoStart(enable, Application.ExecutablePath);
                _settings.StartWithWindows = enable;
                Persist();
                UpdateMenu();
                SyncForm();
            };
            _menu.Items.Add(_startupItem);

            _autoStartElevatedItem = NewItem("Запуск от имени администратора");
            _autoStartElevatedItem.Click += delegate
            {
                bool enable = !AppSettings.IsAutoStartElevatedEnabled();
                AppSettings.SetAutoStartElevated(enable, Application.ExecutablePath);
                _settings.AutoStartElevated = enable;
                Persist();
                UpdateMenu();
                SyncForm();
            };
            _menu.Items.Add(_autoStartElevatedItem);

            _darkThemeItem = NewItem("Тёмная тема");
            _darkThemeItem.Click += delegate
            {
                bool enable = !_settings.DarkTheme;
                _settings.DarkTheme = enable;
                Persist();
                if (_form != null && !_form.IsDisposed)
                    _form.ApplyTheme();
                UpdateMenu();
                SyncForm();
            };
            _menu.Items.Add(_darkThemeItem);

            _hotkeyChangeItem = NewItem("Изменить горячую клавишу…");
            _hotkeyChangeItem.Click += delegate
            {
                if (_form != null && !_form.IsDisposed)
                {
                    _form.Show();
                    _form.ApplyHotkey();
                }
            };
            _menu.Items.Add(_hotkeyChangeItem);

            _menu.Items.Add(new ToolStripSeparator());

            _updateItem = NewItem("");
            _updateItem.Visible = false;
            _updateItem.ForeColor = Theme.Accent;
            _updateItem.Click += delegate { ApplyUpdateNow(); };
            _menu.Items.Add(_updateItem);

            _notesItem = NewItem("Что нового…");
            _notesItem.Visible = false;
            _notesItem.Click += delegate { ShowUpdateDialog(); };
            _menu.Items.Add(_notesItem);

            _skipItem = NewItem("Пропустить эту версию");
            _skipItem.Visible = false;
            _skipItem.Click += delegate { SkipReadyUpdate(); };
            _menu.Items.Add(_skipItem);

            _checkItem = NewItem("Проверить обновления…");
            _checkItem.Click += delegate { CheckUpdates(true); };
            _menu.Items.Add(_checkItem);

            _autoUpdateItem = NewItem("");
            _autoUpdateItem.Click += delegate
            {
                _settings.AutoUpdate = !_settings.AutoUpdate;
                Persist();
                UpdateMenu();
                if (_settings.AutoUpdate && _ready != null) ScheduleApply();
            };
            _menu.Items.Add(_autoUpdateItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem logItem = NewItem("Открыть журнал");
            logItem.Click += delegate { OpenLog(); };
            _menu.Items.Add(logItem);

            ToolStripMenuItem diagnosticsItem = NewItem("Диагностика…");
            diagnosticsItem.Click += delegate
            {
                Diagnostics.Show(_form, _settings, _engine, _hotkeyStatus);
            };
            _menu.Items.Add(diagnosticsItem);
            _activeWindowItem = NewItem("Активное окно…");
            _activeWindowItem.Click += delegate { ShowActiveWindow(); };
            _menu.Items.Add(_activeWindowItem);
            _lockPositionItem = NewItem("Зафиксировать положение активного окна");
            _lockPositionItem.Click += delegate { ToggleActiveWindowLock(true); };
            _menu.Items.Add(_lockPositionItem);
            _lockSizeItem = NewItem("Зафиксировать размер активного окна");
            _lockSizeItem.Click += delegate { ToggleActiveWindowLock(false); };
            _menu.Items.Add(_lockSizeItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = NewItem("Выход");
            exitItem.Click += delegate { ExitApp(); };
            _menu.Items.Add(exitItem);

            _menu.Opening += delegate { UpdateMenu(); };

            _notify = new NotifyIcon();
            _notify.Icon = TrayIcons.For(_icon, _settings.Enabled, _engine.PauseReason);
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
                if (_ready != null) ShowUpdateDialog();
            };

            ApplyLanguage();
            StartUpdater();

            if (StartupGuard.SafeMode)
            {
                Balloon(Loc.T("Безопасный режим"),
                    Loc.T("Прошлые запуски завершились неудачно, поэтому эффекты выключены. Включите их в трее, когда всё будет в порядке."), true);
            }

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
                        _notify.BalloonTipTitle = Loc.T("Окна");
                        _notify.BalloonTipText = Loc.T("Перетаскивайте окна за заголовок. Shift — обычное перетаскивание.");
                        _notify.BalloonTipIcon = ToolTipIcon.Info;
                        _notify.ShowBalloonTip(4500);
                    }
                };
                _startTimer.Start();
            }
        }

        private static readonly Color TrayText = Color.FromArgb(232, 242, 243);
        private static readonly Color TrayMuted = Color.FromArgb(139, 160, 171);
        private static readonly Color TrayAccent = Color.FromArgb(45, 212, 166);

        private static ToolStripMenuItem NewItem(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = TrayText;
            item.Padding = new Padding(4, 3, 4, 3);
            return item;
        }

        public void ShowSettings()
        {
            if (_exiting) return;
            if (_form == null || _form.IsDisposed)
            {
                _form = new SettingsForm(_settings, _engine, _icon, this);
                _form.FormClosed += delegate
                {
                    if (_form != null && _form.ReallyExit)
                        _form = null;
                };
            }
            _form.ShowSettings();
        }

        private void SyncForm()
        {
            if (_form != null && !_form.IsDisposed) _form.SyncFromSettings();
        }

        private void SetCloseEffect(int effect)
        {
            _settings.Shatter = effect == 1;
            _settings.Burn = effect == 2;
            Persist();
            UpdateMenu();
            SyncForm();
        }

        private void OpenLog()
        {
            try
            {
                string path = Log.LogPath;
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Balloon("Не удалось открыть журнал", ex.Message, true);
            }
        }

        internal void UpdateMenu()
        {
            _settings.StartWithWindows = AppSettings.IsAutoStartEnabled();

            string reason = _engine.PauseReason;
            string statusSource;
            if (StartupGuard.SafeMode && !_settings.Enabled)
                statusSource = "Окна — безопасный режим";
            else if (!_settings.Enabled)
                statusSource = "Окна — выключены";
            else if (reason == null)
                statusSource = "Окна — работают";
            else
                statusSource = "Окна — " + reason;

            string status = reason != null && _settings.Enabled && !StartupGuard.SafeMode
                ? Loc.T("Окна — ") + Loc.T(reason)
                : Loc.T(statusSource);
            _statusItem.AccessibleDescription = statusSource;
            _statusItem.Text = status;
            _statusItem.ForeColor = reason == null && _settings.Enabled ? Theme.Accent : Theme.Muted;

            SetText(_toggleItem, _settings.Enabled ? "Эффект включён" : "Эффект выключен");
            _toggleItem.ForeColor = _settings.Enabled ? Theme.Accent : Theme.Muted;
            _toggleItem.ShortcutKeyDisplayString = _settings.HasHotkey ? _settings.HotkeyLabel : "";

            if (_pauseItem != null)
            {
                if (_engine.ManuallyPaused)
                {
                    int minutes = TimedPauseMinutesLeft;
                    string source = minutes > 0
                        ? "Возобновить (ещё " + minutes + " мин)"
                        : "Возобновить (до перезапуска)";
                    _pauseItem.AccessibleDescription = source;
                    _pauseItem.Text = minutes > 0
                        ? Loc.T("Возобновить (ещё ") + minutes + Loc.T(" мин)")
                        : Loc.T("Возобновить (до перезапуска)");
                    _pauseItem.ForeColor = Theme.Accent;
                }
                else
                {
                    SetText(_pauseItem, "Пауза на 30 минут");
                    _pauseItem.ForeColor = Theme.Text;
                }
            }

            SetText(_startupItem, _settings.StartWithWindows ? "Автозапуск включён" : "Автозапуск выключен");

            if (_autoStartElevatedItem != null)
            {
                bool elevated = AppSettings.IsAutoStartElevatedEnabled();
                _autoStartElevatedItem.Checked = elevated;
                _autoStartElevatedItem.Text = elevated
                    ? Loc.T("Запуск от имени администратора: вкл")
                    : Loc.T("Запуск от имени администратора: выкл");
            }

            if (_darkThemeItem != null)
            {
                _darkThemeItem.Checked = _settings.DarkTheme;
                _darkThemeItem.Text = _settings.DarkTheme
                    ? Loc.T("Тёмная тема: вкл")
                    : Loc.T("Тёмная тема: выкл");
            }

            if (_jellyItem != null)
            {
                SetText(_jellyItem, _settings.Bend ? "Желеобразные окна: вкл" : "Желеобразные окна: выкл");
                _jellyItem.Checked = _settings.Bend;
            }
            if (_rollItem != null)
            {
                SetText(_rollItem, _settings.Roll ? "Скатывать в трубочку: вкл" : "Скатывать в трубочку: выкл");
                _rollItem.Checked = _settings.Roll;
            }
            if (_presetItems != null)
            {
                int currentPreset = _settings.CurrentPresetIndex;
                for (int i = 0; i < _presetItems.Length; i++)
                    _presetItems[i].Checked = i == currentPreset;
            }
            if (_closeNoneItem != null)
            {
                _closeNoneItem.Checked = !_settings.Shatter && !_settings.Burn;
                _closeShatterItem.Checked = _settings.Shatter;
                _closeBurnItem.Checked = _settings.Burn;
            }
            if (_lockPositionItem != null && _lockSizeItem != null)
            {
                bool positionLocked = false;
                bool sizeLocked = false;
                IntPtr active = BoundsLocks == null ? IntPtr.Zero : BoundsLocks.LastActiveWindow;
                bool hasActive = BoundsLocks != null &&
                    BoundsLocks.TryGetLockState(active, out positionLocked, out sizeLocked);
                _lockPositionItem.Enabled = hasActive;
                _lockSizeItem.Enabled = hasActive;
                _lockPositionItem.Checked = hasActive && positionLocked;
                _lockSizeItem.Checked = hasActive && sizeLocked;
                SetText(_lockPositionItem, "Зафиксировать положение активного окна");
                SetText(_lockSizeItem, "Зафиксировать размер активного окна");
            }

            _notify.Text = status.Length <= 63 ? status : status.Substring(0, 63);
            Icon stateIcon = TrayIcons.For(_icon, _settings.Enabled, reason);
            if (stateIcon != null && !ReferenceEquals(_notify.Icon, stateIcon))
                _notify.Icon = stateIcon;

            if (_autoUpdateItem != null)
                SetText(_autoUpdateItem, _settings.AutoUpdate ? "Автообновление включено" : "Автообновление выключено");

            if (_updateItem != null)
            {
                bool ready = _ready != null && _ready.Version != null;
                _updateItem.Visible = ready;
                _notesItem.Visible = ready;
                _skipItem.Visible = ready;
                if (ready)
                {
                    string version = Updater.Format(_ready.Version);
                    SetText(_updateItem, "Обновить до v" + version,
                        Loc.T("Обновить до v") + version);
                    SetText(_notesItem, "Что нового…");
                    SetText(_skipItem, "Пропустить эту версию");
                }
            }
        }

        /// <summary>
        /// Re-translates the whole tray menu and refreshes the strings that
        /// are built from live state. Called when the language changes.
        /// </summary>
        public void ApplyLanguage()
        {
            Loc.ApplyItems(_menu.Items);
            UpdateMenu();
        }

        private static void SetText(ToolStripItem item, string source)
        {
            SetText(item, source, Loc.T(source));
        }

        private static void SetText(ToolStripItem item, string source, string translated)
        {
            if (item == null) return;
            item.AccessibleDescription = source;
            item.Text = translated;
        }

        /// <summary>
        /// Refreshes the tray tooltip and icon straight from the engine state,
        /// so an automatic pause or an exclusion is visible even with the menu
        /// closed.
        /// </summary>
        public void RefreshStatus()
        {
            Post(delegate { UpdateMenu(); });
        }

        /// <summary>
        /// (Re)registers the global hotkey and reports a conflict instead of
        /// hiding it in the debug log.
        /// </summary>
        public bool ApplyHotkey(bool notify)
        {
            string error = null;
            bool ok = false;
            if (Dispatcher != null)
                ok = Dispatcher.ApplyHotkey(_settings, out error);
            if (Dispatcher == null)
            {
                _hotkeyStatus = "диспетчер ещё не создан";
                return false;
            }

            if (ok)
            {
                _hotkeyStatus = _settings.HasHotkey ? _settings.HotkeyLabel + " — работает" : "выключена";
                Log.Info("Hotkey: " + _hotkeyStatus);
            }
            else
            {
                _hotkeyStatus = error;
                Log.Error("Hotkey: " + error);
                if (notify || _showOnStart)
                    Balloon("Горячая клавиша занята", error +
                        Loc.T(". Выберите другую в настройках или закройте программу, которая её использует."), true);
            }
            UpdateMenu();
            return ok;
        }

        /// <summary>
        /// Turns the effects on or off for good. Bound to the tray item and to
        /// the global hotkey; a temporary pause is cleared when switching off.
        /// </summary>
        public void TogglePause()
        {
            TogglePause(true);
        }

        private void TogglePause(bool notify)
        {
            _settings.Enabled = !_settings.Enabled;
            Persist();
            if (_settings.Enabled) StartupGuard.NoteEffectsEnabled();
            Log.Debug("Effects " + (_settings.Enabled ? "on" : "off"));
            if (!_settings.Enabled) ClearTimedPause();
            UpdateMenu();
            SyncForm();
            if (notify)
                Balloon(_settings.Enabled ? "Окна включены" : "Окна на паузе",
                        _settings.Enabled
                            ? "Эффекты снова работают."
                            : Loc.T("Нажмите ") + _settings.HotkeyLabel +
                              Loc.T(" или выберите «Эффект выключен» в трее, чтобы вернуть."), true);
        }

        /// <summary>
        /// A pause that ends by itself: the master switch stays on, but the
        /// effects sleep for half an hour. Pressing it again resumes early.
        /// </summary>
        internal void ToggleTimedPause()
        {
            ToggleTimedPause(true);
        }

        internal void ToggleTimedPause(bool notify)
        {
            if (_engine.ManuallyPaused)
            {
                ClearTimedPause();
                UpdateMenu();
                SyncForm();
                if (notify) Balloon("Окна включены", "Пауза снята.", true);
                return;
            }
            PauseFor(TimeSpan.FromMinutes(30), notify);
        }

        internal void ToggleBendHotkey()
        {
            _settings.Bend = !_settings.Bend;
            Persist();
            UpdateMenu();
            SyncForm();
            Log.Debug("Bend hotkey -> " + _settings.Bend);
        }

        internal void ToggleRollHotkey()
        {
            _settings.Roll = !_settings.Roll;
            Persist();
            UpdateMenu();
            SyncForm();
            Log.Debug("Roll hotkey -> " + _settings.Roll);
        }

        private ToolStripMenuItem NewPauseChoice(string text, int minutes)
        {
            ToolStripMenuItem item = NewItem(text);
            item.Click += delegate { PauseFor(TimeSpan.FromMinutes(minutes), true); };
            return item;
        }

        private void PauseUntilTomorrow(bool notify)
        {
            DateTime now = DateTime.Now;
            PauseFor(NextPauseResumeTime(now).ToUniversalTime() - DateTime.UtcNow, notify);
        }

        internal static DateTime NextPauseResumeTime(DateTime now)
        {
            return now.Date.AddDays(1).AddHours(9);
        }

        private void PauseFor(TimeSpan duration, bool notify)
        {
            ClearTimedPause();
            _engine.ManuallyPaused = true;
            if (duration > TimeSpan.Zero)
            {
                _pauseUntil = DateTime.UtcNow.Add(duration);
                _pauseTimer = new Timer();
                _pauseTimer.Interval = Math.Max(1, (int)Math.Min(int.MaxValue, duration.TotalMilliseconds));
                _pauseTimer.Tick += delegate
                {
                    TimeSpan remaining = _pauseUntil - DateTime.UtcNow;
                    if (remaining > TimeSpan.Zero)
                    {
                        _pauseTimer.Interval = Math.Max(1, (int)Math.Min(int.MaxValue, remaining.TotalMilliseconds));
                        return;
                    }
                    ClearTimedPause();
                    UpdateMenu();
                    SyncForm();
                    if (notify) Balloon("Окна включены", "Время паузы закончилось.", true);
                };
                _pauseTimer.Start();
            }
            else
            {
                _pauseUntil = DateTime.MaxValue;
            }
            Log.Debug(duration > TimeSpan.Zero
                ? "Manual pause until " + _pauseUntil.ToString("o")
                : "Manual pause until restart");
            UpdateMenu();
            SyncForm();
            if (notify)
                Balloon("Окна на паузе", duration > TimeSpan.Zero
                    ? "Эффекты вернутся по окончании паузы."
                    : "Эффекты вернутся после перезапуска.", true);
        }

        private void ClearTimedPause()
        {
            if (_pauseTimer != null)
            {
                _pauseTimer.Stop();
                _pauseTimer.Dispose();
                _pauseTimer = null;
                Log.Debug("Manual pause cleared");
            }
            _pauseUntil = DateTime.MinValue;
            _engine.ManuallyPaused = false;
        }

        private void StartUpdater()
        {
            if (Updater.HasPending)
            {
                Version pending = Updater.PendingVersion();
                if (pending != null)
                {
                    if (string.Equals(_settings.SkippedVersion, Updater.Format(pending),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Updater.Discard();
                    }
                    else
                    {
                        _ready = Updater.LoadPendingInfo();
                        if (_ready == null || _ready.Version == null)
                        {
                            _ready = new UpdateInfo();
                            _ready.Version = pending;
                        }
                        UpdateMenu();
                        if (_settings.AutoUpdate) ScheduleApply();
                    }
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

        internal void CheckUpdates(bool manual)
        {
            if (_checking || _downloading) return;
            _checking = true;
            _downloading = false;
            _downloadProgress = 0;
            if (_checkItem != null) _checkItem.Enabled = false;
            SyncForm();
            UpdateMenu();

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo found = null;
                string error = null;
                bool downloaded = false;
                try
                {
                    found = Updater.Check();
                    if (found != null)
                    {
                        Post(delegate
                        {
                            if (_exiting) return;
                            _checking = false;
                            _downloading = true;
                            _downloadProgress = 0;
                            if (_checkItem != null) _checkItem.Enabled = false;
                            SyncForm();
                            UpdateMenu();
                        });
                        Updater.Download(found, delegate(int p)
                        {
                            Post(delegate
                            {
                                if (_exiting) return;
                                _downloadProgress = p;
                                SyncForm();
                            });
                        });
                        downloaded = true;
                    }
                }
                catch (Exception ex)
                {
                    if (!downloaded) found = null;
                    error = ex.Message;
                    Log.Debug("Update check/download failed: " + ex);
                }

                Post(delegate { OnUpdateChecked(manual, found, error, downloaded); });
            });
        }

        private void OnUpdateChecked(bool manual, UpdateInfo found, string error, bool downloaded)
        {
            if (_exiting) return;
            _checking = false;
            _downloading = false;
            _downloadProgress = 0;
            if (_checkItem != null) _checkItem.Enabled = true;
            SyncForm();

            if (error != null)
            {
                // Keep a previously ready update if we only failed a re-check; discard partial new state.
                if (found != null && !downloaded) Updater.Discard();
                UpdateMenu();
                if (manual) Balloon(Loc.T("Не удалось обновить/проверить"), error, true);
                return;
            }

            if (found == null)
            {
                Log.Debug("Update check: no newer release");
                UpdateMenu();
                if (manual) Balloon(Loc.T("Обновлений нет"),
                    Loc.T("У вас последняя версия — ") + Updater.CurrentLabel + ".", true);
                return;
            }

            bool skipped = IsSkipped(found);
            if (skipped && !manual)
            {
                Log.Debug("Update " + Updater.Format(found.Version) + " skipped by the user");
                Updater.Discard();
                return;
            }

            _ready = found;
            Log.Info("Update " + Updater.Format(found.Version) + " downloaded and verified");
            UpdateMenu();

            if (_settings.AutoUpdate && !skipped)
            {
                ScheduleApply();
                if (!manual)
                    Balloon(Loc.T("Доступно обновление v") + Updater.Format(found.Version),
                            Loc.T("Обновление загружено и будет установлено автоматически."));
                else
                    ShowUpdateDialog();
            }
            else
            {
                if (!skipped)
                    Balloon(Loc.T("Доступно обновление v") + Updater.Format(found.Version),
                            Loc.T("Обновление загружено. Нажмите «Установить», когда удобно."));
                if (manual) ShowUpdateDialog();
            }
        }

        private bool IsSkipped(UpdateInfo info)
        {
            return info != null && info.Version != null &&
                   string.Equals(_settings.SkippedVersion, Updater.Format(info.Version),
                       StringComparison.OrdinalIgnoreCase);
        }

        internal void ShowUpdateDialog()
        {
            if (_ready == null) return;
            UpdateInfo info = _ready;
            using (UpdateDialog dialog = new UpdateDialog(info))
            {
                IWin32Window owner = _form != null && !_form.IsDisposed && _form.Visible ? _form : null;
                dialog.ShowDialog(owner);
                if (dialog.Install)
                {
                    ApplyUpdateNow();
                }
                else if (dialog.Skip)
                {
                    SkipReadyUpdate();
                }
                else if (dialog.OpenPage && !string.IsNullOrEmpty(info.PageUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Open release page failed: " + ex.Message);
                    }
                }
            }
        }

        private void SkipReadyUpdate()
        {
            if (_ready == null || _ready.Version == null) return;
            _settings.SkippedVersion = Updater.Format(_ready.Version);
            Persist();
            Log.Info("Update " + _settings.SkippedVersion + " skipped");
            Updater.Discard();
            _ready = null;
            UpdateMenu();
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

        internal void ApplyUpdateNow()
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

            // Keep pending/ready state when possible so the user can retry from within the app.
            if (!Updater.HasPending) _ready = null;
            else if (_ready == null)
            {
                Version pv = Updater.PendingVersion();
                if (pv != null) { _ready = new UpdateInfo(); _ready.Version = pv; }
            }
            UpdateMenu();
            SyncForm();
            Balloon(Loc.T("Не удалось установить обновление"),
                Loc.T("Файлы обновления сохранены. Можно попробовать ещё раз из «Система → Обновления»."));
        }

        /// <summary>Writes the settings unless the menu is only being smoke-tested.</summary>
        private void Persist()
        {
            if (SmokeMode) return;
            _settings.Save();
        }

        /// <summary>
        /// Clicks the safe menu items and checks that they leave the settings
        /// in a sane state. Used by --ui-smoke; nothing is written to disk.
        /// </summary>
        internal UpdateInfo ReadyUpdate
        {
            get { return _ready; }
        }

        internal bool IsCheckingUpdates
        {
            get { return _checking; }
        }

        internal bool IsDownloadingUpdate
        {
            get { return _downloading; }
        }

        internal int DownloadProgress
        {
            get { return _downloadProgress; }
        }

        internal bool TimedPauseActive
        {
            get { return _engine.ManuallyPaused; }
        }

        internal int TimedPauseMinutesLeft
        {
            get
            {
                if (_pauseTimer == null || !_engine.ManuallyPaused) return 0;
                int m = (int)Math.Ceiling((_pauseUntil - DateTime.UtcNow).TotalMinutes);
                return m < 1 ? 1 : m;
            }
        }

        internal void ShowActiveWindow()
        {
            Diagnostics.ShowActiveWindow(_form,
                Diagnostics.BuildActiveWindowReport(_settings, _engine, _hotkeyStatus));
        }

        private void ToggleActiveWindowLock(bool position)
        {
            bool toggled = BoundsLocks != null &&
                (position
                    ? BoundsLocks.ToggleLastActivePosition()
                    : BoundsLocks.ToggleLastActiveSize());
            if (!toggled)
                Balloon("Окно не найдено", "Выберите окно другой программы.", true);
            UpdateMenu();
        }

        internal void ShowProfileForActiveWindow()
        {
            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || Native.IsOwnProcessWindow(hwnd))
            {
                Balloon("Нет активного окна", "Выберите окно другой программы.", true);
                return;
            }

            string exe = Native.GetWindowProcessName(hwnd);
            if (string.IsNullOrEmpty(exe))
            {
                Balloon("Не удалось определить программу", Native.GetWindowTitle(hwnd), true);
                return;
            }

            string program;
            int effect;
            bool bend, shake, roll;
            if (!CloseOverrideDialog.ShowFor(_form, _settings, exe,
                    out program, out effect, out bend, out shake, out roll))
                return;
            _settings.SetAppProfile(program, effect, bend, shake, roll);
            Persist();
            UpdateMenu();
            SyncForm();
        }

        internal void ClearSkippedUpdateUi()
        {
            _settings.SkippedVersion = "";
            Persist();
            UpdateMenu();
        }

        internal void ApplyAutoUpdateSetting()
        {
            UpdateMenu();
            if (_settings.AutoUpdate && _ready != null) ScheduleApply();
        }

        internal string RunSmokeTest()
        {
            SmokeMode = true;
            System.Collections.Generic.List<string> lines =
                new System.Collections.Generic.List<string>();
            int failed = 0;
            Action<string, bool> check = delegate(string name, bool ok)
            {
                if (ok) lines.Add("[ok] " + name);
                else { failed++; lines.Add("[FAIL] " + name); }
            };

            try
            {
                check("the tray menu has its main items", _menu.Items.Count >= 15);
                check("the preset submenu has four modes",
                    _presetItems != null && _presetItems.Length == 4);

                for (int i = 0; i < _presetItems.Length; i++)
                {
                    _presetItems[i].PerformClick();
                    check("tray preset " + i + " applies",
                        _settings.CurrentPresetIndex == i);
                }

                bool bend = _settings.Bend;
                _jellyItem.PerformClick();
                check("the tray bend item flips the switch", _settings.Bend == !bend);
                _jellyItem.PerformClick();

                bool roll = _settings.Roll;
                _rollItem.PerformClick();
                check("the tray roll item flips the switch", _settings.Roll == !roll);
                _rollItem.PerformClick();

                _closeBurnItem.PerformClick();
                check("the tray burn item is exclusive", _settings.Burn && !_settings.Shatter);
                _closeShatterItem.PerformClick();
                check("the tray shard item is exclusive", _settings.Shatter && !_settings.Burn);
                _closeNoneItem.PerformClick();
                check("the tray none item clears both", !_settings.Shatter && !_settings.Burn);

                bool enabled = _settings.Enabled;
                _toggleItem.PerformClick();
                check("the tray master switch turns off", _settings.Enabled == !enabled);
                _toggleItem.PerformClick();
                check("the tray master switch turns back on", _settings.Enabled == enabled);

                bool auto = _settings.AutoUpdate;
                _autoUpdateItem.PerformClick();
                _autoUpdateItem.PerformClick();
                check("the update switch returns to its value", _settings.AutoUpdate == auto);

                UpdateMenu();
                check("the status line is filled", _statusItem.Text.Length > 0);
                check("the pause item is present", _pauseItem != null);
                check("the pause menu offers all restored durations",
                    _pauseItem != null && _pauseItem.DropDownItems.Count == 6);
                check("active-window diagnostics are available from the tray",
                    _activeWindowItem != null);
                check("active-window position lock is available from the tray",
                    _lockPositionItem != null);
                check("active-window size lock is available from the tray",
                    _lockSizeItem != null);
                PauseFor(TimeSpan.FromMinutes(15), false);
                check("manual pause starts for the selected duration",
                    _engine.ManuallyPaused && TimedPauseMinutesLeft == 15);
                ClearTimedPause();
                PauseFor(TimeSpan.Zero, false);
                check("manual pause can last until restart",
                    _engine.ManuallyPaused && TimedPauseMinutesLeft == 0);
                ToggleTimedPause(false);
                check("pause hotkey toggle resumes manual pause", !_engine.ManuallyPaused);
                check("the update item is present", _updateItem != null);
                check("the what's new item is present", _notesItem != null);
                check("the skip version item is present", _skipItem != null);

                UpdateInfo sample = new UpdateInfo();
                sample.Version = new Version(9, 9, 9);
                sample.Notes = "test notes";
                sample.PageUrl = "https://example.com";
                using (UpdateDialog dialog = new UpdateDialog(sample))
                {
                    check("the update dialog builds", dialog.Controls.Count >= 6);
                }

            }
            catch (Exception ex)
            {
                failed++;
                lines.Add("[FAIL] the tray smoke test crashed: " + ex.Message);
            }
            finally
            {
                SmokeMode = false;
            }

            lines.Add("");
            lines.Add(failed == 0
                ? "All " + lines.Count + " tray checks passed."
                : failed + " of " + lines.Count + " tray checks failed.");
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        internal void PerformBoundsLockItemForSmoke(bool position)
        {
            ToolStripMenuItem item = position ? _lockPositionItem : _lockSizeItem;
            if (item == null) throw new InvalidOperationException("Bounds-lock tray item is missing.");
            item.PerformClick();
        }

        internal bool IsBoundsLockItemCheckedForSmoke(bool position)
        {
            ToolStripMenuItem item = position ? _lockPositionItem : _lockSizeItem;
            return item != null && item.Checked;
        }

        internal void DisposeForSmoke()
        {
            try { if (_startTimer != null) { _startTimer.Stop(); _startTimer.Dispose(); } } catch { }
            try { if (_pauseTimer != null) { _pauseTimer.Stop(); _pauseTimer.Dispose(); } } catch { }
            try { if (_updateTimer != null) { _updateTimer.Stop(); _updateTimer.Dispose(); } } catch { }
            try { if (_firstCheck != null) { _firstCheck.Stop(); _firstCheck.Dispose(); } } catch { }
            try { if (_applyTimer != null) { _applyTimer.Stop(); _applyTimer.Dispose(); } } catch { }
            try { _notify.Visible = false; _notify.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
        }

        internal void Balloon(string title, string text)
        {
            Balloon(title, text, false);
        }

        internal void Balloon(string title, string text, bool force)
        {
            if (_notify == null) return;
            if (!force && _settings != null && _settings.QuietNotifications) return;
            _notify.BalloonTipTitle = Loc.T(title);
            _notify.BalloonTipText = Loc.T(text);
            _notify.BalloonTipIcon = ToolTipIcon.Info;
            _notify.ShowBalloonTip(force ? 6000 : 4500);
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
            Persist();

            if (_firstCheck != null) { _firstCheck.Stop(); _firstCheck.Dispose(); _firstCheck = null; }
            if (_updateTimer != null) { _updateTimer.Stop(); _updateTimer.Dispose(); _updateTimer = null; }
            if (_applyTimer != null) { _applyTimer.Stop(); _applyTimer.Dispose(); _applyTimer = null; }
            ClearTimedPause();

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

    internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new JellyColorTable()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled)
                e.TextColor = Color.FromArgb(139, 160, 171);
            base.OnRenderItemText(e);
        }
    }

    internal sealed class JellyColorTable : ProfessionalColorTable
    {
        // Match earlier Okna tray menu appearance (solid)
        private static readonly Color Bg = Color.FromArgb(17, 28, 36);
        private static readonly Color Sel = Color.FromArgb(23, 37, 47);
        private static readonly Color Sep = Color.FromArgb(33, 51, 63);
        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Color.FromArgb(42, 64, 80); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(42, 64, 80); } }
        public override Color MenuItemSelected { get { return Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override Color SeparatorDark { get { return Sep; } }
        public override Color SeparatorLight { get { return Sep; } }
        public override Color ButtonSelectedHighlight { get { return Sel; } }
        public override Color ButtonSelectedHighlightBorder { get { return Sep; } }
        public override Color ButtonPressedHighlight { get { return Sel; } }
        public override Color ButtonPressedHighlightBorder { get { return Sep; } }
    }

    internal sealed class DispatcherForm : Form
    {
        private const int HotkeyIdToggle = 0x4F4B;
        private const int HotkeyIdBend = 0x4F4C;
        private const int HotkeyIdRoll = 0x4F4D;
        private const int HotkeyIdSettings = 0x4F4E;
        private const int HotkeyIdPause = 0x4F4F;
        private const int HotkeyIdProfileWindow = 0x4F50;
        private readonly System.Collections.Generic.List<int> _registeredIds = new System.Collections.Generic.List<int>();

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

        /// <summary>
        /// Registers hotkeys from settings. Main toggle conflict is reported via <paramref name="error"/>.
        /// Additional hotkey conflicts are collected but do not block the main toggle.
        /// </summary>
        public bool ApplyHotkey(AppSettings settings, out string error)
        {
            error = null;
            if (!IsHandleCreated)
            {
                error = "окно диспетчера ещё не создано";
                return false;
            }

            UnregisterAll();

            bool mainOk = true;
            if (settings != null && settings.HasHotkey)
                mainOk = RegisterOne(HotkeyIdToggle, settings.HotkeyModifiers, settings.HotkeyKey, settings.HotkeyLabel, out error);
            // register extras (best-effort)
            if (settings != null)
            {
                string e2;
                if (settings.HasHotkeyBend) RegisterOne(HotkeyIdBend, settings.HotkeyBendModifiers, settings.HotkeyBendKey, settings.HotkeyBendLabel, out e2);
                if (settings.HasHotkeyRoll) RegisterOne(HotkeyIdRoll, settings.HotkeyRollModifiers, settings.HotkeyRollKey, settings.HotkeyRollLabel, out e2);
                if (settings.HasHotkeySettings) RegisterOne(HotkeyIdSettings, settings.HotkeySettingsModifiers, settings.HotkeySettingsKey, settings.HotkeySettingsLabel, out e2);
                if (settings.HasPauseHotkey) RegisterOne(HotkeyIdPause, settings.PauseHotkeyModifiers, settings.PauseHotkeyKey, settings.PauseHotkeyLabel, out e2);
                if (settings.HasHotkeyProfileWindow) RegisterOne(HotkeyIdProfileWindow, settings.HotkeyProfileWindowModifiers, settings.HotkeyProfileWindowKey, settings.HotkeyProfileWindowLabel, out e2);
            }
            return mainOk;
        }

        private bool RegisterOne(int id, int modifiers, int key, string label, out string error)
        {
            error = null;
            if (key == 0 || modifiers == 0) return true;
            uint mods = (uint)modifiers | Native.MOD_NOREPEAT;
            bool ok = Native.RegisterHotKey(Handle, id, mods, (uint)key);
            if (!ok)
            {
                error = Loc.T("Клавиша ") + label + Loc.T(" уже занята другой программой");
                return false;
            }
            _registeredIds.Add(id);
            return true;
        }

        private void UnregisterAll()
        {
            for (int i = 0; i < _registeredIds.Count; i++)
            {
                try { Native.UnregisterHotKey(Handle, _registeredIds[i]); } catch { }
            }
            _registeredIds.Clear();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { UnregisterAll(); } catch { }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_APP_SHOW)
            {
                if (Tray != null) Tray.ShowSettings();
                return;
            }
            if (m.Msg == Native.WM_APP_ALREADY_RUNNING)
            {
                if (Tray != null)
                    Tray.Balloon(Loc.T("Окна уже запущены"),
                        Loc.T("Программа уже работает в трее."), true);
                return;
            }
            if (m.Msg == Native.WM_HOTKEY && Tray != null)
            {
                int id = m.WParam.ToInt32();
                if (id == HotkeyIdToggle) { Tray.TogglePause(); return; }
                if (id == HotkeyIdBend) { Tray.ToggleBendHotkey(); return; }
                if (id == HotkeyIdRoll) { Tray.ToggleRollHotkey(); return; }
                if (id == HotkeyIdSettings) { Tray.ShowSettings(); return; }
                if (id == HotkeyIdPause) { Tray.ToggleTimedPause(false); return; }
                if (id == HotkeyIdProfileWindow) { Tray.ShowProfileForActiveWindow(); return; }
            }
            base.WndProc(ref m);
        }
    }
}
