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

        private JellyPreview _preview;
        private JellySwitch _effectSwitch;
        private JellySwitch _wobbleSwitch;
        private JellySwitch _bendSwitch;
        private JellySwitch _maxShakeSwitch;
        private JellyButton _closeNone;
        private JellyButton _closeShatter;
        private JellyButton _closeBurn;
        private JellySwitch _rollSwitch;
        private JellyButton[] _directionButtons;
        private JellySwitch _startupSwitch;
        private JellySwitch _callSwitch;
        private JellySwitch _batterySwitch;
        private JellySwitch _motionSwitch;
        private JellySwitch _portableSwitch;
        private Label _portableLabel;
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
        private Timer _statusTimer;
        private Timer _saveTimer;

        public bool ReallyExit { get; set; }

        public SettingsForm(AppSettings settings, JellyEngine engine, Icon icon, TrayApp tray)
        {
            _settings = settings;
            _engine = engine;
            _icon = icon;
            _tray = tray;

            Text = "Окна";
            Icon = icon;
            ClientSize = new Size(560, 2040);
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
            // The design is taller than a 1366x768 screen or a 1080p display at
            // 150%. Clamp the window to the working area in OnLoad and scroll
            // the rest; otherwise the bottom buttons are simply unreachable.
            AutoScroll = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw, true);

            BuildUi();
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
                _settings.Save();
            };

            SyncFromSettings();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.EnableDarkTitleBar(Handle);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            Rectangle area = Screen.FromControl(this).WorkingArea;
            int maxHeight = Math.Max(360, area.Height - 48);
            if (ClientSize.Height > maxHeight)
                ClientSize = new Size(ClientSize.Width, maxHeight);

            // Scroll only as far as the content actually goes. The width stays
            // out of it so the vertical scrollbar does not trigger a second,
            // horizontal one.
            int contentBottom = 0;
            foreach (Control c in Controls)
                if (c.Bottom > contentBottom) contentBottom = c.Bottom;
            AutoScrollMinSize = new Size(0, contentBottom + 8);
            AutoScrollPosition = Point.Empty;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CLOSE = 0x0010;
            if (m.Msg == WM_CLOSE && !ReallyExit && !Environment.HasShutdownStarted)
            {
                _settings.Save();
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
            PictureBox logo = new PictureBox();
            logo.Location = new Point(24, 20);
            logo.Size = new Size(46, 46);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Color.Transparent;
            try
            {
                using (Icon sized = new Icon(_icon, 46, 46))
                    logo.Image = sized.ToBitmap();
            }
            catch { logo.Image = _icon.ToBitmap(); }
            Controls.Add(logo);

            Label title = MakeLabel("Окна", 80, 14, Theme.FontTitle, Theme.Text);
            Controls.Add(title);

            Label subtitle = MakeLabel("Инерционные окна для Windows", 82, 49, Theme.FontSmall, Theme.Muted);
            Controls.Add(subtitle);

            _statusPill = new StatusPill();
            _statusPill.Location = new Point(394, 26);
            _statusPill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(_statusPill);

            _preview = new JellyPreview(_settings);
            _preview.Location = new Point(24, 86);
            _preview.Size = new Size(512, 160);
            _preview.BackColor = Theme.Bg;
            Controls.Add(_preview);

            CardPanel moveCard = new CardPanel();
            moveCard.Location = new Point(24, 260);
            moveCard.Size = new Size(512, 196);
            Controls.Add(moveCard);

            Label effectTitle = MakeLabel("Желеобразные окна", 20, 31, Theme.FontCardTitle, Theme.Text);
            moveCard.Controls.Add(effectTitle);
            Label effectSub = MakeLabel("Перетаскивание за заголовок", 20, 55, Theme.FontSmall, Theme.Muted);
            moveCard.Controls.Add(effectSub);

            _effectSwitch = new JellySwitch();
            _effectSwitch.Location = new Point(432, 40);
            moveCard.Controls.Add(_effectSwitch);

            Panel divider = new Panel();
            divider.Location = new Point(20, 90);
            divider.Size = new Size(472, 1);
            divider.BackColor = Theme.BorderSoft;
            moveCard.Controls.Add(divider);

            Label jellyLabel = MakeLabel("Желейность", 20, 104, Theme.FontLabel, Theme.Text);
            moveCard.Controls.Add(jellyLabel);

            _valueLabel = MakeLabel("", 370, 104, Theme.FontSmall, Theme.Accent);
            _valueLabel.Size = new Size(122, 18);
            _valueLabel.TextAlign = ContentAlignment.MiddleRight;
            moveCard.Controls.Add(_valueLabel);

            _slider = new JellySlider();
            _slider.Location = new Point(20, 126);
            _slider.Size = new Size(472, 26);
            moveCard.Controls.Add(_slider);

            _presets = new JellyButton[4];
            string[] names = new string[] { "Спокойно", "Баланс", "Желе", "Экономно" };
            for (int i = 0; i < 4; i++)
            {
                JellyButton b = new JellyButton();
                b.Text = names[i];
                b.Location = new Point(20 + i * 120, 160);
                b.Size = new Size(112, 28);
                b.Font = Theme.FontSmall;
                b.Tag = i;
                _presets[i] = b;
                moveCard.Controls.Add(b);
            }

            CardPanel optionsCard = new CardPanel();
            optionsCard.Location = new Point(24, 470);
            optionsCard.Size = new Size(512, 510);
            Controls.Add(optionsCard);

            Label wobbleTitle = MakeLabel("Покачивание при отпускании", 20, 14, Theme.FontCardTitle, Theme.Text);
            optionsCard.Controls.Add(wobbleTitle);
            Label wobbleSub = MakeLabel("Боковое качание и упругое сжатие после движения", 20, 36, Theme.FontSmall, Theme.Muted);
            optionsCard.Controls.Add(wobbleSub);

            _wobbleSwitch = new JellySwitch();
            _wobbleSwitch.Location = new Point(432, 18);
            optionsCard.Controls.Add(_wobbleSwitch);

            Panel divider2 = new Panel();
            divider2.Location = new Point(20, 58);
            divider2.Size = new Size(472, 1);
            divider2.BackColor = Theme.BorderSoft;
            optionsCard.Controls.Add(divider2);

            Label bendTitle = MakeLabel("Гнуть окна по инерции", 20, 66, Theme.FontCardTitle, Theme.Text);
            optionsCard.Controls.Add(bendTitle);
            Label bendSub = MakeLabel("Края изгибаются при разгоне и торможении", 20, 88, Theme.FontSmall, Theme.Muted);
            optionsCard.Controls.Add(bendSub);

            _bendSwitch = new JellySwitch();
            _bendSwitch.Location = new Point(432, 70);
            optionsCard.Controls.Add(_bendSwitch);

            Panel divider3 = new Panel();
            divider3.Location = new Point(20, 110);
            divider3.Size = new Size(472, 1);
            divider3.BackColor = Theme.BorderSoft;
            optionsCard.Controls.Add(divider3);

            Label maxShakeTitle = MakeLabel("Тряска при разворачивании", 20, 118, Theme.FontCardTitle, Theme.Text);
            optionsCard.Controls.Add(maxShakeTitle);
            Label maxShakeSub = MakeLabel("Качается при развороте, восстановлении и возврате", 20, 140, Theme.FontSmall, Theme.Muted);
            optionsCard.Controls.Add(maxShakeSub);

            _maxShakeSwitch = new JellySwitch();
            _maxShakeSwitch.Location = new Point(432, 122);
            optionsCard.Controls.Add(_maxShakeSwitch);

            Panel divider4 = new Panel();
            divider4.Location = new Point(20, 158);
            divider4.Size = new Size(472, 1);
            divider4.BackColor = Theme.BorderSoft;
            optionsCard.Controls.Add(divider4);

            Label closeTitle = MakeLabel("Эффект при закрытии", 20, 166, Theme.FontCardTitle, Theme.Text);
            optionsCard.Controls.Add(closeTitle);
            Label closeSub = MakeLabel("Если программа остаётся в трее — окно скатывается туда", 20, 188, Theme.FontSmall, Theme.Muted);
            optionsCard.Controls.Add(closeSub);

            _closeNone = new JellyButton();
            _closeNone.Text = "Без эффекта";
            _closeNone.Location = new Point(20, 210);
            _closeNone.Size = new Size(152, 28);
            _closeNone.Font = Theme.FontSmall;
            optionsCard.Controls.Add(_closeNone);

            _closeShatter = new JellyButton();
            _closeShatter.Text = "Осколки";
            _closeShatter.Location = new Point(180, 210);
            _closeShatter.Size = new Size(152, 28);
            _closeShatter.Font = Theme.FontSmall;
            optionsCard.Controls.Add(_closeShatter);

            _closeBurn = new JellyButton();
            _closeBurn.Text = "Сгорание";
            _closeBurn.Location = new Point(340, 210);
            _closeBurn.Size = new Size(152, 28);
            _closeBurn.Font = Theme.FontSmall;
            optionsCard.Controls.Add(_closeBurn);

            JellyButton previewShatter = new JellyButton();
            previewShatter.Text = "Показать осколки";
            previewShatter.Location = new Point(20, 246);
            previewShatter.Size = new Size(152, 28);
            previewShatter.Font = Theme.FontSmall;
            previewShatter.Click += delegate { EffectPreview.Play(_preview, 1); };
            optionsCard.Controls.Add(previewShatter);

            JellyButton previewBurn = new JellyButton();
            previewBurn.Text = "Показать сгорание";
            previewBurn.Location = new Point(180, 246);
            previewBurn.Size = new Size(152, 28);
            previewBurn.Font = Theme.FontSmall;
            previewBurn.Click += delegate { EffectPreview.Play(_preview, 2); };
            optionsCard.Controls.Add(previewBurn);

            JellyButton previewRoll = new JellyButton();
            previewRoll.Text = "Показать трубочку";
            previewRoll.Location = new Point(340, 246);
            previewRoll.Size = new Size(152, 28);
            previewRoll.Font = Theme.FontSmall;
            previewRoll.Click += delegate { EffectPreview.Play(_preview, 3); };
            optionsCard.Controls.Add(previewRoll);

            Panel divider5 = new Panel();
            divider5.Location = new Point(20, 288);
            divider5.Size = new Size(472, 1);
            divider5.BackColor = Theme.BorderSoft;
            optionsCard.Controls.Add(divider5);

            Label rollTitle = MakeLabel("Сворачивать в трубочку", 20, 296, Theme.FontCardTitle, Theme.Text);
            optionsCard.Controls.Add(rollTitle);
            Label rollSub = MakeLabel("Окно скатывается и уезжает в панель задач", 20, 318, Theme.FontSmall, Theme.Muted);
            optionsCard.Controls.Add(rollSub);

            _rollSwitch = new JellySwitch();
            _rollSwitch.Location = new Point(432, 300);
            optionsCard.Controls.Add(_rollSwitch);

            Label directionTitle = MakeLabel("Куда скатывать", 20, 334, Theme.FontLabel, Theme.Text);
            optionsCard.Controls.Add(directionTitle);
            Label directionSub = MakeLabel("Авто — в сторону панели задач, остальное — принудительно", 20, 356, Theme.FontSmall, Theme.Muted);
            optionsCard.Controls.Add(directionSub);

            _directionButtons = new JellyButton[5];
            string[] directionNames = new string[] { "Авто", "Вниз", "Вверх", "Влево", "Вправо" };
            for (int i = 0; i < 5; i++)
            {
                JellyButton b = new JellyButton();
                b.Text = directionNames[i];
                b.Location = new Point(20 + i * 94, 380);
                b.Size = new Size(88, 28);
                b.Font = Theme.FontSmall;
                b.Tag = (RollDirection)i;
                _directionButtons[i] = b;
                optionsCard.Controls.Add(b);
            }

            Panel divider6 = new Panel();
            divider6.Location = new Point(20, 422);
            divider6.Size = new Size(472, 1);
            divider6.BackColor = Theme.BorderSoft;
            optionsCard.Controls.Add(divider6);

            Label startupTitle = MakeLabel("Запускать с Windows", 20, 430, Theme.FontCardTitle, Theme.Text);
            optionsCard.Controls.Add(startupTitle);
            Label startupSub = MakeLabel("Тихо стартовать вместе с системой", 20, 452, Theme.FontSmall, Theme.Muted);
            optionsCard.Controls.Add(startupSub);

            _startupSwitch = new JellySwitch();
            _startupSwitch.Location = new Point(432, 432);
            optionsCard.Controls.Add(_startupSwitch);

            CardPanel pauseCard = new CardPanel();
            pauseCard.Location = new Point(24, 994);
            pauseCard.Size = new Size(512, 206);
            Controls.Add(pauseCard);

            Label pauseTitle = MakeLabel("Пауза сама по себе", 20, 14, Theme.FontCardTitle, Theme.Text);
            pauseCard.Controls.Add(pauseTitle);
            Label pauseSub = MakeLabel("Эффекты уступают место, когда вы заняты другим", 20, 36, Theme.FontSmall, Theme.Muted);
            pauseCard.Controls.Add(pauseSub);

            Label callTitle = MakeLabel("Звонки и показ экрана", 20, 66, Theme.FontCardTitle, Theme.Text);
            pauseCard.Controls.Add(callTitle);
            Label callSub = MakeLabel("Teams, Zoom, Discord, Slack и похожие впереди", 20, 88, Theme.FontSmall, Theme.Muted);
            pauseCard.Controls.Add(callSub);
            _callSwitch = new JellySwitch();
            _callSwitch.Location = new Point(432, 70);
            pauseCard.Controls.Add(_callSwitch);

            Label batteryTitle = MakeLabel("Питание от батареи", 20, 112, Theme.FontCardTitle, Theme.Text);
            pauseCard.Controls.Add(batteryTitle);
            Label batterySub = MakeLabel("Не тратить заряд на эффекты", 20, 134, Theme.FontSmall, Theme.Muted);
            pauseCard.Controls.Add(batterySub);
            _batterySwitch = new JellySwitch();
            _batterySwitch.Location = new Point(432, 116);
            pauseCard.Controls.Add(_batterySwitch);

            Label motionTitle = MakeLabel("Уважать уменьшение движения", 20, 158, Theme.FontCardTitle, Theme.Text);
            pauseCard.Controls.Add(motionTitle);
            Label motionSub = MakeLabel("Если анимации выключены в Windows — не мешать", 20, 180, Theme.FontSmall, Theme.Muted);
            pauseCard.Controls.Add(motionSub);
            _motionSwitch = new JellySwitch();
            _motionSwitch.Location = new Point(432, 162);
            pauseCard.Controls.Add(_motionSwitch);

            CardPanel hotkeyCard = new CardPanel();
            hotkeyCard.Location = new Point(24, 1214);
            hotkeyCard.Size = new Size(512, 126);
            Controls.Add(hotkeyCard);

            Label hotkeyTitle = MakeLabel("Горячая клавиша", 20, 14, Theme.FontCardTitle, Theme.Text);
            hotkeyCard.Controls.Add(hotkeyTitle);
            Label hotkeySub = MakeLabel("Нажмите на поле и введите сочетание; Backspace — выключить", 20, 36, Theme.FontSmall, Theme.Muted);
            hotkeyCard.Controls.Add(hotkeySub);

            _hotkeyButton = new JellyButton();
            _hotkeyButton.Text = "Ctrl+Alt+J";
            _hotkeyButton.Location = new Point(20, 66);
            _hotkeyButton.Size = new Size(200, 34);
            _hotkeyButton.Font = Theme.FontBody;
            _hotkeyButton.Click += delegate { _hotkeyButton.Focus(); };
            hotkeyCard.Controls.Add(_hotkeyButton);

            _hotkeyStatus = MakeLabel("", 236, 72, Theme.FontSmall, Theme.Muted);
            _hotkeyStatus.Size = new Size(256, 22);
            hotkeyCard.Controls.Add(_hotkeyStatus);

            CardPanel excludeCard = new CardPanel();
            excludeCard.Location = new Point(24, 1354);
            excludeCard.Size = new Size(512, 256);
            Controls.Add(excludeCard);

            Label excludeTitle = MakeLabel("Исключения", 20, 14, Theme.FontCardTitle, Theme.Text);
            excludeCard.Controls.Add(excludeTitle);
            Label excludeSub = MakeLabel("Здесь окна остаются обычными: программы, маски и классы окон", 20, 36, Theme.FontSmall, Theme.Muted);
            excludeCard.Controls.Add(excludeSub);

            _excludeList = new ListBox();
            _excludeList.Location = new Point(20, 62);
            _excludeList.Size = new Size(300, 110);
            _excludeList.DrawMode = DrawMode.OwnerDrawFixed;
            _excludeList.ItemHeight = 22;
            _excludeList.BorderStyle = BorderStyle.None;
            _excludeList.BackColor = Theme.CardAlt;
            _excludeList.ForeColor = Theme.Text;
            _excludeList.Font = Theme.FontSmall;
            _excludeList.IntegralHeight = false;
            _excludeList.DrawItem += DrawExcludeItem;
            excludeCard.Controls.Add(_excludeList);

            JellyButton excludeAdd = new JellyButton();
            excludeAdd.Text = "Добавить файл…";
            excludeAdd.Location = new Point(340, 62);
            excludeAdd.Size = new Size(152, 30);
            excludeAdd.Font = Theme.FontSmall;
            excludeAdd.Click += delegate { AddExclusions(); };
            excludeCard.Controls.Add(excludeAdd);

            JellyButton excludeRunning = new JellyButton();
            excludeRunning.Text = "Из запущенных…";
            excludeRunning.Location = new Point(340, 100);
            excludeRunning.Size = new Size(152, 30);
            excludeRunning.Font = Theme.FontSmall;
            excludeRunning.Click += delegate { PickRunning(); };
            excludeCard.Controls.Add(excludeRunning);

            JellyButton excludeRemove = new JellyButton();
            excludeRemove.Text = "Убрать";
            excludeRemove.Location = new Point(340, 138);
            excludeRemove.Size = new Size(152, 30);
            excludeRemove.Font = Theme.FontSmall;
            excludeRemove.Click += delegate { RemoveSelectedExclusion(); };
            excludeCard.Controls.Add(excludeRemove);

            Label maskLabel = MakeLabel("Маска: steam* или class:Progman", 20, 180, Theme.FontSmall, Theme.Muted);
            excludeCard.Controls.Add(maskLabel);

            _excludeMask = new TextBox();
            _excludeMask.Location = new Point(20, 202);
            _excludeMask.Size = new Size(300, 28);
            _excludeMask.BorderStyle = BorderStyle.FixedSingle;
            _excludeMask.BackColor = Theme.CardAlt;
            _excludeMask.ForeColor = Theme.Text;
            _excludeMask.Font = Theme.FontSmall;
            excludeCard.Controls.Add(_excludeMask);

            JellyButton addMask = new JellyButton();
            addMask.Text = "Добавить маску";
            addMask.Location = new Point(340, 200);
            addMask.Size = new Size(152, 30);
            addMask.Font = Theme.FontSmall;
            addMask.Click += delegate { AddMask(); };
            excludeCard.Controls.Add(addMask);

            CardPanel overrideCard = new CardPanel();
            overrideCard.Location = new Point(24, 1624);
            overrideCard.Size = new Size(512, 146);
            Controls.Add(overrideCard);

            Label overrideTitle = MakeLabel("Эффект для отдельной программы", 20, 14, Theme.FontCardTitle, Theme.Text);
            overrideCard.Controls.Add(overrideTitle);
            Label overrideSub = MakeLabel("Перекрывает общий выбор при закрытии", 20, 36, Theme.FontSmall, Theme.Muted);
            overrideCard.Controls.Add(overrideSub);

            _overrideList = new ListBox();
            _overrideList.Location = new Point(20, 62);
            _overrideList.Size = new Size(300, 60);
            _overrideList.DrawMode = DrawMode.OwnerDrawFixed;
            _overrideList.ItemHeight = 22;
            _overrideList.BorderStyle = BorderStyle.None;
            _overrideList.BackColor = Theme.CardAlt;
            _overrideList.ForeColor = Theme.Text;
            _overrideList.Font = Theme.FontSmall;
            _overrideList.IntegralHeight = false;
            _overrideList.DrawItem += DrawOverrideItem;
            overrideCard.Controls.Add(_overrideList);

            JellyButton overrideAdd = new JellyButton();
            overrideAdd.Text = "Для программы…";
            overrideAdd.Location = new Point(340, 62);
            overrideAdd.Size = new Size(152, 30);
            overrideAdd.Font = Theme.FontSmall;
            overrideAdd.Click += delegate { AddOverride(); };
            overrideCard.Controls.Add(overrideAdd);

            JellyButton overrideRemove = new JellyButton();
            overrideRemove.Text = "Убрать";
            overrideRemove.Location = new Point(340, 100);
            overrideRemove.Size = new Size(152, 30);
            overrideRemove.Font = Theme.FontSmall;
            overrideRemove.Click += delegate { RemoveOverride(); };
            overrideCard.Controls.Add(overrideRemove);

            CardPanel miscCard = new CardPanel();
            miscCard.Location = new Point(24, 1784);
            miscCard.Size = new Size(512, 136);
            Controls.Add(miscCard);

            Label portableTitle = MakeLabel("Настройки рядом с программой", 20, 14, Theme.FontCardTitle, Theme.Text);
            miscCard.Controls.Add(portableTitle);
            Label portableSub = MakeLabel("Файл okna.ini вместо реестра — для флешки", 20, 36, Theme.FontSmall, Theme.Muted);
            miscCard.Controls.Add(portableSub);

            _portableSwitch = new JellySwitch();
            _portableSwitch.Location = new Point(432, 18);
            miscCard.Controls.Add(_portableSwitch);

            _portableLabel = MakeLabel("", 20, 58, Theme.FontTiny, Theme.Faint);
            _portableLabel.AutoSize = false;
            _portableLabel.Size = new Size(472, 18);
            miscCard.Controls.Add(_portableLabel);

            JellyButton export = new JellyButton();
            export.Text = "Экспорт…";
            export.Location = new Point(20, 84);
            export.Size = new Size(112, 30);
            export.Font = Theme.FontSmall;
            export.Click += delegate { ExportSettings(); };
            miscCard.Controls.Add(export);

            JellyButton import = new JellyButton();
            import.Text = "Импорт…";
            import.Location = new Point(140, 84);
            import.Size = new Size(112, 30);
            import.Font = Theme.FontSmall;
            import.Click += delegate { ImportSettings(); };
            miscCard.Controls.Add(import);

            JellyButton reset = new JellyButton();
            reset.Text = "Сбросить";
            reset.Location = new Point(260, 84);
            reset.Size = new Size(112, 30);
            reset.Font = Theme.FontSmall;
            reset.Click += delegate { ResetSettings(); };
            miscCard.Controls.Add(reset);

            JellyButton diagnostics = new JellyButton();
            diagnostics.Text = "Диагностика…";
            diagnostics.Location = new Point(380, 84);
            diagnostics.Size = new Size(112, 30);
            diagnostics.Font = Theme.FontSmall;
            diagnostics.Click += delegate
            {
                Diagnostics.Show(this, _settings, _engine,
                    _tray != null ? _tray.HotkeyStatus : "неизвестно");
            };
            miscCard.Controls.Add(diagnostics);

            _hintLabel = MakeLabel("", 24, 1984, Theme.FontSmall, Theme.Faint);
            Controls.Add(_hintLabel);

            Label version = MakeLabel(Updater.CurrentLabel + "  ·  живёт в трее", 24, 2012, Theme.FontTiny, Theme.Faint);
            Controls.Add(version);

            JellyButton shake = new JellyButton();
            shake.Text = "Потрясти окно";
            shake.Location = new Point(280, 1936);
            shake.Size = new Size(150, 38);
            shake.Click += delegate { _engine.ShakeWindow(Handle); };
            Controls.Add(shake);

            JellyButton done = new JellyButton();
            done.Text = "Готово";
            done.Accent = true;
            done.Location = new Point(440, 1936);
            done.Size = new Size(96, 38);
            done.Click += delegate { Hide(); };
            Controls.Add(done);

            ToolTip tip = new ToolTip();
            tip.SetToolTip(_effectSwitch, "Включить или выключить эффект");
            tip.SetToolTip(_slider, "Насколько сильно окно отстаёт и покачивается");
            tip.SetToolTip(shake, "Покачать это окно");
            tip.SetToolTip(_wobbleSwitch, "Добавляет боковое покачивание в конце движения");
            tip.SetToolTip(_bendSwitch, "Настоящая гнутость: окно на время перетаскивания замирает и изгибается");
            tip.SetToolTip(_maxShakeSwitch, "Окно качается, когда разворачивается на весь экран, восстанавливается или возвращается из панели задач");
            tip.SetToolTip(_closeNone, "Закрывать окна без эффекта");
            tip.SetToolTip(_closeShatter, "Окно рассыпается осколками, когда закрывается кнопкой или по Alt+F4; если программа остаётся в трее, окно скатывается в трубочку и уезжает в трей");
            tip.SetToolTip(_closeBurn, "Окно прогорает снизу вверх: огонь, обугленный край, угли и дым; если программа остаётся в трее, окно скатывается в трубочку и уезжает в трей");
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
                if (_settings.Economy) _settings.Economy = false;
                UpdatePresetStyles();
                SaveSoon();
            };

            _maxShakeSwitch.CheckedChanged += delegate
            {
                _settings.MaxShake = _maxShakeSwitch.Checked;
                if (_settings.Economy) _settings.Economy = false;
                UpdatePresetStyles();
                SaveSoon();
            };

            _closeNone.Click += delegate { SetCloseEffect(0); };
            _closeShatter.Click += delegate { SetCloseEffect(1); };
            _closeBurn.Click += delegate { SetCloseEffect(2); };

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
                        "Не удалось записать okna.ini рядом с программой. " +
                        "Скопируйте Okna.exe в папку, где можно писать, или оставьте настройки в реестре.",
                        "Окна", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                _settings.Jelly = _slider.Value;
                UpdateJellyLabel();
                UpdatePresetStyles();
                _preview.Invalidate();
                SaveSoon();
            };

            for (int i = 0; i < _presets.Length; i++)
            {
                int index = i;
                _presets[i].Click += delegate
                {
                    if (index == 3)
                    {
                        _settings.ApplyEconomyPreset();
                        _settings.Save();
                        SyncFromSettings();
                        return;
                    }

                    _settings.Economy = false;
                    int value = index == 0 ? 18 : (index == 1 ? 70 : 92);
                    _slider.Value = value;
                    UpdatePresetStyles();
                };
            }
        }

        private void ApplyHotkey()
        {
            _settings.Save();
            bool ok = _tray == null || _tray.ApplyHotkey(true);
            _hotkeyButton.Text = _settings.HotkeyLabel;
            _hotkeyStatus.Text = _tray != null ? _tray.HotkeyStatus : (ok ? "работает" : "занята");
            _hotkeyStatus.ForeColor = ok ? Theme.Muted : Color.FromArgb(232, 163, 61);
            UpdateHint();
        }

        public void SyncFromSettings()
        {
            _effectSwitch.Checked = _settings.Enabled;
            _wobbleSwitch.Checked = _settings.ReleaseWobble;
            _bendSwitch.Checked = _settings.Bend;
            _maxShakeSwitch.Checked = _settings.MaxShake;
            UpdateCloseStyles();
            _rollSwitch.Checked = _settings.Roll;
            UpdateDirectionStyles();
            _startupSwitch.Checked = _settings.StartWithWindows;
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
            UpdateHint();
            _statusPill.SetStatus(_engine.PauseReason ?? _engine.Status);
        }

        private void UpdateHint()
        {
            if (_hintLabel == null) return;
            _hintLabel.Text = "Shift — обычное перетаскивание  ·  " +
                _settings.HotkeyLabel + " — пауза";
        }

        private void UpdatePortableLabel()
        {
            if (_portableLabel == null) return;
            _portableLabel.Text = _settings.IsPortable && !string.IsNullOrEmpty(_settings.PortablePath)
                ? "Файл: " + _settings.PortablePath
                : "Сейчас настройки хранятся в реестре (HKCU\\Software\\Okna)";
        }

        private void UpdateJellyLabel()
        {
            _valueLabel.Text = _settings.JellyLabel;
        }

        /// <summary>
        /// 0 = no effect, 1 = shards, 2 = fire. The two effects are
        /// alternatives, never both on.
        /// </summary>
        private void SetCloseEffect(int effect)
        {
            _settings.Shatter = effect == 1;
            _settings.Burn = effect == 2;
            UpdateCloseStyles();
            SaveSoon();
        }

        private void UpdateCloseStyles()
        {
            int selected = _settings.Burn ? 2 : (_settings.Shatter ? 1 : 0);
            JellyButton[] buttons = new JellyButton[] { _closeNone, _closeShatter, _closeBurn };
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
            int selected = _settings.Economy ? 3 : (value < 42 ? 0 : (value < 82 ? 1 : 2));
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

        private void SaveSoon()
        {
            if (_saveTimer == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
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
            string[] list = _settings.CloseOverrides;
            for (int i = 0; i < list.Length; i++)
            {
                int eq = list[i].LastIndexOf('=');
                if (eq <= 0) continue;
                string name = list[i].Substring(0, eq);
                int effect;
                int.TryParse(list[i].Substring(eq + 1), out effect);
                string label = effect == 2 ? "сгорание" : (effect == 1 ? "осколки" : "без эффекта");
                _overrideList.Items.Add(name + "  —  " + label);
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
            if (!CloseOverrideDialog.ShowFor(this, _settings, "", out program, out effect)) return;
            _settings.SetCloseOverride(program, effect);
            RefreshOverrideList();
            SaveSoon();
        }

        private void RemoveOverride()
        {
            int index = _overrideList.SelectedIndex;
            if (index < 0) return;
            string[] list = _settings.CloseOverrides;
            if (index >= list.Length) return;
            int eq = list[index].LastIndexOf('=');
            if (eq <= 0) return;
            _settings.RemoveCloseOverride(list[index].Substring(0, eq));
            RefreshOverrideList();
            SaveSoon();
        }

        private void ExportSettings()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Экспорт настроек";
                dialog.Filter = "Настройки Окон (*.ini)|*.ini|Все файлы (*.*)|*.*";
                dialog.FileName = "okna.ini";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _settings.Export(dialog.FileName);
                MessageBox.Show(this, "Настройки сохранены в " + dialog.FileName,
                    "Окна", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ImportSettings()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Импорт настроек";
                dialog.Filter = "Настройки Окон (*.ini)|*.ini|Все файлы (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!_settings.Import(dialog.FileName))
                {
                    MessageBox.Show(this, "В файле нет настроек Окон.",
                        "Окна", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _settings.Save();
                SyncFromSettings();
                if (_tray != null) _tray.ApplyHotkey(true);
                MessageBox.Show(this, "Настройки загружены.", "Окна",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ResetSettings()
        {
            if (MessageBox.Show(this, "Вернуть все настройки к заводским?",
                    "Окна", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _settings.ResetAll();
            _settings.Save();
            SyncFromSettings();
            if (_tray != null) _tray.ApplyHotkey(true);
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
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ReallyExit && e.CloseReason != CloseReason.WindowsShutDown &&
                e.CloseReason != CloseReason.TaskManagerClosing)
            {
                e.Cancel = true;
                _settings.Save();
                BeginInvoke(new MethodInvoker(delegate { Hide(); }));
                return;
            }
            _settings.Save();
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
