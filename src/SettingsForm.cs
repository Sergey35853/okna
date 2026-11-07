using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Okna
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly JellyEngine _engine;
        private readonly Icon _icon;
        private readonly TrayApp _tray;

        private bool _jellyDemoDone;
        private JellySwitch _effectSwitch;
        private JellySwitch _wobbleSwitch;
        private JellySwitch _bendSwitch;
        private JellySwitch _maxShakeSwitch;
        private JellyButton _closeNone;
        private JellyButton _closeShatter;
        private JellyButton _closeBurn;
        private JellyButton _closeFade;
        private JellySwitch _rollSwitch;
        private JellyButton[] _directionButtons;
        private JellySwitch _startupSwitch;
        private JellySwitch _autoStartElevatedSwitch;
        private JellySwitch _callSwitch;
        private JellySwitch _batterySwitch;
        private JellySwitch _motionSwitch;
        private JellySwitch _portableSwitch;
        private JellyButton _languageAuto;
        private JellyButton _languageRu;
        private JellyButton _languageEn;
        private Label _portableLabel;
        private Label _versionLabel;
        private JellyButton _hotkeyButton;
        private Label _hotkeyStatus;
        private ListBox _excludeList;
        private TextBox _excludeMask;
        private ListBox _overrideList;
        private JellySlider _slider;
        private Label _valueLabel;
        private Label _hintLabel;
        private StatusPill _statusPill;
        private JellyButton[] _presets;
        private JellySwitch _darkThemeSwitch;
        private bool _syncing;
        private Timer _statusTimer;
        private Timer _saveTimer;

        /// <summary>Set by --ui-smoke so clicks never reach the real settings.</summary>
        internal bool SmokeMode;

        public bool ReallyExit { get; set; }

        public SettingsForm(AppSettings settings, JellyEngine engine, Icon icon, TrayApp tray)
        {
            _settings = settings;
            _engine = engine;
            _icon = icon;
            _tray = tray;

            Text = "Окна";
            Icon = icon;
            ClientSize = new Size(950, 820);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            DoubleBuffered = true;
            // The landscape design still does not fit every screen (1366x768,
            // or 150% scaling). Clamp it to the working area in OnLoad and
            // scroll the rest; otherwise the right column or the bottom
            // buttons would be unreachable.
            AutoScroll = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw, true);

            BuildUi();
            Loc.Apply(this);
            WireEvents();

            _statusTimer = new Timer();
            _statusTimer.Interval = 350;
            _statusTimer.Tick += delegate
            {
                _statusPill.SetStatus(_engine.PauseReason ?? _engine.Status);
            };
            _statusTimer.Start();

            _saveTimer = new Timer();
            _saveTimer.Interval = 350;
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                Persist();
            };

            SyncFromSettings();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.EnableDarkTitleBar(Handle);
            // Let the engine treat this one window of its own process as a
            // normal target, so the settings window itself can be dragged and
            // wobbled while the slider is being tried out.
            if (_engine != null) _engine.AllowOwnWindow(Handle);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_engine != null) _engine.DisallowOwnWindow(Handle);
            base.OnHandleDestroyed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            Rectangle area = Screen.FromControl(this).WorkingArea;
            int maxWidth = Math.Max(720, area.Width - 48);
            int maxHeight = Math.Max(360, area.Height - 48);
            int width = Math.Min(ClientSize.Width, maxWidth);
            int height = Math.Min(ClientSize.Height, maxHeight);
            if (width != ClientSize.Width || height != ClientSize.Height)
                ClientSize = new Size(width, height);

            // Scroll only as far as the content actually goes. The width stays
            // just below the client width so a vertical scrollbar on a short
            // screen does not trigger a second, horizontal one; when the screen
            // is narrower than the design, the horizontal bar appears instead
            // of cutting the right column off.
            int contentRight = 0;
            int contentBottom = 0;
            foreach (Control c in Controls)
            {
                if (c.Right > contentRight) contentRight = c.Right;
                if (c.Bottom > contentBottom) contentBottom = c.Bottom;
            }
            AutoScrollMinSize = new Size(contentRight, contentBottom + 8);
            AutoScrollPosition = Point.Empty;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CLOSE = 0x0010;
            if (m.Msg == WM_CLOSE && !ReallyExit && !Environment.HasShutdownStarted)
            {
                Persist();
                BeginInvoke(new MethodInvoker(delegate { Hide(); }));
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle rect = ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using (LinearGradientBrush bg = new LinearGradientBrush(rect, Theme.Bg, Theme.BgLight, LinearGradientMode.Vertical))
                g.FillRectangle(bg, rect);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 6; i >= 1; i--)
            {
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(4 + i, Theme.Accent)))
                    g.FillEllipse(glow, Width - 260 - i * 14, -210 - i * 10, 420 + i * 28, 420 + i * 28);
            }
        }

        private void BuildUi()
        {
            const int leftX = 20;
            const int colW = 440;
            const int rightX = 470;
            const int topY = 74;
            const int gap = 8;

            PictureBox logo = new PictureBox();
            logo.Location = new Point(leftX, 14);
            logo.Size = new Size(38, 38);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Color.Transparent;
            try
            {
                using (Icon sized = new Icon(_icon, 38, 38))
                    logo.Image = sized.ToBitmap();
            }
            catch { logo.Image = _icon.ToBitmap(); }
            Controls.Add(logo);

            Label title = MakeLabel("Окна", 66, 10, Theme.FontTitle, Theme.Text);
            Controls.Add(title);

            Label subtitle = MakeLabel("Инерционные окна для Windows", 68, 42, Theme.FontSmall, Theme.Muted);
            Controls.Add(subtitle);

            _statusPill = new StatusPill();
            _statusPill.Location = new Point(ClientSize.Width - 24 - _statusPill.Width, 20);
            _statusPill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(_statusPill);

            CardPanel moveCard = new CardPanel();
            moveCard.Location = new Point(leftX, topY);
            moveCard.Size = new Size(colW, 160);
            Controls.Add(moveCard);

            Label effectTitle = MakeLabel("Желеобразные окна", 20, 10, Theme.FontCardTitle, Theme.Text);
            moveCard.Controls.Add(effectTitle);
            Label effectSub = MakeLabel("Перетаскивание за заголовок", 20, 34, Theme.FontSmall, Theme.Muted);
            moveCard.Controls.Add(effectSub);

            _effectSwitch = new JellySwitch();
            _effectSwitch.Location = new Point(372, 24);
            moveCard.Controls.Add(_effectSwitch);

            Panel divider = new Panel();
            divider.Location = new Point(20, 68);
            divider.Size = new Size(400, 1);
            divider.BackColor = Theme.BorderSoft;
            moveCard.Controls.Add(divider);

            Label jellyLabel = MakeLabel("Желейность", 20, 80, Theme.FontLabel, Theme.Text);
            moveCard.Controls.Add(jellyLabel);

            _valueLabel = MakeLabel("", 292, 80, Theme.FontSmall, Theme.Accent);
            _valueLabel.AutoSize = false;
            _valueLabel.Size = new Size(128, 16);
            _valueLabel.TextAlign = ContentAlignment.MiddleRight;
            moveCard.Controls.Add(_valueLabel);

            _slider = new JellySlider();
            _slider.Minimum = 0;
            _slider.Maximum = 200;
            _slider.Location = new Point(20, 102);
            _slider.Size = new Size(400, 24);
            moveCard.Controls.Add(_slider);

            _presets = new JellyButton[6];
            string[] names = new string[] { "Спокойно", "Баланс", "Желе", "Экономно", "Яркий", "Минимальный" };
            for (int i = 0; i < 6; i++)
            {
                JellyButton b = new JellyButton();
                b.Text = names[i];
                b.Location = new Point(20 + i * 74, 130);
                b.Size = new Size(70, 24);
                b.Font = Theme.FontSmall;
                b.Tag = i;
                _presets[i] = b;
                moveCard.Controls.Add(b);
            }

            CardPanel behaviorCard = new CardPanel();
            behaviorCard.Location = new Point(leftX, topY + 160 + gap);
            behaviorCard.Size = new Size(colW, 232);
            Controls.Add(behaviorCard);

            Label wobbleTitle = MakeLabel("Покачивание при отпускании", 20, 10, Theme.FontCardTitle, Theme.Text);
            behaviorCard.Controls.Add(wobbleTitle);
            Label wobbleSub = MakeLabel("Боковое качание после движения", 20, 30, Theme.FontSmall, Theme.Muted);
            behaviorCard.Controls.Add(wobbleSub);

            _wobbleSwitch = new JellySwitch();
            _wobbleSwitch.Location = new Point(372, 14);
            behaviorCard.Controls.Add(_wobbleSwitch);

            Panel divider2 = new Panel();
            divider2.Location = new Point(20, 48);
            divider2.Size = new Size(400, 1);
            divider2.BackColor = Theme.BorderSoft;
            behaviorCard.Controls.Add(divider2);

            Label bendTitle = MakeLabel("Гнуть окна по инерции", 20, 56, Theme.FontCardTitle, Theme.Text);
            behaviorCard.Controls.Add(bendTitle);
            Label bendSub = MakeLabel("Края изгибаются при разгоне и торможении", 20, 76, Theme.FontSmall, Theme.Muted);
            behaviorCard.Controls.Add(bendSub);

            _bendSwitch = new JellySwitch();
            _bendSwitch.Location = new Point(372, 60);
            behaviorCard.Controls.Add(_bendSwitch);

            Panel divider3 = new Panel();
            divider3.Location = new Point(20, 90);
            divider3.Size = new Size(400, 1);
            divider3.BackColor = Theme.BorderSoft;
            behaviorCard.Controls.Add(divider3);

            Label maxShakeTitle = MakeLabel("Тряска при разворачивании", 20, 98, Theme.FontCardTitle, Theme.Text);
            behaviorCard.Controls.Add(maxShakeTitle);
            Label maxShakeSub = MakeLabel("Качается при развороте, восстановлении", 20, 118, Theme.FontSmall, Theme.Muted);
            behaviorCard.Controls.Add(maxShakeSub);

            _maxShakeSwitch = new JellySwitch();
            _maxShakeSwitch.Location = new Point(372, 102);
            behaviorCard.Controls.Add(_maxShakeSwitch);

            Panel divider4 = new Panel();
            divider4.Location = new Point(20, 134);
            divider4.Size = new Size(400, 1);
            divider4.BackColor = Theme.BorderSoft;
            behaviorCard.Controls.Add(divider4);

            Label startupTitle = MakeLabel("Запускать с Windows", 20, 142, Theme.FontCardTitle, Theme.Text);
            behaviorCard.Controls.Add(startupTitle);
            Label startupSub = MakeLabel("Тихо стартовать с системой", 20, 162, Theme.FontSmall, Theme.Muted);
            behaviorCard.Controls.Add(startupSub);

            _startupSwitch = new JellySwitch();
            _startupSwitch.Location = new Point(372, 146);
            behaviorCard.Controls.Add(_startupSwitch);

            Label elevatedTitle = MakeLabel("Запуск от имени администратора", 20, 176, Theme.FontCardTitle, Theme.Text);
            behaviorCard.Controls.Add(elevatedTitle);
            Label elevatedSub = MakeLabel("С правами администратора при входе", 20, 196, Theme.FontSmall, Theme.Muted);
            behaviorCard.Controls.Add(elevatedSub);

            _autoStartElevatedSwitch = new JellySwitch();
            _autoStartElevatedSwitch.Location = new Point(372, 180);
            behaviorCard.Controls.Add(_autoStartElevatedSwitch);

            CardPanel closeCard = new CardPanel();
            closeCard.Location = new Point(leftX, topY + 160 + gap + 232 + gap);
            closeCard.Size = new Size(colW, 172);
            Controls.Add(closeCard);

            Label closeTitle = MakeLabel("Эффект при закрытии", 20, 10, Theme.FontCardTitle, Theme.Text);
            closeCard.Controls.Add(closeTitle);
            Label closeSub = MakeLabel("Если программа остаётся в трее — скатывается туда", 20, 30, Theme.FontSmall, Theme.Muted);
            closeCard.Controls.Add(closeSub);

            _closeNone = new JellyButton();
            _closeNone.Text = "Без эффекта";
            _closeNone.Location = new Point(20, 52);
            _closeNone.Size = new Size(132, 26);
            _closeNone.Font = Theme.FontSmall;
            closeCard.Controls.Add(_closeNone);

            _closeShatter = new JellyButton();
            _closeShatter.Text = "Осколки";
            _closeShatter.Location = new Point(160, 52);
            _closeShatter.Size = new Size(132, 26);
            _closeShatter.Font = Theme.FontSmall;
            closeCard.Controls.Add(_closeShatter);

            _closeBurn = new JellyButton();
            _closeBurn.Text = "Сгорание";
            _closeBurn.Location = new Point(300, 52);
            _closeBurn.Size = new Size(132, 26);
            _closeBurn.Font = Theme.FontSmall;
            closeCard.Controls.Add(_closeBurn);

            _closeFade = new JellyButton();
            _closeFade.Text = "Затухание";
            _closeFade.Location = new Point(20, 84);
            _closeFade.Size = new Size(132, 26);
            _closeFade.Font = Theme.FontSmall;
            closeCard.Controls.Add(_closeFade);

            Panel divider5 = new Panel();
            divider5.Location = new Point(20, 120);
            divider5.Size = new Size(400, 1);
            divider5.BackColor = Theme.BorderSoft;
            closeCard.Controls.Add(divider5);

            Label rollTitle = MakeLabel("Сворачивать в трубочку", 20, 128, Theme.FontCardTitle, Theme.Text);
            closeCard.Controls.Add(rollTitle);
            Label rollSub = MakeLabel("Окно скатывается в панель задач", 20, 148, Theme.FontSmall, Theme.Muted);
            closeCard.Controls.Add(rollSub);

            _rollSwitch = new JellySwitch();
            _rollSwitch.Location = new Point(372, 132);
            closeCard.Controls.Add(_rollSwitch);

            Label directionTitle = MakeLabel("Куда скатывать", 20, 166, Theme.FontLabel, Theme.Text);
            closeCard.Controls.Add(directionTitle);
            Label directionSub = MakeLabel("Авто — к панели задач", 20, 186, Theme.FontSmall, Theme.Muted);
            closeCard.Controls.Add(directionSub);

            _directionButtons = new JellyButton[5];
            string[] directionNames = new string[] { "Авто", "Вниз", "Вверх", "Влево", "Вправо" };
            for (int i = 0; i < 5; i++)
            {
                JellyButton b = new JellyButton();
                b.Text = directionNames[i];
                b.Location = new Point(20 + i * 76, 148);
                b.Size = new Size(72, 24);
                b.Font = Theme.FontSmall;
                b.Tag = (RollDirection)i;
                _directionButtons[i] = b;
                closeCard.Controls.Add(b);
            }

            CardPanel hotkeyCard = new CardPanel();
            hotkeyCard.Location = new Point(leftX, topY + 160 + gap + 232 + gap + 172 + gap);
            hotkeyCard.Size = new Size(colW, 96);
            Controls.Add(hotkeyCard);

            Label hotkeyTitle = MakeLabel("Горячая клавиша", 20, 10, Theme.FontCardTitle, Theme.Text);
            hotkeyCard.Controls.Add(hotkeyTitle);
            Label hotkeySub = MakeLabel("Нажмите поле и введите сочетание; Backspace — выключить", 20, 30, Theme.FontSmall, Theme.Muted);
            hotkeyCard.Controls.Add(hotkeySub);

            _hotkeyButton = new JellyButton();
            _hotkeyButton.Text = "Ctrl+Alt+J";
            _hotkeyButton.Location = new Point(20, 52);
            _hotkeyButton.Size = new Size(180, 30);
            _hotkeyButton.Font = Theme.FontBody;
            _hotkeyButton.Click += delegate { _hotkeyButton.Focus(); };
            hotkeyCard.Controls.Add(_hotkeyButton);

            _hotkeyStatus = MakeLabel("", 210, 58, Theme.FontSmall, Theme.Muted);
            _hotkeyStatus.Size = new Size(210, 20);
            hotkeyCard.Controls.Add(_hotkeyStatus);

            CardPanel pauseCard = new CardPanel();
            pauseCard.Location = new Point(rightX, topY);
            pauseCard.Size = new Size(colW, 168);
            Controls.Add(pauseCard);

            Label pauseTitle = MakeLabel("Пауза сама по себе", 20, 10, Theme.FontCardTitle, Theme.Text);
            pauseCard.Controls.Add(pauseTitle);
            Label pauseSub = MakeLabel("Эффекты уступают место, когда вы заняты", 20, 30, Theme.FontSmall, Theme.Muted);
            pauseCard.Controls.Add(pauseSub);

            Label callTitle = MakeLabel("Звонки и показ экрана", 20, 54, Theme.FontCardTitle, Theme.Text);
            pauseCard.Controls.Add(callTitle);
            Label callSub = MakeLabel("Teams, Zoom, Discord, Slack и др.", 20, 74, Theme.FontSmall, Theme.Muted);
            pauseCard.Controls.Add(callSub);
            _callSwitch = new JellySwitch();
            _callSwitch.Location = new Point(372, 58);
            pauseCard.Controls.Add(_callSwitch);

            Label batteryTitle = MakeLabel("Питание от батареи", 20, 88, Theme.FontCardTitle, Theme.Text);
            pauseCard.Controls.Add(batteryTitle);
            Label batterySub = MakeLabel("Не тратить заряд на эффекты", 20, 108, Theme.FontSmall, Theme.Muted);
            pauseCard.Controls.Add(batterySub);
            _batterySwitch = new JellySwitch();
            _batterySwitch.Location = new Point(372, 92);
            pauseCard.Controls.Add(_batterySwitch);

            Label motionTitle = MakeLabel("Уважать уменьшение движения", 20, 122, Theme.FontCardTitle, Theme.Text);
            pauseCard.Controls.Add(motionTitle);
            Label motionSub = MakeLabel("Если анимации выключены в Windows", 20, 142, Theme.FontSmall, Theme.Muted);
            pauseCard.Controls.Add(motionSub);
            _motionSwitch = new JellySwitch();
            _motionSwitch.Location = new Point(372, 126);
            pauseCard.Controls.Add(_motionSwitch);

            CardPanel excludeCard = new CardPanel();
            excludeCard.Location = new Point(rightX, topY + 168 + gap);
            excludeCard.Size = new Size(colW, 216);
            Controls.Add(excludeCard);

            Label excludeTitle = MakeLabel("Исключения", 20, 10, Theme.FontCardTitle, Theme.Text);
            excludeCard.Controls.Add(excludeTitle);
            Label excludeSub = MakeLabel("Окна остаются обычными: программы, маски, классы", 20, 30, Theme.FontSmall, Theme.Muted);
            excludeCard.Controls.Add(excludeSub);

            _excludeList = new ListBox();
            _excludeList.Location = new Point(20, 54);
            _excludeList.Size = new Size(260, 88);
            _excludeList.DrawMode = DrawMode.OwnerDrawFixed;
            _excludeList.ItemHeight = 20;
            _excludeList.BorderStyle = BorderStyle.None;
            _excludeList.BackColor = Theme.CardAlt;
            _excludeList.ForeColor = Theme.Text;
            _excludeList.Font = Theme.FontSmall;
            _excludeList.IntegralHeight = false;
            _excludeList.DrawItem += DrawExcludeItem;
            excludeCard.Controls.Add(_excludeList);

            JellyButton excludeAdd = new JellyButton();
            excludeAdd.Text = "Добавить файл…";
            excludeAdd.Location = new Point(292, 54);
            excludeAdd.Size = new Size(128, 28);
            excludeAdd.Font = Theme.FontSmall;
            excludeAdd.Click += delegate { AddExclusions(); };
            excludeCard.Controls.Add(excludeAdd);

            JellyButton excludeRunning = new JellyButton();
            excludeRunning.Text = "Из запущенных…";
            excludeRunning.Location = new Point(292, 86);
            excludeRunning.Size = new Size(128, 28);
            excludeRunning.Font = Theme.FontSmall;
            excludeRunning.Click += delegate { PickRunning(); };
            excludeCard.Controls.Add(excludeRunning);

            JellyButton excludeRemove = new JellyButton();
            excludeRemove.Text = "Убрать";
            excludeRemove.Location = new Point(292, 118);
            excludeRemove.Size = new Size(128, 28);
            excludeRemove.Font = Theme.FontSmall;
            excludeRemove.Click += delegate { RemoveSelectedExclusion(); };
            excludeCard.Controls.Add(excludeRemove);

            Label maskLabel = MakeLabel("Маска: steam* или class:Progman", 20, 150, Theme.FontSmall, Theme.Muted);
            excludeCard.Controls.Add(maskLabel);

            _excludeMask = new TextBox();
            _excludeMask.Location = new Point(20, 172);
            _excludeMask.Size = new Size(260, 26);
            _excludeMask.BorderStyle = BorderStyle.FixedSingle;
            _excludeMask.BackColor = Theme.CardAlt;
            _excludeMask.ForeColor = Theme.Text;
            _excludeMask.Font = Theme.FontSmall;
            excludeCard.Controls.Add(_excludeMask);

            JellyButton addMask = new JellyButton();
            addMask.Text = "Добавить маску";
            addMask.Location = new Point(292, 172);
            addMask.Size = new Size(128, 26);
            addMask.Font = Theme.FontSmall;
            addMask.Click += delegate { AddMask(); };
            excludeCard.Controls.Add(addMask);

            CardPanel overrideCard = new CardPanel();
            overrideCard.Location = new Point(rightX, topY + 168 + gap + 216 + gap);
            overrideCard.Size = new Size(colW, 120);
            Controls.Add(overrideCard);

            Label overrideTitle = MakeLabel("Свои правила для программы", 20, 10, Theme.FontCardTitle, Theme.Text);
            overrideCard.Controls.Add(overrideTitle);
            Label overrideSub = MakeLabel("Эффект закрытия, изгиб, тряска, скатывание", 20, 30, Theme.FontSmall, Theme.Muted);
            overrideCard.Controls.Add(overrideSub);

            _overrideList = new ListBox();
            _overrideList.Location = new Point(20, 52);
            _overrideList.Size = new Size(260, 48);
            _overrideList.DrawMode = DrawMode.OwnerDrawFixed;
            _overrideList.ItemHeight = 20;
            _overrideList.BorderStyle = BorderStyle.None;
            _overrideList.BackColor = Theme.CardAlt;
            _overrideList.ForeColor = Theme.Text;
            _overrideList.Font = Theme.FontSmall;
            _overrideList.IntegralHeight = false;
            _overrideList.DrawItem += DrawOverrideItem;
            overrideCard.Controls.Add(_overrideList);

            JellyButton overrideAdd = new JellyButton();
            overrideAdd.Text = "Для программы…";
            overrideAdd.Location = new Point(292, 52);
            overrideAdd.Size = new Size(128, 28);
            overrideAdd.Font = Theme.FontSmall;
            overrideAdd.Click += delegate { AddOverride(); };
            overrideCard.Controls.Add(overrideAdd);

            JellyButton overrideRemove = new JellyButton();
            overrideRemove.Text = "Убрать";
            overrideRemove.Location = new Point(292, 84);
            overrideRemove.Size = new Size(128, 28);
            overrideRemove.Font = Theme.FontSmall;
            overrideRemove.Click += delegate { RemoveOverride(); };
            overrideCard.Controls.Add(overrideRemove);

            CardPanel miscCard = new CardPanel();
            miscCard.Location = new Point(rightX, topY + 168 + gap + 216 + gap + 120 + gap);
            miscCard.Size = new Size(colW, 164);
            Controls.Add(miscCard);

            Label portableTitle = MakeLabel("Настройки рядом с программой", 20, 10, Theme.FontCardTitle, Theme.Text);
            miscCard.Controls.Add(portableTitle);
            Label portableSub = MakeLabel("okna.ini вместо реестра — для флешки", 20, 30, Theme.FontSmall, Theme.Muted);
            miscCard.Controls.Add(portableSub);

            _portableSwitch = new JellySwitch();
            _portableSwitch.Location = new Point(372, 14);
            miscCard.Controls.Add(_portableSwitch);

            _portableLabel = MakeLabel("", 20, 50, Theme.FontTiny, Theme.Faint);
            _portableLabel.AutoSize = false;
            _portableLabel.Size = new Size(400, 16);
            miscCard.Controls.Add(_portableLabel);

            JellyButton export = new JellyButton();
            export.Text = "Экспорт…";
            export.Location = new Point(20, 76);
            export.Size = new Size(100, 28);
            export.Font = Theme.FontSmall;
            export.Click += delegate { ExportSettings(); };
            miscCard.Controls.Add(export);

            JellyButton import = new JellyButton();
            import.Text = "Импорт…";
            import.Location = new Point(128, 76);
            import.Size = new Size(100, 28);
            import.Font = Theme.FontSmall;
            import.Click += delegate { ImportSettings(); };
            miscCard.Controls.Add(import);

            JellyButton reset = new JellyButton();
            reset.Text = "Сбросить";
            reset.Location = new Point(236, 76);
            reset.Size = new Size(100, 28);
            reset.Font = Theme.FontSmall;
            reset.Click += delegate { ResetSettings(); };
            miscCard.Controls.Add(reset);

            JellyButton diagnostics = new JellyButton();
            diagnostics.Text = "Диагностика…";
            diagnostics.Location = new Point(344, 76);
            diagnostics.Size = new Size(100, 28);
            diagnostics.Font = Theme.FontSmall;
            diagnostics.Click += delegate
            {
                Diagnostics.Show(this, _settings, _engine,
                    _tray != null ? _tray.HotkeyStatus : Loc.T("неизвестно"));
            };
            miscCard.Controls.Add(diagnostics);

            Label languageLabel = MakeLabel("Язык", 20, 110, Theme.FontLabel, Theme.Text);
            miscCard.Controls.Add(languageLabel);

            _languageAuto = new JellyButton();
            _languageAuto.Text = "Системный";
            _languageAuto.Location = new Point(96, 106);
            _languageAuto.Size = new Size(96, 26);
            _languageAuto.Font = Theme.FontSmall;
            _languageAuto.Tag = (int)UiLanguage.Auto;
            _languageAuto.Click += delegate { SetLanguage((int)UiLanguage.Auto); };
            miscCard.Controls.Add(_languageAuto);

            _languageRu = new JellyButton();
            _languageRu.Text = "Русский";
            _languageRu.Location = new Point(200, 106);
            _languageRu.Size = new Size(96, 26);
            _languageRu.Font = Theme.FontSmall;
            _languageRu.Tag = (int)UiLanguage.Russian;
            _languageRu.Click += delegate { SetLanguage((int)UiLanguage.Russian); };
            miscCard.Controls.Add(_languageRu);

            _languageEn = new JellyButton();
            _languageEn.Text = "English";
            _languageEn.Location = new Point(304, 106);
            _languageEn.Size = new Size(96, 26);
            _languageEn.Font = Theme.FontSmall;
            _languageEn.Tag = (int)UiLanguage.English;
            _languageEn.Click += delegate { SetLanguage((int)UiLanguage.English); };
            miscCard.Controls.Add(_languageEn);

            Label themeLabel = MakeLabel("Тема оформления", 20, 138, Theme.FontLabel, Theme.Text);
            miscCard.Controls.Add(themeLabel);

            _darkThemeSwitch = new JellySwitch();
            _darkThemeSwitch.Location = new Point(96, 142);
            _darkThemeSwitch.CheckedChanged += delegate
            {
                _settings.DarkTheme = _darkThemeSwitch.Checked;
                ApplyTheme();
                SaveSoon();
            };
            miscCard.Controls.Add(_darkThemeSwitch);

            Label themeSub = MakeLabel("Тёмная тема настроек", 20, 162, Theme.FontSmall, Theme.Muted);
            miscCard.Controls.Add(themeSub);

            // Hint line above the bottom strip — kept short so it does not
            // collide with the Done button on the right.
            _hintLabel = MakeLabel("", 20, 728, Theme.FontSmall, Theme.Faint);
            _hintLabel.AutoSize = false;
            _hintLabel.Size = new Size(440, 16);
            Controls.Add(_hintLabel);

            _versionLabel = MakeLabel("", 20, 756, Theme.FontTiny, Theme.Faint);
            UpdateVersionLabel();
            Controls.Add(_versionLabel);

            Label copyrightLabel = MakeLabel("© Сергей", ClientSize.Width - 90, 756, Theme.FontTiny, Theme.Faint);
            copyrightLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(copyrightLabel);

            LinkLabel telegramLink = new LinkLabel();
            telegramLink.Text = "Обратная связь: @Link_Ser";
            telegramLink.Location = new Point(ClientSize.Width - 200, 776);
            telegramLink.Size = new Size(180, 18);
            telegramLink.Font = Theme.FontTiny;
            telegramLink.LinkColor = Theme.Accent;
            telegramLink.ActiveLinkColor = Theme.Accent;
            telegramLink.LinkBehavior = LinkBehavior.HoverUnderline;
            telegramLink.Click += delegate
            {
                try { System.Diagnostics.Process.Start("https://t.me/Link_Ser"); } catch { }
            };
            Controls.Add(telegramLink);

            JellyButton done = new JellyButton();
            done.Text = "Готово";
            done.Accent = true;
            done.Location = new Point(ClientSize.Width - 108, 728);
            done.Size = new Size(96, 36);
            done.Click += delegate { Hide(); };
            Controls.Add(done);

            ToolTip tip = new ToolTip();
            tip.SetToolTip(_effectSwitch, "Включить или выключить эффект");
            tip.SetToolTip(_slider, "0–200: насколько сильно окно отстаёт и изгибается; 200 — максимально вязко");
            tip.SetToolTip(_wobbleSwitch, "Добавляет боковое покачивание в конце движения");
            tip.SetToolTip(_bendSwitch, "Настоящая гнутость: окно на время перетаскивания замирает и изгибается");
            tip.SetToolTip(_maxShakeSwitch, "Окно качается, когда разворачивается на весь экран, восстанавливается или возвращается из панели задач");
            tip.SetToolTip(_closeNone, "Закрывать окна без эффекта");
            tip.SetToolTip(_closeShatter, "Окно рассыпается осколками, когда закрывается кнопкой или по Alt+F4; если программа остаётся в трее, окно скатывается в трубочку и уезжает в трей");
            tip.SetToolTip(_closeBurn, "Окно прогорает снизу вверх: огонь, обугленный край, угли и дым; если программа остаётся в трее, окно скатывается в трубочку и уезжает в трей");
            tip.SetToolTip(_closeFade, "Окно плавно растворяется при закрытии; если программа остаётся в трее, окно скатывается в трубочку и уезжает в трей");
            tip.SetToolTip(_rollSwitch, "При сворачивании окно скатывается в трубочку и уезжает в панель задач");
            tip.SetToolTip(_hotkeyButton, "Нажмите и введите сочетание, например Ctrl+Alt+K");
            tip.SetToolTip(_portableSwitch, "Хранить настройки в okna.ini рядом с Okna.exe");
            tip.SetToolTip(excludeRunning, "Выбрать из окон, которые открыты прямо сейчас");
        }


        private static Label MakeLabel(string text, int x, int y, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.AutoSize = true;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }

        private void WireEvents()
        {
            _effectSwitch.CheckedChanged += delegate
            {
                _settings.Enabled = _effectSwitch.Checked;
                if (_settings.Enabled) StartupGuard.NoteEffectsEnabled();
                SaveSoon();
                _statusPill.SetStatus(_engine.PauseReason ?? _engine.Status);
            };

            _wobbleSwitch.CheckedChanged += delegate
            {
                _settings.ReleaseWobble = _wobbleSwitch.Checked;
                SaveSoon();
            };

            _bendSwitch.CheckedChanged += delegate
            {
                _settings.Bend = _bendSwitch.Checked;
                if (!_syncing && _settings.Economy) _settings.Economy = false;
                UpdatePresetStyles();
                SaveSoon();
            };

            _maxShakeSwitch.CheckedChanged += delegate
            {
                _settings.MaxShake = _maxShakeSwitch.Checked;
                if (!_syncing && _settings.Economy) _settings.Economy = false;
                UpdatePresetStyles();
                SaveSoon();
            };

            _closeNone.Click += delegate { SetCloseEffect(0); };
            _closeShatter.Click += delegate { SetCloseEffect(1); };
            _closeBurn.Click += delegate { SetCloseEffect(2); };
            _closeFade.Click += delegate { SetCloseEffect(3); };

            _rollSwitch.CheckedChanged += delegate
            {
                _settings.Roll = _rollSwitch.Checked;
                SaveSoon();
            };

            for (int i = 0; i < _directionButtons.Length; i++)
            {
                int index = i;
                _directionButtons[i].Click += delegate
                {
                    _settings.RollDirection = (RollDirection)index;
                    UpdateDirectionStyles();
                    SaveSoon();
                };
            }

            _startupSwitch.CheckedChanged += delegate
            {
                _settings.StartWithWindows = _startupSwitch.Checked;
                AppSettings.SetAutoStart(_startupSwitch.Checked, Application.ExecutablePath);
                SaveSoon();
            };

            _autoStartElevatedSwitch.CheckedChanged += delegate
            {
                _settings.AutoStartElevated = _autoStartElevatedSwitch.Checked;
                AppSettings.SetAutoStartElevated(_autoStartElevatedSwitch.Checked, Application.ExecutablePath);
                SaveSoon();
            };

            _callSwitch.CheckedChanged += delegate
            {
                _settings.PauseOnCall = _callSwitch.Checked;
                SaveSoon();
            };

            _batterySwitch.CheckedChanged += delegate
            {
                _settings.PauseOnBattery = _batterySwitch.Checked;
                SaveSoon();
            };

            _motionSwitch.CheckedChanged += delegate
            {
                _settings.RespectReduceMotion = _motionSwitch.Checked;
                SaveSoon();
            };

            _portableSwitch.CheckedChanged += delegate
            {
                bool wanted = _portableSwitch.Checked;
                if (!_settings.SetPortable(wanted))
                {
                    MessageBox.Show(this,
                        Loc.T("Не удалось записать okna.ini рядом с программой. " +
                              "Скопируйте Okna.exe в папку, где можно писать, или оставьте настройки в реестре."),
                        Loc.T("Окна"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _portableSwitch.Checked = _settings.IsPortable;
                }
                UpdatePortableLabel();
                SaveSoon();
            };

            _hotkeyButton.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;

                if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
                {
                    _settings.HotkeyKey = 0;
                    _settings.HotkeyModifiers = 0;
                    ApplyHotkey();
                    return;
                }

                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey ||
                    e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                    return;

                Keys modifiers = Control.ModifierKeys;
                int mods = 0;
                if ((modifiers & Keys.Control) != 0) mods |= (int)Native.MOD_CONTROL;
                if ((modifiers & Keys.Alt) != 0) mods |= (int)Native.MOD_ALT;
                if ((modifiers & Keys.Shift) != 0) mods |= (int)Native.MOD_SHIFT;
                // A global hotkey without a modifier would swallow ordinary
                // typing, so require at least one.
                if (mods == 0) return;

                _settings.HotkeyModifiers = mods;
                _settings.HotkeyKey = (int)e.KeyCode;
                ApplyHotkey();
            };

            _slider.ValueChanged += delegate
            {
                // Moving the slider is a manual override: it leaves the
                // economy preset, which otherwise keeps the live copy and the
                // bend switched off and makes the window look rigid at any
                // value. SyncFromSettings raises this event too; the guard
                // keeps the preset's own state intact while it is applied.
                if (_syncing) return;
                _settings.LeaveEconomy();
                _settings.Jelly = _slider.Value;
                _bendSwitch.Checked = _settings.Bend;
                _maxShakeSwitch.Checked = _settings.MaxShake;
                UpdateJellyLabel();
                UpdatePresetStyles();
                SaveSoon();
            };

            for (int i = 0; i < _presets.Length; i++)
            {
                int index = i;
                _presets[i].Click += delegate
                {
                    // The presets set the effect switches as well as the
                    // value. Choosing "Желе" right after "Экономно" used to
                    // leave bend and shake off, so the window stayed rigid.
                    _settings.ApplyPreset(index);
                    Persist();
                    SyncFromSettings();
                };
            }
        }

        internal void ApplyHotkey()
        {
            Persist();
            bool ok = _tray == null || _tray.ApplyHotkey(true);
            _hotkeyButton.Text = _settings.HotkeyLabel;
            _hotkeyStatus.Text = _tray != null
                ? _tray.HotkeyStatus
                : Loc.T(ok ? "работает" : "занята");
            _hotkeyStatus.ForeColor = ok ? Theme.Muted : Color.FromArgb(232, 163, 61);
            UpdateHint();
        }

        public void SyncFromSettings()
        {
            _syncing = true;
            try
            {
                _effectSwitch.Checked = _settings.Enabled;
                _wobbleSwitch.Checked = _settings.ReleaseWobble;
                _bendSwitch.Checked = _settings.Bend;
                _maxShakeSwitch.Checked = _settings.MaxShake;
                UpdateCloseStyles();
                _rollSwitch.Checked = _settings.Roll;
                UpdateDirectionStyles();
                _startupSwitch.Checked = _settings.StartWithWindows;
                _autoStartElevatedSwitch.Checked = _settings.AutoStartElevated;
                _darkThemeSwitch.Checked = _settings.DarkTheme;
                _callSwitch.Checked = _settings.PauseOnCall;
                _batterySwitch.Checked = _settings.PauseOnBattery;
                _motionSwitch.Checked = _settings.RespectReduceMotion;
                _portableSwitch.Checked = _settings.IsPortable;
                UpdatePortableLabel();
                _hotkeyButton.Text = _settings.HotkeyLabel;
                _hotkeyStatus.Text = _tray != null ? _tray.HotkeyStatus : "";
                _hotkeyStatus.ForeColor = Theme.Muted;
                RefreshExcludeList();
                RefreshOverrideList();
                _slider.Value = _settings.Jelly;
                UpdateJellyLabel();
                UpdatePresetStyles();
                UpdateLanguageStyles();
                UpdateHint();
                _statusPill.SetStatus(_engine.PauseReason ?? _engine.Status);
            }
            finally
            {
                _syncing = false;
            }
        }

        private void UpdateHint()
        {
            if (_hintLabel == null) return;
            _hintLabel.Text =
                Loc.T("Потяните это окно за заголовок — оно желейное  ·  Shift — обычное перетаскивание  ·  ") +
                _settings.HotkeyLabel + Loc.T(" — пауза");
        }

        private void UpdateVersionLabel()
        {
            if (_versionLabel == null) return;
            _versionLabel.Text = Updater.CurrentLabel + "  ·  " + Loc.T("живёт в трее");
        }

        private void UpdatePortableLabel()
        {
            if (_portableLabel == null) return;
            _portableLabel.Text = _settings.IsPortable && !string.IsNullOrEmpty(_settings.PortablePath)
                ? Loc.T("Файл: ") + _settings.PortablePath
                : Loc.T("Сейчас настройки хранятся в реестре (HKCU\\Software\\Okna)");
        }

        private void UpdateJellyLabel()
        {
            _valueLabel.Text = _settings.Jelly + "  ·  " + _settings.JellyLabel;
        }

        /// <summary>Applies the dark theme to the settings window controls.</summary>
        internal void ApplyTheme()
        {
            if (_settings.DarkTheme)
            {
                BackColor = Color.FromArgb(32, 32, 36);
                ForeColor = Color.FromArgb(224, 224, 224);
            }
            else
            {
                BackColor = Theme.Bg;
                ForeColor = Theme.Text;
            }
            // Repaint all child containers so the new colors take effect.
            foreach (Control c in Controls) c.Invalidate();
        }

        /// <summary>
        /// 0 = no effect, 1 = shards, 2 = fire, 3 = fade. The effects are
        /// alternatives, never both on.
        /// </summary>
        private void SetCloseEffect(int effect)
        {
            _settings.Shatter = effect == 1;
            _settings.Burn = effect == 2;
            _settings.Fade = effect == 3;
            UpdateCloseStyles();
            SaveSoon();
        }

        private void UpdateCloseStyles()
        {
            int selected = _settings.Burn ? 2 : (_settings.Shatter ? 1 : (_settings.Fade ? 3 : 0));
            JellyButton[] buttons = new JellyButton[] { _closeNone, _closeShatter, _closeBurn, _closeFade };
            for (int i = 0; i < buttons.Length; i++)
            {
                bool accent = i == selected;
                if (buttons[i].Accent != accent)
                {
                    buttons[i].Accent = accent;
                    buttons[i].Invalidate();
                }
            }
        }

        private void UpdateDirectionStyles()
        {
            if (_directionButtons == null) return;
            RollDirection selected = _settings.RollDirection;
            for (int i = 0; i < _directionButtons.Length; i++)
            {
                bool accent = (RollDirection)i == selected;
                if (_directionButtons[i].Accent != accent)
                {
                    _directionButtons[i].Accent = accent;
                    _directionButtons[i].Invalidate();
                }
            }
        }

        private void UpdatePresetStyles()
        {
            int value = _slider == null ? _settings.Jelly : _slider.Value;
            int selected = _settings.Economy ? 3 : (value < 42 ? 0 : (value < 100 ? 1 : 2));
            for (int i = 0; i < _presets.Length; i++)
            {
                bool accent = i == selected;
                if (_presets[i].Accent != accent)
                {
                    _presets[i].Accent = accent;
                    _presets[i].Invalidate();
                }
            }
        }

        /// <summary>
        /// The language is applied immediately: the static controls are
        /// re-walked, the dynamic labels are rebuilt, and the tray menu is
        /// asked to do the same.
        /// </summary>
        private void SetLanguage(int language)
        {
            _settings.Language = language;
            Loc.Configure(language);
            Loc.Apply(this);
            UpdateLanguageStyles();
            UpdateVersionLabel();
            SyncFromSettings();
            if (_tray != null) _tray.ApplyLanguage();
            Persist();
        }

        private void UpdateLanguageStyles()
        {
            JellyButton[] buttons = new JellyButton[] { _languageAuto, _languageRu, _languageEn };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                bool accent = (int)buttons[i].Tag == _settings.Language;
                if (buttons[i].Accent != accent)
                {
                    buttons[i].Accent = accent;
                    buttons[i].Invalidate();
                }
            }
        }

        private void SaveSoon()
        {
            if (SmokeMode || _saveTimer == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Writes the settings unless the form is only being smoke-tested.</summary>
        private void Persist()
        {
            if (SmokeMode) return;
            _settings.Save();
        }

        private void DrawExcludeItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(selected ? Theme.BorderSoft : Theme.CardAlt))
                e.Graphics.FillRectangle(back, e.Bounds);
            string text = Convert.ToString(_excludeList.Items[e.Index]);
            TextRenderer.DrawText(e.Graphics, text, _excludeList.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void DrawOverrideItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(selected ? Theme.BorderSoft : Theme.CardAlt))
                e.Graphics.FillRectangle(back, e.Bounds);
            string text = Convert.ToString(_overrideList.Items[e.Index]);
            TextRenderer.DrawText(e.Graphics, text, _overrideList.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void RefreshExcludeList()
        {
            if (_excludeList == null) return;
            _excludeList.BeginUpdate();
            _excludeList.Items.Clear();
            string[] names = _settings.Exclude;
            for (int i = 0; i < names.Length; i++)
                _excludeList.Items.Add(names[i]);
            _excludeList.EndUpdate();
        }

        private void RefreshOverrideList()
        {
            if (_overrideList == null) return;
            _overrideList.BeginUpdate();
            _overrideList.Items.Clear();
            string[] list = _settings.AppProfiles;
            for (int i = 0; i < list.Length; i++)
            {
                string mask;
                int effect;
                bool bend;
                bool shake;
                bool roll;
                if (!AppSettings.ParseProfile(list[i], out mask, out effect, out bend,
                        out shake, out roll))
                    continue;
                string label = effect == 2 ? "сгорание" : (effect == 1 ? "осколки" : "без эффекта");
                string notes = "";
                if (!bend) notes += " · без изгиба";
                if (!shake) notes += " · без тряски";
                if (!roll) notes += " · без скатывания";
                _overrideList.Items.Add(mask + "  —  " + label + notes);
            }
            _overrideList.EndUpdate();
        }

        private void AddExclusions()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Программы без эффектов";
                dialog.Filter = "Программы (*.exe)|*.exe";
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string[] files = dialog.FileNames;
                for (int i = 0; i < files.Length; i++)
                    _settings.AddExclude(Path.GetFileName(files[i]));
                RefreshExcludeList();
                SaveSoon();
            }
        }

        private void PickRunning()
        {
            WindowPicker.ShowPicker(this, _settings);
            RefreshExcludeList();
            SaveSoon();
        }

        private void AddMask()
        {
            string mask = _excludeMask.Text.Trim();
            if (mask.Length == 0) return;
            _settings.AddExclude(mask);
            _excludeMask.Text = "";
            RefreshExcludeList();
            SaveSoon();
        }

        private void RemoveSelectedExclusion()
        {
            string name = _excludeList.SelectedItem as string;
            if (name == null) return;
            _settings.RemoveExclude(name);
            RefreshExcludeList();
            SaveSoon();
        }

        private void AddOverride()
        {
            string program;
            int effect;
            bool bend;
            bool shake;
            bool roll;
            if (!CloseOverrideDialog.ShowFor(this, _settings, "", out program, out effect,
                    out bend, out shake, out roll))
                return;
            _settings.SetAppProfile(program, effect, bend, shake, roll);
            RefreshOverrideList();
            SaveSoon();
        }

        private void RemoveOverride()
        {
            int index = _overrideList.SelectedIndex;
            if (index < 0) return;
            string[] list = _settings.AppProfiles;
            if (index >= list.Length) return;
            string mask;
            int effect;
            bool bend;
            bool shake;
            bool roll;
            if (!AppSettings.ParseProfile(list[index], out mask, out effect, out bend,
                    out shake, out roll))
                return;
            _settings.RemoveCloseOverride(mask);
            RefreshOverrideList();
            SaveSoon();
        }

        private void ExportSettings()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Loc.T("Экспорт настроек");
                dialog.Filter = Loc.T("Настройки Окон (*.ini)|*.ini|Все файлы (*.*)|*.*");
                dialog.FileName = "okna.ini";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _settings.Export(dialog.FileName);
                MessageBox.Show(this, Loc.T("Настройки сохранены в ") + dialog.FileName,
                    Loc.T("Окна"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ImportSettings()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Loc.T("Импорт настроек");
                dialog.Filter = Loc.T("Настройки Окон (*.ini)|*.ini|Все файлы (*.*)|*.*");
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!_settings.Import(dialog.FileName))
                {
                    MessageBox.Show(this, Loc.T("В файле нет настроек Окон."),
                        Loc.T("Окна"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Persist();
                SyncFromSettings();
                if (_tray != null) _tray.ApplyHotkey(true);
                MessageBox.Show(this, Loc.T("Настройки загружены."), Loc.T("Окна"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ResetSettings()
        {
            if (MessageBox.Show(this, Loc.T("Вернуть все настройки к заводским?"),
                    Loc.T("Окна"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _settings.ResetAll();
            Persist();
            SyncFromSettings();
            if (_tray != null) _tray.ApplyHotkey(true);
        }

        /// <summary>
        /// Exercises the real controls without saving anything: presets,
        /// slider, switches, close effects, roll direction, both lists and the
        /// diagnostics text. Used by --ui-smoke.
        /// </summary>
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
                for (int i = 0; i < _presets.Length; i++)
                {
                    _presets[i].PerformClick();
                    Application.DoEvents();
                    check("preset " + i + " applies",
                        _settings.CurrentPresetIndex == i);
                }

                _slider.Value = 150;
                Application.DoEvents();
                check("the slider reaches the settings", _settings.Jelly == 150);

                bool bend = _settings.Bend;
                _bendSwitch.Checked = !bend;
                Application.DoEvents();
                check("the bend switch changes the setting", _settings.Bend == !bend);
                _bendSwitch.Checked = bend;

                bool roll = _settings.Roll;
                _rollSwitch.Checked = !roll;
                Application.DoEvents();
                check("the roll switch changes the setting", _settings.Roll == !roll);
                _rollSwitch.Checked = roll;

                _closeBurn.PerformClick();
                check("burn is chosen alone", _settings.Burn && !_settings.Shatter);
                _closeShatter.PerformClick();
                check("shards are chosen alone", _settings.Shatter && !_settings.Burn);
                _closeNone.PerformClick();
                check("no close effect can be chosen", !_settings.Shatter && !_settings.Burn);

                for (int i = 0; i < _directionButtons.Length; i++)
                {
                    _directionButtons[i].PerformClick();
                    check("roll direction " + i + " applies",
                        (int)_settings.RollDirection == i);
                }

                _settings.AddExclude("smoke-test.exe");
                RefreshExcludeList();
                check("the exclusion list shows a new mask", _excludeList.Items.Count > 0);
                _settings.RemoveExclude("smoke-test.exe");
                RefreshExcludeList();

                _settings.SetAppProfile("smoke-test.exe", 2, true, false, false);
                RefreshOverrideList();
                check("the program list shows a new rule", _overrideList.Items.Count > 0);
                _settings.RemoveCloseOverride("smoke-test.exe");
                RefreshOverrideList();

                int originalLanguage = _settings.Language;
                _languageEn.PerformClick();
                Application.DoEvents();
                check("English changes the language",
                    _settings.Language == (int)UiLanguage.English);
                check("English translates a visible button", _presets[0].Text == "Calm");
                _languageRu.PerformClick();
                Application.DoEvents();
                check("Russian comes back",
                    _settings.Language == (int)UiLanguage.Russian &&
                    _presets[0].Text == "Спокойно");
                _settings.Language = originalLanguage;
                Loc.Configure(originalLanguage);
                Loc.Apply(this);
                SyncFromSettings();

                string report = Diagnostics.BuildReport(_settings, _engine, "smoke");
                check("the diagnostics report has content", report.Length > 200);
                check("the diagnostics report names the program",
                    report.IndexOf("Okna", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception ex)
            {
                failed++;
                lines.Add("[FAIL] the smoke test crashed: " + ex.Message);
            }
            finally
            {
                // Keep smoke mode on: the form is disposed right after the
                // check, and OnFormClosing must not write the test values.
                SmokeMode = true;
            }

            lines.Add("");
            lines.Add(failed == 0
                ? "All " + lines.Count + " UI checks passed."
                : failed + " of " + lines.Count + " UI checks failed.");
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        public void ShowSettings()
        {
            if (Visible)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                BringToFront();
                return;
            }
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();

            // The settings window is the jelly test itself now: the first time
            // it appears it wobbles once, so the effect can be seen without
            // dragging anything.
            if (!_jellyDemoDone)
            {
                _jellyDemoDone = true;
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (Visible) _engine.ShakeWindow(Handle);
                }));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ReallyExit && e.CloseReason != CloseReason.WindowsShutDown &&
                e.CloseReason != CloseReason.TaskManagerClosing)
            {
                e.Cancel = true;
                Persist();
                BeginInvoke(new MethodInvoker(delegate { Hide(); }));
                return;
            }
            Persist();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_statusTimer != null) _statusTimer.Dispose();
                if (_saveTimer != null) _saveTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
