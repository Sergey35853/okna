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
        private JellyButton _closeDemo;
        private JellySwitch _rollSwitch;
        private JellyButton[] _directionButtons;
        private JellySwitch _startupSwitch;
        private JellySwitch _autoStartElevatedSwitch;
        private JellySwitch _callSwitch;
        private JellySwitch _batterySwitch;
        private JellySwitch _motionSwitch;
        private JellySwitch _fullscreenSwitch;
        private Label _pauseStatusLabel;
        private JellyButton _timedPauseButton;
        private JellySlider _resumeDelaySlider;
        private Label _resumeDelayValueLabel;
        private JellySwitch _portableSwitch;
        private JellySwitch _autoUpdateSwitch;
        private Label _updateStatusLabel;
        private JellyButton _checkUpdateButton;
        private JellyButton _skipResetUpdateButton;
        private JellyButton _installUpdateButton;
        private JellyButton _whatsNewButton;
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
        private ListBox _callAppsList;
        private TextBox _callAppMask;
        private JellySlider _slider;
        private Label _valueLabel;
        private Label _hintLabel;
        private StatusPill _statusPill;
        private JellyButton[] _presets;
        private JellySwitch _darkThemeSwitch;
        private bool _syncing;
        private Timer _statusTimer;
        private Timer _saveTimer;

        private Panel _navPanel;
        private Panel _contentHost;
        private Panel[] _pages;
        private CategoryNavItem[] _navItems;
        private int _activePage;

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
            ClientSize = new Size(860, 600);
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
            AutoScroll = false;
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
                UpdatePauseStatusLabel();
                UpdateUpdateStatusLabel();
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
            SelectPage(0);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.EnableDarkTitleBar(Handle);
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
            int maxWidth = Math.Max(760, area.Width - 48);
            int maxHeight = Math.Max(520, area.Height - 48);
            int width = Math.Min(ClientSize.Width, maxWidth);
            int height = Math.Min(ClientSize.Height, maxHeight);
            if (width != ClientSize.Width || height != ClientSize.Height)
                ClientSize = new Size(width, height);
            LayoutChrome();
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

            Color bg1 = _settings != null && _settings.DarkTheme ? Color.FromArgb(26, 28, 32) : Theme.Bg;
            Color bg2 = _settings != null && _settings.DarkTheme ? Color.FromArgb(34, 36, 42) : Theme.BgLight;
            using (LinearGradientBrush bg = new LinearGradientBrush(rect, bg1, bg2, LinearGradientMode.Vertical))
                g.FillRectangle(bg, rect);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 5; i >= 1; i--)
            {
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(3 + i, Theme.Accent)))
                    g.FillEllipse(glow, Width - 220 - i * 12, -180 - i * 8, 360 + i * 24, 360 + i * 24);
            }
        }

        private void BuildUi()
        {
            PictureBox logo = new PictureBox();
            logo.Location = new Point(20, 14);
            logo.Size = new Size(36, 36);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Color.Transparent;
            try
            {
                using (Icon sized = new Icon(_icon, 36, 36))
                    logo.Image = sized.ToBitmap();
            }
            catch { logo.Image = _icon.ToBitmap(); }
            Controls.Add(logo);

            Controls.Add(MakeLabel("Окна", 64, 10, Theme.FontTitle, Theme.Text));
            Controls.Add(MakeLabel("Инерционные окна для Windows", 66, 40, Theme.FontSmall, Theme.Muted));

            _statusPill = new StatusPill();
            _statusPill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(_statusPill);

            _navPanel = new Panel();
            _navPanel.Location = new Point(16, 72);
            _navPanel.BackColor = Color.Transparent;
            Controls.Add(_navPanel);

            _contentHost = new Panel();
            _contentHost.Location = new Point(220, 72);
            _contentHost.BackColor = Color.Transparent;
            Controls.Add(_contentHost);

            string[] names = new string[] {
                "Эффект", "Движение", "Закрытие", "Пауза", "Исключения и правила", "Язык и тема", "Система"
            };
            string[] hints = new string[] {
                "Желейность и режимы", "Изгиб, тряска, трубочка", "Что при закрытии", "Когда уступать место", "Программы и правила", "Язык, тема, о программе", "Запуск, клавиша, обновления"
            };
            _navItems = new CategoryNavItem[names.Length];
            _pages = new Panel[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                CategoryNavItem nav = new CategoryNavItem();
                nav.Title = names[i];
                nav.Hint = hints[i];
                nav.Text = names[i];
                nav.Location = new Point(0, i * 54);
                nav.Size = new Size(192, 48);
                nav.Click += delegate { SelectPage(index); };
                nav.Cursor = Cursors.Hand;
                _navPanel.Controls.Add(nav);
                _navItems[i] = nav;

                Panel page = new Panel();
                page.Location = new Point(0, 0);
                page.BackColor = Color.Transparent;
                page.Visible = false;
                page.AutoScroll = true;
                _contentHost.Controls.Add(page);
                _pages[i] = page;
            }

            LayoutChrome();
            BuildEffectPage(_pages[0]);
            BuildMotionPage(_pages[1]);
            BuildClosePage(_pages[2]);
            BuildPausePage(_pages[3]);
            BuildExcludePage(_pages[4]);
            BuildAppearancePage(_pages[5]);
            BuildSystemPage(_pages[6]);
            LayoutPages();

            _hintLabel = MakeLabel("", 20, 0, Theme.FontSmall, Theme.Faint);
            _hintLabel.AutoSize = false;
            _hintLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_hintLabel);

            _versionLabel = MakeLabel("", 20, 0, Theme.FontTiny, Theme.Faint);
            _versionLabel.AutoSize = false;
            _versionLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            UpdateVersionLabel();
            Controls.Add(_versionLabel);

            LinkLabel telegramLink = new LinkLabel();
            telegramLink.Text = "Обратная связь: @Link_Ser";
            telegramLink.AutoSize = true;
            telegramLink.Font = Theme.FontTiny;
            telegramLink.LinkColor = Theme.Accent;
            telegramLink.ActiveLinkColor = Theme.Accent;
            telegramLink.LinkBehavior = LinkBehavior.HoverUnderline;
            telegramLink.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            telegramLink.Click += delegate
            {
                try { System.Diagnostics.Process.Start("https://t.me/Link_Ser"); } catch { }
            };
            telegramLink.Name = "telegramLink";
            Controls.Add(telegramLink);

            JellyButton done = new JellyButton();
            done.Text = "Готово";
            done.Accent = true;
            done.Size = new Size(110, 36);
            done.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            done.Click += delegate { Hide(); };
            done.Name = "doneButton";
            Controls.Add(done);

            LayoutChrome();

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
        }

        private void LayoutChrome()
        {
            int footerH = 62;
            int top = 72;
            int navW = 200;
            int gap = 14;
            _statusPill.Location = new Point(ClientSize.Width - 20 - _statusPill.Width, 20);
            _navPanel.SetBounds(16, top, navW, ClientSize.Height - top - footerH - 8);
            _contentHost.SetBounds(16 + navW + gap, top, ClientSize.Width - 16 - navW - gap - 16, ClientSize.Height - top - footerH - 8);
            if (_pages != null)
            {
                for (int i = 0; i < _pages.Length; i++)
                    _pages[i].Size = _contentHost.ClientSize;
            }
            LayoutPages();

            Control done = Controls["doneButton"];
            Control link = Controls["telegramLink"];
            int fy = ClientSize.Height - footerH + 10;
            if (_hintLabel != null) _hintLabel.SetBounds(20, fy, Math.Max(120, ClientSize.Width - 170), 16);
            if (_versionLabel != null) _versionLabel.SetBounds(20, fy + 22, 360, 14);
            if (link != null) link.Location = new Point(380, fy + 20);
            if (done != null) done.Location = new Point(ClientSize.Width - done.Width - 16, fy + 8);
        }

        private void LayoutPages()
        {
            if (_pages == null || _contentHost == null) return;
            int pageW = Math.Max(300, _contentHost.ClientSize.Width - 8);
            for (int p = 0; p < _pages.Length; p++)
            {
                Panel page = _pages[p];
                if (page == null) continue;
                foreach (Control c in page.Controls)
                {
                    c.Width = pageW;
                    if (c is CardPanel) RelayoutCard((CardPanel)c);
                }
            }
        }

        private void RelayoutCard(CardPanel card)
        {
            int pad = 20;
            int rightPad = 20;
            foreach (Control c in card.Controls)
            {
                if (c is JellySwitch)
                {
                    c.Left = Math.Max(pad, card.ClientSize.Width - c.Width - rightPad);
                }
                else if (c == _slider)
                {
                    c.Width = Math.Max(100, card.ClientSize.Width - pad - rightPad);
                }
                else if (c == _valueLabel)
                {
                    c.Left = Math.Max(pad, card.ClientSize.Width - c.Width - rightPad);
                }
                else if (c is Panel && c.Height <= 2) // divider
                {
                    c.Width = Math.Max(20, card.ClientSize.Width - pad - rightPad);
                }
                else if (c == _excludeList || c == _overrideList)
                {
                    c.Width = Math.Max(120, card.ClientSize.Width - pad - 140);
                }
                else if (c == _excludeMask || c == _callAppMask)
                {
                    c.Width = Math.Max(120, card.ClientSize.Width - pad - 140);
                }
                else if (c is Label && c.Name == "badge")
                {
                    c.Left = Math.Max(pad, card.ClientSize.Width - ((Label)c).PreferredWidth - rightPad);
                }
                else if (c is Label && c.AutoSize == false && c.Font != Theme.FontTiny && c != _pauseStatusLabel && c != _valueLabel && c != _hotkeyStatus && c != _portableLabel && c != _hintLabel && c != _versionLabel)
                {
                    // constrain text labels used in rows so they don't overlap switches/badges
                    if (c.Left == pad && c.Top >= 0)
                    {
                        int reserve = card.Controls.Contains(_fullscreenSwitch) || card.Controls.Contains(_callSwitch) || card.Controls.Contains(_startupSwitch) ? 74 : (c.Parent != null && HasBadge(card) ? 80 : 0);
                        if (reserve > 0) c.Width = Math.Max(60, card.ClientSize.Width - pad - reserve);
                    }
                }
            }
            // Reflow preset grid
            if (_presets != null && _presets.Length == 6 && card.Controls.Contains(_presets[0]))
            {
                int bw = (card.ClientSize.Width - 40 - 12) / 3;
                for (int i = 0; i < 6; i++)
                {
                    _presets[i].Size = new Size(bw, 30);
                    _presets[i].Location = new Point(20 + (i % 3) * (bw + 6), 14 + (i / 3) * 38);
                }
                card.Height = 14 + 38 * 2 + 8;
            }
            // Direction buttons
            if (_directionButtons != null && _directionButtons.Length == 5 && card.Controls.Contains(_directionButtons[0]))
            {
                int dw = (card.ClientSize.Width - 40 - 16) / 5;
                for (int i = 0; i < 5; i++)
                {
                    _directionButtons[i].Size = new Size(dw, 30);
                    _directionButtons[i].Location = new Point(20 + i * (dw + 4), 40);
                }
            }
            // Close effect buttons: 2x2 for readability
            if (_closeNone != null && card.Controls.Contains(_closeNone))
            {
                int bw = (card.ClientSize.Width - 40 - 10) / 2;
                _closeNone.SetBounds(20, 42, bw, 34);
                _closeShatter.SetBounds(20 + bw + 10, 42, bw, 34);
                _closeBurn.SetBounds(20, 84, bw, 34);
                _closeFade.SetBounds(20 + bw + 10, 84, bw, 34);
                if (_closeDemo != null)
                {
                    _closeDemo.SetBounds(20, 126, Math.Min(180, card.ClientSize.Width - 40), 30);
                }
                int ly = 164;
                foreach (Control c in card.Controls)
                {
                    if (c is Label && c.Font == Theme.FontTiny && c.Name != "badge")
                    { c.AutoSize = false; c.SetBounds(20, ly, card.ClientSize.Width - 40, 16); ly += 18; }
                }
                card.Height = Math.Max(190, ly + 8);
            }
            // Exclude page buttons
            if (_excludeList != null && card.Controls.Contains(_excludeList))
            {
                int bx = card.ClientSize.Width - 128;
                foreach (Control c in card.Controls)
                {
                    if (c is JellyButton)
                    {
                        if (c.Text == "Добавить файл…" || Loc.T(c.AccessibleDescription ?? c.Text) == "Add file…" || c.Left > 200)
                        {
                            // handled below by text matching is fragile; use position heuristic
                        }
                        c.Left = Math.Max(20, bx);
                        c.Width = 108;
                    }
                }
                // fix specific buttons by reference search not easy; re-find
                int ry = 40;
                foreach (Control c in card.Controls)
                {
                    if (c is JellyButton && c.Top >= 38 && c.Top <= 110 && c.Height == 28) { c.Location = new Point(bx, ry); ry += 32; }
                    if (c is JellyButton && c.Top >= 180 && c.Height == 28) { c.Location = new Point(bx, 186); }
                }
            }
            if (_overrideList != null && card.Controls.Contains(_overrideList))
            {
                int bx = card.ClientSize.Width - 128;
                foreach (Control c in card.Controls)
                {
                    if (c is JellyButton && c.Height == 28)
                    {
                        if (c.Top < 60) c.Location = new Point(bx, 40);
                        else c.Location = new Point(bx, 72);
                        c.Width = 108;
                    }
                }
            }
            if (_callAppsList != null && card.Controls.Contains(_callAppsList))
            {
                int bx = card.ClientSize.Width - 128;
                _callAppsList.Width = Math.Max(120, card.ClientSize.Width - 20 - 140);
                foreach (Control c in card.Controls)
                {
                    if (!(c is JellyButton)) continue;
                    if (c.Name == "callAppAddFile") c.SetBounds(bx, 40, 108, 28);
                    else if (c.Name == "callAppRemove") c.SetBounds(bx, 72, 108, 28);
                    else if (c.Name == "callAppAddMask") c.SetBounds(bx, 126, 108, 28);
                }
                if (_callAppMask != null) _callAppMask.Width = Math.Max(120, card.ClientSize.Width - 20 - 140);
            }
            // System data buttons row
            if (_portableSwitch != null && card.Controls.Contains(_portableSwitch))
            {
                int bx = 20;
                foreach (Control c in card.Controls)
                {
                    if (c is JellyButton && c.Top >= 78)
                    {
                        c.Location = new Point(bx, 84);
                        bx += c.Width + 8;
                    }
                }
            }
            // Language buttons
            if (_languageAuto != null && card.Controls.Contains(_languageAuto))
            {
                _languageAuto.Location = new Point(110, 12);
                _languageRu.Location = new Point(214, 12);
                _languageEn.Location = new Point(318, 12);
                _darkThemeSwitch.Left = Math.Max(110, card.ClientSize.Width - _darkThemeSwitch.Width - 20);
            }
            if (_hotkeyButton != null && card.Controls.Contains(_hotkeyButton))
            {
                _hotkeyStatus.Width = Math.Max(80, card.ClientSize.Width - _hotkeyButton.Right - 28);
            }
            if (_pauseStatusLabel != null && card.Controls.Contains(_pauseStatusLabel))
            {
                if (_timedPauseButton != null)
                {
                    _timedPauseButton.Left = Math.Max(20, card.ClientSize.Width - _timedPauseButton.Width - 20);
                    _pauseStatusLabel.Width = Math.Max(80, _timedPauseButton.Left - 28);
                }
                else
                {
                    _pauseStatusLabel.Width = Math.Max(80, card.ClientSize.Width - 36);
                }
            }
            if (_resumeDelaySlider != null && card.Controls.Contains(_resumeDelaySlider))
            {
                _resumeDelaySlider.Width = Math.Max(100, card.ClientSize.Width - 40);
                if (_resumeDelayValueLabel != null)
                    _resumeDelayValueLabel.Left = Math.Max(20, card.ClientSize.Width - _resumeDelayValueLabel.Width - 20);
                foreach (Control c in card.Controls)
                    if (c.Name == "resumeDelayHint" && c is Label) { c.AutoSize = false; c.Width = Math.Max(80, card.ClientSize.Width - 40); }
            }
            if (_updateStatusLabel != null && card.Controls.Contains(_updateStatusLabel))
            {
                if (_checkUpdateButton != null)
                {
                    _checkUpdateButton.Left = Math.Max(20, card.ClientSize.Width - _checkUpdateButton.Width - 20);
                    _updateStatusLabel.Width = Math.Max(80, _checkUpdateButton.Left - 28);
                }
                else
                {
                    _updateStatusLabel.Width = Math.Max(80, card.ClientSize.Width - 36);
                }
                // Keep skip/install buttons on their row if present
                if (_skipResetUpdateButton != null) _skipResetUpdateButton.Top = Math.Max(_skipResetUpdateButton.Top, 96);
                if (_installUpdateButton != null)
                {
                    _installUpdateButton.Top = _skipResetUpdateButton != null ? _skipResetUpdateButton.Top : Math.Max(_installUpdateButton.Top, 96);
                    _installUpdateButton.Left = _skipResetUpdateButton != null ? _skipResetUpdateButton.Right + 8 : 20;
                }
                if (_whatsNewButton != null)
                {
                    _whatsNewButton.Top = _installUpdateButton != null ? _installUpdateButton.Top : Math.Max(_whatsNewButton.Top, 96);
                    _whatsNewButton.Left = _installUpdateButton != null ? _installUpdateButton.Right + 8 : (_skipResetUpdateButton != null ? _skipResetUpdateButton.Right + 8 : 20);
                }
            }
        }

        private static bool HasBadge(Control parent)
        {
            foreach (Control c in parent.Controls) if (c.Name == "badge") return true;
            return false;
        }

        private void SelectPage(int index)
        {
            if (index < 0 || index >= _pages.Length) return;
            _activePage = index;
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = (i == index);
                _navItems[i].Active = (i == index);
                _navItems[i].Invalidate();
            }
            _pages[index].BringToFront();
            _pages[index].AutoScrollPosition = Point.Empty;
        }

        private void BuildEffectPage(Panel page)
        {
            int y = 0;
            CardPanel card = AddCard(page, "Основные параметры", 0, y, _contentHost.Width - 8, 148); y += 156;
            AddLabel(card, "Желеобразные окна", 18, 14, Theme.FontCardTitle, Theme.Text);
            AddLabel(card, "Перетаскивание за заголовок", 18, 36, Theme.FontSmall, Theme.Muted);
            _effectSwitch = new JellySwitch(); _effectSwitch.Location = new Point(card.Width - 68, 18); card.Controls.Add(_effectSwitch);
            AddDivider(card, 18, 60, card.Width - 36);
            AddLabel(card, "Желейность", 18, 72, Theme.FontLabel, Theme.Text);
            _valueLabel = MakeLabel("", card.Width - 150, 72, Theme.FontSmall, Theme.Accent);
            _valueLabel.AutoSize = false; _valueLabel.Size = new Size(132, 16); _valueLabel.TextAlign = ContentAlignment.MiddleRight; card.Controls.Add(_valueLabel);
            _slider = new JellySlider(); _slider.Minimum = 0; _slider.Maximum = 200;
            _slider.Location = new Point(18, 94); _slider.Size = new Size(card.Width - 36, 24); card.Controls.Add(_slider);

            CardPanel presetsCard = AddCard(page, "Режим", 0, y, _contentHost.Width - 8, 96);
            _presets = new JellyButton[6];
            string[] names = new string[] { "Спокойно", "Баланс", "Желе", "Экономно", "Яркий", "Минимальный" };
            int bw = (presetsCard.Width - 36 - 10) / 3;
            for (int i = 0; i < 6; i++)
            {
                JellyButton b = new JellyButton();
                b.Text = names[i];
                b.Size = new Size(bw, 28);
                b.Location = new Point(18 + (i % 3) * (bw + 5), 12 + (i / 3) * 36);
                b.Font = Theme.FontSmall;
                b.Tag = i;
                _presets[i] = b;
                presetsCard.Controls.Add(b);
            }
        }

        private void BuildMotionPage(Panel page)
        {
            int y = 0;
            CardPanel card = AddCard(page, "Поведение окон", 0, y, _contentHost.Width - 8, 258); y += 266;
            int ry = 14;
            ry = AddSwitchRow(card, "Покачивание при отпускании", "Боковое качание после движения", ry, out _wobbleSwitch);
            AddDivider(card, 18, ry, card.Width - 36); ry += 10;
            ry = AddSwitchRow(card, "Гнуть окна по инерции", "Края изгибаются при разгоне и торможении", ry, out _bendSwitch);
            AddDivider(card, 18, ry, card.Width - 36); ry += 10;
            ry = AddSwitchRow(card, "Тряска при разворачивании", "Качается при развороте, восстановлении", ry, out _maxShakeSwitch);
            AddDivider(card, 18, ry, card.Width - 36); ry += 10;
            AddSwitchRow(card, "Сворачивать в трубочку", "Окно скатывается в панель задач", ry, out _rollSwitch);

            CardPanel dirCard = AddCard(page, "Куда скатывать", 0, y, _contentHost.Width - 8, 104);
            AddLabel(dirCard, "Авто — к панели задач", 18, 12, Theme.FontSmall, Theme.Muted);
            _directionButtons = new JellyButton[5];
            string[] directionNames = new string[] { "Авто", "Вниз", "Вверх", "Влево", "Вправо" };
            int dw = (dirCard.Width - 36 - 16) / 5;
            for (int i = 0; i < 5; i++)
            {
                JellyButton b = new JellyButton();
                b.Text = directionNames[i];
                b.Location = new Point(18 + i * (dw + 4), 38);
                b.Size = new Size(dw, 28);
                b.Font = Theme.FontSmall;
                b.Tag = (RollDirection)i;
                _directionButtons[i] = b;
                dirCard.Controls.Add(b);
            }
        }

        private void BuildClosePage(Panel page)
        {
            CardPanel card = AddCard(page, "Эффект при закрытии", 0, 0, _contentHost.Width - 8, 190);
            AddLabel(card, "Если программа остаётся в трее — скатывается туда", 18, 14, Theme.FontSmall, Theme.Muted);
            int bw = (card.Width - 36 - 12) / 4;
            _closeNone = MakeChoice(card, "Без эффекта", 18, 44, bw, 30);
            _closeShatter = MakeChoice(card, "Осколки", 18 + bw + 4, 44, bw, 30);
            _closeBurn = MakeChoice(card, "Сгорание", 18 + (bw + 4) * 2, 44, bw, 30);
            _closeFade = MakeChoice(card, "Затухание", 18 + (bw + 4) * 3, 44, bw, 30);
            _closeDemo = MakeSmallButton(card, "Показать эффект", 18, 126, 150);
            _closeDemo.Name = "closeDemoButton";
            _closeDemo.Click += delegate { DemoCloseEffect(); };
            Label h1 = AddLabel(card, "Сгорание на больших окнах автоматически упрощается, чтобы не было фризов.", 18, 162, Theme.FontTiny, Theme.Faint);
            h1.AutoSize = false; h1.Name = "closeHint1"; h1.Size = new Size(Math.Max(100, card.Width - 36), 14);
            Label h2 = AddLabel(card, "Если приложение прячется в трей — вместо закрытия окно скатывается в трубочку.", 18, 178, Theme.FontTiny, Theme.Faint);
            h2.AutoSize = false; h2.Name = "closeHint2"; h2.Size = new Size(Math.Max(100, card.Width - 36), 14);
        }

        private void BuildPausePage(Panel page)
        {
            int y = 0;

            CardPanel statusCard = AddCard(page, "Текущее состояние", 0, y, _contentHost.Width - 8, 96); y += 104;
            _pauseStatusLabel = MakeLabel("", 18, 16, Theme.FontLabel, Theme.Text);
            _pauseStatusLabel.AutoSize = false;
            _pauseStatusLabel.Size = new Size(statusCard.Width - 180, 20);
            statusCard.Controls.Add(_pauseStatusLabel);
            _timedPauseButton = MakeSmallButton(statusCard, "Пауза на 30 мин", statusCard.Width - 148, 12, 130);
            _timedPauseButton.Name = "timedPauseButton";
            _timedPauseButton.Click += delegate
            {
                if (_tray != null) _tray.ToggleTimedPause();
                UpdatePauseStatusLabel();
            };
            Label ph1 = AddLabel(statusCard, "Ручная пауза на 30 минут (трей/горячая клавиша). Автопауза действует отдельно.", 18, 42, Theme.FontTiny, Theme.Faint);
            ph1.AutoSize = false; ph1.Width = Math.Max(100, statusCard.Width - 36); ph1.Height = 14;
            Label ph2 = AddLabel(statusCard, "Если нужно отключить эффекты для одной программы — «Исключения и правила».", 18, 58, Theme.FontTiny, Theme.Faint);
            ph2.AutoSize = false; ph2.Width = Math.Max(100, statusCard.Width - 36); ph2.Height = 14;

            CardPanel cfgCard = AddCard(page, "Настраиваемые условия", 0, y, _contentHost.Width - 8, 282); y += 290;
            AddLabel(cfgCard, "Эффекты уступают место, когда вы заняты", 18, 14, Theme.FontSmall, Theme.Muted);
            int ry = 42;
            ry = AddSwitchRow(cfgCard, "Полноэкранные приложения", "Игры и видео на весь экран", ry, out _fullscreenSwitch);
            AddDivider(cfgCard, 18, ry, cfgCard.Width - 36); ry += 10;
            ry = AddSwitchRow(cfgCard, "Звонки и показ экрана", "Teams, Zoom, Discord, Slack и доп. список", ry, out _callSwitch);
            AddDivider(cfgCard, 18, ry, cfgCard.Width - 36); ry += 10;
            ry = AddSwitchRow(cfgCard, "Питание от батареи", "Не тратить заряд на эффекты", ry, out _batterySwitch);
            AddDivider(cfgCard, 18, ry, cfgCard.Width - 36); ry += 10;
            ry = AddSwitchRow(cfgCard, "Уважать уменьшение движения", "Если анимации выключены в Windows", ry, out _motionSwitch);
            AddDivider(cfgCard, 18, ry, cfgCard.Width - 36); ry += 8;
            AddLabel(cfgCard, "Задержка возврата после автопаузы", 18, ry, Theme.FontCardTitle, Theme.Text);
            _resumeDelayValueLabel = MakeLabel("", cfgCard.Width - 70, ry, Theme.FontSmall, Theme.Accent);
            _resumeDelayValueLabel.AutoSize = false; _resumeDelayValueLabel.Name = "resumeDelayValue"; _resumeDelayValueLabel.Size = new Size(52, 16); _resumeDelayValueLabel.TextAlign = ContentAlignment.MiddleRight;
            cfgCard.Controls.Add(_resumeDelayValueLabel);
            _resumeDelaySlider = new JellySlider();
            _resumeDelaySlider.Minimum = 0; _resumeDelaySlider.Maximum = 30;
            _resumeDelaySlider.Location = new Point(18, ry + 22); _resumeDelaySlider.Size = new Size(cfgCard.Width - 36, 22);
            _resumeDelaySlider.Name = "resumeDelaySlider";
            cfgCard.Controls.Add(_resumeDelaySlider);
            Label rdHint = AddLabel(cfgCard, "0–30 сек: защищает от мигания при Alt-Tab/переключении", 18, ry + 46, Theme.FontTiny, Theme.Faint);
            rdHint.AutoSize = false; rdHint.Name = "resumeDelayHint"; rdHint.Width = Math.Max(100, cfgCard.Width - 36); rdHint.Height = 14;

            CardPanel alwaysCard = AddCard(page, "Всегда активные условия", 0, y, _contentHost.Width - 8, 112); y += 120;
            AddInfoRow(alwaysCard, "Удалённый рабочий стол", "Пауза при сеансе RDP", "Всегда", 14);
            AddDivider(alwaysCard, 18, 58, alwaysCard.Width - 36);
            AddInfoRow(alwaysCard, "Режим экономии энергии Windows", "Системный режим экономии заряда", "Всегда", 68);

            CardPanel callCard = AddCard(page, "Доп. приложения для звонков/экрана", 0, y, _contentHost.Width - 8, 190);
            AddLabel(callCard, "Добавляются к встроенному списку (Teams, Zoom, Discord…)", 18, 14, Theme.FontSmall, Theme.Muted);
            _callAppsList = new ListBox();
            _callAppsList.Location = new Point(18, 40);
            _callAppsList.Size = new Size(callCard.Width - 160, 80);
            _callAppsList.DrawMode = DrawMode.OwnerDrawFixed;
            _callAppsList.ItemHeight = 20;
            _callAppsList.BorderStyle = BorderStyle.None;
            _callAppsList.BackColor = Theme.CardAlt;
            _callAppsList.ForeColor = Theme.Text;
            _callAppsList.Font = Theme.FontSmall;
            _callAppsList.IntegralHeight = false;
            _callAppsList.Name = "callAppsList";
            _callAppsList.DrawItem += DrawCallAppItem;
            callCard.Controls.Add(_callAppsList);

            int cbx = callCard.Width - 132;
            JellyButton callAdd = MakeSmallButton(callCard, "Добавить файл…", cbx, 40, 114);
            callAdd.Name = "callAppAddFile";
            callAdd.Click += delegate { AddCallAppFile(); };
            JellyButton callRemove = MakeSmallButton(callCard, "Убрать", cbx, 72, 114);
            callRemove.Name = "callAppRemove";
            callRemove.Click += delegate { RemoveSelectedCallApp(); };

            _callAppMask = new TextBox();
            _callAppMask.Location = new Point(18, 128);
            _callAppMask.Size = new Size(callCard.Width - 160, 26);
            _callAppMask.BorderStyle = BorderStyle.FixedSingle;
            _callAppMask.BackColor = Theme.CardAlt;
            _callAppMask.ForeColor = Theme.Text;
            _callAppMask.Font = Theme.FontSmall;
            _callAppMask.Name = "callAppMask";
            callCard.Controls.Add(_callAppMask);
            JellyButton callMask = MakeSmallButton(callCard, "Добавить", cbx, 126, 114);
            callMask.Name = "callAppAddMask";
            callMask.Click += delegate { AddCallAppMask(); };
        }

        private static void AddInfoRow(Control parent, string title, string sub, string badge, int y)
        {
            int textW = Math.Max(80, parent.ClientSize.Width - 18 - 80);
            Label t = AddLabel(parent, title, 18, y, Theme.FontCardTitle, Theme.Text);
            t.AutoSize = false; t.Size = new Size(textW, 20);
            Label s = AddLabel(parent, sub, 18, y + 20, Theme.FontSmall, Theme.Muted);
            s.AutoSize = false; s.Size = new Size(textW, 16);
            Label b = MakeLabel(badge, 0, y + 8, Theme.FontSmall, Theme.Accent);
            b.AutoSize = true;
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            parent.Controls.Add(b);
            b.Location = new Point(Math.Max(18, parent.ClientSize.Width - b.PreferredWidth - 20), y + 10);
            b.Name = "badge";
        }

        private void UpdatePauseStatusLabel()
        {
            if (_pauseStatusLabel == null || _pauseStatusLabel.IsDisposed) return;
            string reason = _engine == null ? null : _engine.PauseReason;
            if (_tray != null && _tray.TimedPauseActive)
            {
                _pauseStatusLabel.Text = Loc.T("Ручная пауза: ещё ") + _tray.TimedPauseMinutesLeft + Loc.T(" мин");
                _pauseStatusLabel.ForeColor = Theme.Accent;
            }
            else if (reason != null)
            {
                _pauseStatusLabel.Text = Loc.T("Автопауза: ") + Loc.T(reason);
                _pauseStatusLabel.ForeColor = Theme.Warm;
            }
            else if (_settings != null && !_settings.Enabled)
            {
                _pauseStatusLabel.Text = Loc.T("Окна — выключены");
                _pauseStatusLabel.ForeColor = Theme.Muted;
            }
            else
            {
                _pauseStatusLabel.Text = Loc.T("Эффекты работают");
                _pauseStatusLabel.ForeColor = Theme.Accent;
            }
            if (_timedPauseButton != null)
                _timedPauseButton.Text = (_tray != null && _tray.TimedPauseActive) ? Loc.T("Возобновить") : Loc.T("Пауза на 30 мин");
        }

        private void UpdateResumeDelayLabel()
        {
            if (_resumeDelayValueLabel == null) return;
            int s = _settings == null ? 0 : _settings.AutoPauseResumeDelaySeconds;
            _resumeDelayValueLabel.Text = s <= 0 ? Loc.T("выкл") : (s + Loc.T(" сек"));
        }

        private void UpdateUpdateStatusLabel()
        {
            if (_updateStatusLabel == null || _updateStatusLabel.IsDisposed) return;
            string status = Loc.T("Текущая версия: ") + Updater.CurrentLabel;
            bool checking = _tray != null && _tray.IsCheckingUpdates;
            bool ready = _tray != null && _tray.ReadyUpdate != null && _tray.ReadyUpdate.Version != null;
            bool skipped = _settings != null && !string.IsNullOrEmpty(_settings.SkippedVersion);
            if (checking)
            {
                status += "  ·  " + Loc.T("проверка…");
                _updateStatusLabel.ForeColor = Theme.Muted;
            }
            else if (ready)
            {
                status += "  ·  " + Loc.T("доступна v") + Updater.Format(_tray.ReadyUpdate.Version);
                _updateStatusLabel.ForeColor = Theme.Accent;
            }
            else if (skipped)
            {
                status += "  ·  " + Loc.T("пропущена v") + _settings.SkippedVersion;
                _updateStatusLabel.ForeColor = Theme.Faint;
            }
            else
            {
                _updateStatusLabel.ForeColor = Theme.Muted;
            }
            _updateStatusLabel.Text = status;
            if (_checkUpdateButton != null) _checkUpdateButton.Enabled = !checking;
            if (_skipResetUpdateButton != null) _skipResetUpdateButton.Enabled = skipped && !checking;
            if (_installUpdateButton != null) _installUpdateButton.Enabled = ready && !checking;
            if (_whatsNewButton != null) _whatsNewButton.Enabled = ready && !checking;
        }

        private void BuildExcludePage(Panel page)
        {
            int y = 0;
            CardPanel exCard = AddCard(page, "Исключения", 0, y, _contentHost.Width - 8, 238); y += 246;
            AddLabel(exCard, "Окна остаются обычными: программы, маски, классы", 18, 14, Theme.FontSmall, Theme.Muted);
            _excludeList = new ListBox();
            _excludeList.Location = new Point(18, 40);
            _excludeList.Size = new Size(exCard.Width - 160, 116);
            _excludeList.DrawMode = DrawMode.OwnerDrawFixed;
            _excludeList.ItemHeight = 20;
            _excludeList.BorderStyle = BorderStyle.None;
            _excludeList.BackColor = Theme.CardAlt;
            _excludeList.ForeColor = Theme.Text;
            _excludeList.Font = Theme.FontSmall;
            _excludeList.IntegralHeight = false;
            _excludeList.DrawItem += DrawExcludeItem;
            exCard.Controls.Add(_excludeList);

            int bx = exCard.Width - 132;
            JellyButton excludeAdd = MakeSmallButton(exCard, "Добавить файл…", bx, 40, 114);
            excludeAdd.Click += delegate { AddExclusions(); };
            JellyButton excludeRunning = MakeSmallButton(exCard, "Из запущенных…", bx, 72, 114);
            excludeRunning.Click += delegate { PickRunning(); };
            ToolTip tip = new ToolTip(); tip.SetToolTip(excludeRunning, "Выбрать из окон, которые открыты прямо сейчас");
            JellyButton excludeRemove = MakeSmallButton(exCard, "Убрать", bx, 104, 114);
            excludeRemove.Click += delegate { RemoveSelectedExclusion(); };

            AddLabel(exCard, "Маска: steam* или class:Progman", 18, 164, Theme.FontSmall, Theme.Muted);
            _excludeMask = new TextBox();
            _excludeMask.Location = new Point(18, 186);
            _excludeMask.Size = new Size(exCard.Width - 160, 26);
            _excludeMask.BorderStyle = BorderStyle.FixedSingle;
            _excludeMask.BackColor = Theme.CardAlt;
            _excludeMask.ForeColor = Theme.Text;
            _excludeMask.Font = Theme.FontSmall;
            exCard.Controls.Add(_excludeMask);
            JellyButton addMask = MakeSmallButton(exCard, "Добавить маску", bx, 184, 114);
            addMask.Click += delegate { AddMask(); };

            CardPanel ovCard = AddCard(page, "Свои правила для программы", 0, y, _contentHost.Width - 8, 140);
            AddLabel(ovCard, "Эффект закрытия, изгиб, тряска, скатывание", 18, 14, Theme.FontSmall, Theme.Muted);
            _overrideList = new ListBox();
            _overrideList.Location = new Point(18, 40);
            _overrideList.Size = new Size(ovCard.Width - 160, 80);
            _overrideList.DrawMode = DrawMode.OwnerDrawFixed;
            _overrideList.ItemHeight = 20;
            _overrideList.BorderStyle = BorderStyle.None;
            _overrideList.BackColor = Theme.CardAlt;
            _overrideList.ForeColor = Theme.Text;
            _overrideList.Font = Theme.FontSmall;
            _overrideList.IntegralHeight = false;
            _overrideList.DrawItem += DrawOverrideItem;
            ovCard.Controls.Add(_overrideList);
            JellyButton overrideAdd = MakeSmallButton(ovCard, "Для программы…", ovCard.Width - 132, 40, 114);
            overrideAdd.Click += delegate { AddOverride(); };
            JellyButton overrideRemove = MakeSmallButton(ovCard, "Убрать", ovCard.Width - 132, 72, 114);
            overrideRemove.Click += delegate { RemoveOverride(); };
        }

        private void BuildSystemPage(Panel page)
        {
            int y = 0;
            CardPanel startCard = AddCard(page, "Запуск", 0, y, _contentHost.Width - 8, 106); y += 114;
            int ry = 14;
            ry = AddSwitchRow(startCard, "Запускать с Windows", "Тихо стартовать с системой", ry, out _startupSwitch);
            AddDivider(startCard, 18, ry, startCard.Width - 36); ry += 10;
            AddSwitchRow(startCard, "Запуск от имени администратора", "С правами администратора при входе", ry, out _autoStartElevatedSwitch);

            CardPanel hkCard = AddCard(page, "Горячая клавиша", 0, y, _contentHost.Width - 8, 110); y += 118;
            AddLabel(hkCard, "Нажмите поле и введите сочетание; Backspace — выключить", 18, 14, Theme.FontSmall, Theme.Muted);
            _hotkeyButton = new JellyButton();
            _hotkeyButton.Text = "Ctrl+Alt+J";
            _hotkeyButton.Location = new Point(18, 42);
            _hotkeyButton.Size = new Size(180, 32);
            _hotkeyButton.Font = Theme.FontBody;
            _hotkeyButton.Click += delegate { _hotkeyButton.Focus(); };
            hkCard.Controls.Add(_hotkeyButton);
            _hotkeyStatus = MakeLabel("", 208, 50, Theme.FontSmall, Theme.Muted);
            _hotkeyStatus.Size = new Size(hkCard.Width - 226, 18);
            hkCard.Controls.Add(_hotkeyStatus);

            CardPanel updCard = AddCard(page, "Обновления", 0, y, _contentHost.Width - 8, 168); y += 176;
            ry = AddSwitchRow(updCard, "Проверять обновления автоматически", "Фоновая проверка через GitHub Releases", 14, out _autoUpdateSwitch);
            AddDivider(updCard, 18, ry, updCard.Width - 36);
            _updateStatusLabel = MakeLabel("", 18, ry + 10, Theme.FontSmall, Theme.Muted);
            _updateStatusLabel.AutoSize = false;
            _updateStatusLabel.Name = "updateStatusLabel";
            _updateStatusLabel.Size = new Size(updCard.Width - 170, 18);
            updCard.Controls.Add(_updateStatusLabel);
            _checkUpdateButton = MakeSmallButton(updCard, "Проверить сейчас", updCard.Width - 138, ry + 6, 120);
            _checkUpdateButton.Name = "checkUpdateButton";
            _checkUpdateButton.Click += delegate
            {
                if (_tray != null) _tray.CheckUpdates(true);
                UpdateUpdateStatusLabel();
            };
            _skipResetUpdateButton = MakeSmallButton(updCard, "Сбросить пропуск", 18, ry + 38, 130);
            _skipResetUpdateButton.Name = "skipResetUpdateButton";
            _skipResetUpdateButton.Click += delegate
            {
                if (_tray != null) _tray.ClearSkippedUpdateUi();
                else { _settings.SkippedVersion = ""; SaveSoon(); }
                UpdateUpdateStatusLabel();
            };
            _installUpdateButton = MakeSmallButton(updCard, "Установить", 154, ry + 38, 110);
            _installUpdateButton.Name = "installUpdateButton";
            _installUpdateButton.Click += delegate
            {
                if (_tray != null && _tray.ReadyUpdate != null) _tray.ApplyUpdateNow();
                UpdateUpdateStatusLabel();
            };
            _whatsNewButton = MakeSmallButton(updCard, "Что нового…", 270, ry + 38, 110);
            _whatsNewButton.Name = "whatsNewButton";
            _whatsNewButton.Click += delegate
            {
                if (_tray != null) _tray.ShowUpdateDialog();
                UpdateUpdateStatusLabel();
            };

            CardPanel dataCard = AddCard(page, "Настройки рядом с программой", 0, y, _contentHost.Width - 8, 150);
            ry = AddSwitchRow(dataCard, "Настройки рядом с программой", "okna.ini вместо реестра — для флешки", 14, out _portableSwitch);
            _portableLabel = MakeLabel("", 18, 58, Theme.FontTiny, Theme.Faint);
            _portableLabel.AutoSize = false; _portableLabel.Size = new Size(dataCard.Width - 36, 16);
            dataCard.Controls.Add(_portableLabel);
            int bx = 18;
            JellyButton export = MakeSmallButton(dataCard, "Экспорт…", bx, 82, 100); bx += 106; export.Click += delegate { ExportSettings(); };
            JellyButton import = MakeSmallButton(dataCard, "Импорт…", bx, 82, 100); bx += 106; import.Click += delegate { ImportSettings(); };
            JellyButton reset = MakeSmallButton(dataCard, "Сбросить", bx, 82, 100); bx += 106; reset.Click += delegate { ResetSettings(); };
            JellyButton diagnostics = MakeSmallButton(dataCard, "Диагностика…", bx, 82, 110);
            diagnostics.Click += delegate
            {
                Diagnostics.Show(this, _settings, _engine, _tray != null ? _tray.HotkeyStatus : Loc.T("неизвестно"));
            };
        }

        private void BuildAppearancePage(Panel page)
        {
            int y = 0;
            CardPanel langCard = AddCard(page, "Язык и тема", 0, y, _contentHost.Width - 8, 136); y += 144;
            AddLabel(langCard, "Язык", 18, 14, Theme.FontLabel, Theme.Text);
            _languageAuto = MakeChoice(langCard, "Системный", 100, 10, 96, 28);
            _languageAuto.Tag = (int)UiLanguage.Auto; _languageAuto.Click += delegate { SetLanguage((int)UiLanguage.Auto); };
            _languageRu = MakeChoice(langCard, "Русский", 204, 10, 96, 28);
            _languageRu.Tag = (int)UiLanguage.Russian; _languageRu.Click += delegate { SetLanguage((int)UiLanguage.Russian); };
            _languageEn = MakeChoice(langCard, "English", 308, 10, 96, 28);
            _languageEn.Tag = (int)UiLanguage.English; _languageEn.Click += delegate { SetLanguage((int)UiLanguage.English); };
            AddDivider(langCard, 18, 50, langCard.Width - 36);
            AddLabel(langCard, "Тема оформления", 18, 62, Theme.FontLabel, Theme.Text);
            AddLabel(langCard, "Тёмная тема настроек", 18, 84, Theme.FontSmall, Theme.Muted);
            _darkThemeSwitch = new JellySwitch();
            _darkThemeSwitch.Location = new Point(langCard.Width - 68, 66);
            _darkThemeSwitch.CheckedChanged += delegate
            {
                _settings.DarkTheme = _darkThemeSwitch.Checked;
                ApplyTheme();
                SaveSoon();
            };
            langCard.Controls.Add(_darkThemeSwitch);

            CardPanel aboutCard = AddCard(page, "Окна", 0, y, _contentHost.Width - 8, 90);
            AddLabel(aboutCard, "Инерционные окна для Windows", 18, 14, Theme.FontSmall, Theme.Muted);
            AddLabel(aboutCard, "Окно настроек тоже желейное — потяните за заголовок, чтобы проверить.", 18, 36, Theme.FontSmall, Theme.Text);
            AddLabel(aboutCard, "© Сергей  ·  " + Updater.CurrentLabel, 18, 58, Theme.FontTiny, Theme.Faint);
        }

        private static CardPanel AddCard(Panel page, string title, int x, int y, int w, int h)
        {
            CardPanel card = new CardPanel();
            card.Location = new Point(x, y);
            card.Size = new Size(w, h);
            page.Controls.Add(card);
            return card;
        }

        private static Label AddLabel(Control parent, string text, int x, int y, Font font, Color color)
        {
            Label label = MakeLabel(text, x, y, font, color);
            parent.Controls.Add(label);
            return label;
        }

        private static void AddDivider(Control parent, int x, int y, int w)
        {
            Panel d = new Panel(); d.Location = new Point(x, y); d.Size = new Size(w, 1);
            d.BackColor = Theme.BorderSoft; parent.Controls.Add(d);
        }

        private static int AddSwitchRow(Control parent, string title, string sub, int y, out JellySwitch sw)
        {
            int textW = Math.Max(80, parent.ClientSize.Width - 18 - 74);
            Label t = AddLabel(parent, title, 18, y, Theme.FontCardTitle, Theme.Text);
            t.AutoSize = false; t.Size = new Size(textW, 20);
            Label s = AddLabel(parent, sub, 18, y + 20, Theme.FontSmall, Theme.Muted);
            s.AutoSize = false; s.Size = new Size(textW, 16);
            sw = new JellySwitch();
            sw.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sw.Location = new Point(Math.Max(18, parent.ClientSize.Width - sw.Width - 20), y + 6);
            parent.Controls.Add(sw);
            return y + 50;
        }

        private static JellyButton MakeChoice(Control parent, string text, int x, int y, int w, int h)
        {
            JellyButton b = new JellyButton();
            b.Text = text; b.Location = new Point(x, y); b.Size = new Size(w, h); b.Font = Theme.FontSmall;
            parent.Controls.Add(b);
            return b;
        }

        private static JellyButton MakeSmallButton(Control parent, string text, int x, int y, int w)
        {
            return MakeChoice(parent, text, x, y, w, 28);
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

            _wobbleSwitch.CheckedChanged += delegate { _settings.ReleaseWobble = _wobbleSwitch.Checked; SaveSoon(); };

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

            _rollSwitch.CheckedChanged += delegate { _settings.Roll = _rollSwitch.Checked; SaveSoon(); };

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

            _callSwitch.CheckedChanged += delegate { _settings.PauseOnCall = _callSwitch.Checked; SaveSoon(); };
            _batterySwitch.CheckedChanged += delegate { _settings.PauseOnBattery = _batterySwitch.Checked; SaveSoon(); };
            _motionSwitch.CheckedChanged += delegate { _settings.RespectReduceMotion = _motionSwitch.Checked; SaveSoon(); };
            _fullscreenSwitch.CheckedChanged += delegate { _settings.PauseOnFullscreen = _fullscreenSwitch.Checked; SaveSoon(); };
            if (_resumeDelaySlider != null)
            {
                _resumeDelaySlider.ValueChanged += delegate
                {
                    if (_syncing) return;
                    _settings.AutoPauseResumeDelaySeconds = _resumeDelaySlider.Value;
                    UpdateResumeDelayLabel();
                    SaveSoon();
                };
            }
            if (_autoUpdateSwitch != null)
            {
                _autoUpdateSwitch.CheckedChanged += delegate
                {
                    if (_syncing) return;
                    _settings.AutoUpdate = _autoUpdateSwitch.Checked;
                    if (_tray != null) _tray.UpdateMenu();
                    SaveSoon();
                    UpdateUpdateStatusLabel();
                };
            }

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
                    _settings.HotkeyKey = 0; _settings.HotkeyModifiers = 0; ApplyHotkey(); return;
                }
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey ||
                    e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                    return;
                Keys modifiers = Control.ModifierKeys;
                int mods = 0;
                if ((modifiers & Keys.Control) != 0) mods |= (int)Native.MOD_CONTROL;
                if ((modifiers & Keys.Alt) != 0) mods |= (int)Native.MOD_ALT;
                if ((modifiers & Keys.Shift) != 0) mods |= (int)Native.MOD_SHIFT;
                if (mods == 0) return;
                _settings.HotkeyModifiers = mods;
                _settings.HotkeyKey = (int)e.KeyCode;
                ApplyHotkey();
            };

            _slider.ValueChanged += delegate
            {
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
            _hotkeyStatus.Text = _tray != null ? _tray.HotkeyStatus : Loc.T(ok ? "работает" : "занята");
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
                _fullscreenSwitch.Checked = _settings.PauseOnFullscreen;
                if (_resumeDelaySlider != null)
                {
                    int d = _settings.AutoPauseResumeDelaySeconds;
                    if (d < _resumeDelaySlider.Minimum) d = _resumeDelaySlider.Minimum;
                    if (d > _resumeDelaySlider.Maximum) d = _resumeDelaySlider.Maximum;
                    _resumeDelaySlider.Value = d;
                    UpdateResumeDelayLabel();
                }
                if (_autoUpdateSwitch != null) _autoUpdateSwitch.Checked = _settings.AutoUpdate;
                _portableSwitch.Checked = _settings.IsPortable;
                UpdatePortableLabel();
                UpdateUpdateStatusLabel();
                _hotkeyButton.Text = _settings.HotkeyLabel;
                _hotkeyStatus.Text = _tray != null ? _tray.HotkeyStatus : "";
                _hotkeyStatus.ForeColor = Theme.Muted;
                RefreshExcludeList();
                RefreshOverrideList();
                RefreshCallAppsList();
                _slider.Value = _settings.Jelly;
                UpdateJellyLabel();
                UpdatePresetStyles();
                UpdateLanguageStyles();
                UpdateHint();
                _statusPill.SetStatus(_engine.PauseReason ?? _engine.Status);
                UpdatePauseStatusLabel();
            }
            finally { _syncing = false; }
        }

        private void UpdateHint()
        {
            if (_hintLabel == null) return;
            _hintLabel.Text = Loc.T("Потяните это окно за заголовок — оно желейное  ·  Shift — обычное перетаскивание  ·  ") +
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

        private void UpdateJellyLabel() { _valueLabel.Text = _settings.Jelly + "  ·  " + _settings.JellyLabel; }

        internal void ApplyTheme()
        {
            if (_settings.DarkTheme)
            { BackColor = Color.FromArgb(26, 28, 32); ForeColor = Color.FromArgb(224, 224, 224); }
            else
            { BackColor = Theme.Bg; ForeColor = Theme.Text; }
            foreach (Control c in Controls) c.Invalidate();
            if (_navItems != null) for (int i = 0; i < _navItems.Length; i++) _navItems[i].Invalidate();
            if (_pages != null) for (int i = 0; i < _pages.Length; i++) foreach (Control c in _pages[i].Controls) c.Invalidate();
        }

        private void SetCloseEffect(int effect)
        {
            _settings.Shatter = effect == 1;
            _settings.Burn = effect == 2;
            _settings.Fade = effect == 3;
            UpdateCloseStyles();
            SaveSoon();
        }

        private void DemoCloseEffect()
        {
            if (SmokeMode) return;
            try
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                int w = Math.Min(360, Math.Max(240, area.Width / 4));
                int h = Math.Min(220, Math.Max(140, area.Height / 4));
                int left = area.Left + (area.Width - w) / 2;
                int top = area.Top + (area.Height - h) / 2;
                Native.RECT frame = new Native.RECT();
                frame.Left = left; frame.Top = top; frame.Right = left + w; frame.Bottom = top + h;

                Bitmap shot = new Bitmap(w, h);
                using (Graphics g = Graphics.FromImage(shot))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                        Color.FromArgb(246, 248, 251), Color.FromArgb(232, 237, 244), LinearGradientMode.Vertical))
                        g.FillRectangle(bg, 0, 0, w, h);
                    using (SolidBrush title = new SolidBrush(Color.FromArgb(48, 92, 140)))
                        g.FillRectangle(title, 0, 0, w, 34);
                    using (Pen border = new Pen(Color.FromArgb(120, 150, 180), 1f))
                        g.DrawRectangle(border, 0, 0, w - 1, h - 1);
                    using (Font f = new Font("Segoe UI", 11f, FontStyle.Bold))
                        TextRenderer.DrawText(g, Loc.T("Демонстрация"), f, new Rectangle(12, 6, w - 24, 24),
                            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    using (Font f = new Font("Segoe UI", 10f))
                        TextRenderer.DrawText(g, Loc.T("Так выглядит выбранный эффект закрытия."), f,
                            new Rectangle(18, 52, w - 36, h - 70), Color.FromArgb(50, 60, 74),
                            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
                }

                if (_settings.Burn)
                {
                    BurnOverlay.Play(shot, frame);
                }
                else if (_settings.Shatter)
                {
                    Native.POINT impact = new Native.POINT();
                    impact.X = frame.Right - 18;
                    impact.Y = frame.Top + 14;
                    ShatterOverlay.Play(shot, frame, impact);
                }
                else if (_settings.Fade)
                {
                    FadeOverlay.Play(shot, frame);
                }
                else
                {
                    using (shot) { }
                    if (_hintLabel != null)
                    {
                        _hintLabel.Text = Loc.T("Выбран вариант без эффекта — демонстрация не нужна.");
                        _hintLabel.ForeColor = Theme.Muted;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Close demo failed: " + ex.Message);
            }
        }

        private void UpdateCloseStyles()
        {
            int selected = _settings.Burn ? 2 : (_settings.Shatter ? 1 : (_settings.Fade ? 3 : 0));
            JellyButton[] buttons = new JellyButton[] { _closeNone, _closeShatter, _closeBurn, _closeFade };
            for (int i = 0; i < buttons.Length; i++)
            {
                bool accent = i == selected;
                if (buttons[i].Accent != accent) { buttons[i].Accent = accent; buttons[i].Invalidate(); }
            }
        }

        private void UpdateDirectionStyles()
        {
            if (_directionButtons == null) return;
            RollDirection selected = _settings.RollDirection;
            for (int i = 0; i < _directionButtons.Length; i++)
            {
                bool accent = (RollDirection)i == selected;
                if (_directionButtons[i].Accent != accent) { _directionButtons[i].Accent = accent; _directionButtons[i].Invalidate(); }
            }
        }

        private void UpdatePresetStyles()
        {
            int selected = _settings.CurrentPresetIndex;
            // Fallback to slider-based mapping if index doesn't fit 6 presets
            if (selected < 0 || selected >= _presets.Length)
            {
                int value = _slider == null ? _settings.Jelly : _slider.Value;
                selected = _settings.Economy ? 3 : (value < 42 ? 0 : (value < 100 ? 1 : 2));
            }
            for (int i = 0; i < _presets.Length; i++)
            {
                bool accent = i == selected;
                if (_presets[i].Accent != accent) { _presets[i].Accent = accent; _presets[i].Invalidate(); }
            }
        }

        private void SetLanguage(int language)
        {
            _settings.Language = language;
            Loc.Configure(language);
            Loc.Apply(this);
            if (_navItems != null) for (int i = 0; i < _navItems.Length; i++) _navItems[i].Invalidate();
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
                if (buttons[i].Accent != accent) { buttons[i].Accent = accent; buttons[i].Invalidate(); }
            }
        }

        private void SaveSoon() { if (SmokeMode || _saveTimer == null) return; _saveTimer.Stop(); _saveTimer.Start(); }
        private void Persist() { if (SmokeMode) return; _settings.Save(); }

        private void DrawExcludeItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(selected ? Theme.BorderSoft : Theme.CardAlt)) e.Graphics.FillRectangle(back, e.Bounds);
            string text = Convert.ToString(_excludeList.Items[e.Index]);
            TextRenderer.DrawText(e.Graphics, text, _excludeList.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void DrawCallAppItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || _callAppsList == null) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(selected ? Theme.BorderSoft : Theme.CardAlt)) e.Graphics.FillRectangle(back, e.Bounds);
            string text = Convert.ToString(_callAppsList.Items[e.Index]);
            TextRenderer.DrawText(e.Graphics, text, _callAppsList.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void DrawOverrideItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(selected ? Theme.BorderSoft : Theme.CardAlt)) e.Graphics.FillRectangle(back, e.Bounds);
            string text = Convert.ToString(_overrideList.Items[e.Index]);
            TextRenderer.DrawText(e.Graphics, text, _overrideList.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void RefreshExcludeList()
        {
            if (_excludeList == null) return;
            _excludeList.BeginUpdate(); _excludeList.Items.Clear();
            string[] names = _settings.Exclude;
            for (int i = 0; i < names.Length; i++) _excludeList.Items.Add(names[i]);
            _excludeList.EndUpdate();
        }

        private void RefreshOverrideList()
        {
            if (_overrideList == null) return;
            _overrideList.BeginUpdate(); _overrideList.Items.Clear();
            string[] list = _settings.AppProfiles;
            for (int i = 0; i < list.Length; i++)
            {
                string mask; int effect; bool bend; bool shake; bool roll;
                if (!AppSettings.ParseProfile(list[i], out mask, out effect, out bend, out shake, out roll)) continue;
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
                dialog.Title = "Программы без эффектов"; dialog.Filter = "Программы (*.exe)|*.exe"; dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string[] files = dialog.FileNames;
                for (int i = 0; i < files.Length; i++) _settings.AddExclude(Path.GetFileName(files[i]));
                RefreshExcludeList(); SaveSoon();
            }
        }

        private void PickRunning() { WindowPicker.ShowPicker(this, _settings); RefreshExcludeList(); SaveSoon(); }

        private void AddMask()
        {
            string mask = _excludeMask.Text.Trim(); if (mask.Length == 0) return;
            _settings.AddExclude(mask); _excludeMask.Text = ""; RefreshExcludeList(); SaveSoon();
        }

        private void RemoveSelectedExclusion()
        {
            string name = _excludeList.SelectedItem as string; if (name == null) return;
            _settings.RemoveExclude(name); RefreshExcludeList(); SaveSoon();
        }

        private void RefreshCallAppsList()
        {
            if (_callAppsList == null) return;
            _callAppsList.BeginUpdate(); _callAppsList.Items.Clear();
            string[] names = _settings.CallApps;
            if (names != null)
                for (int i = 0; i < names.Length; i++) _callAppsList.Items.Add(names[i]);
            _callAppsList.EndUpdate();
        }

        private void AddCallAppFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Loc.T("Программы для автопаузы (звонки/экран)");
                dialog.Filter = Loc.T("Программы (*.exe)|*.exe");
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string[] files = dialog.FileNames;
                for (int i = 0; i < files.Length; i++) _settings.AddCallApp(Path.GetFileName(files[i]));
                RefreshCallAppsList(); SaveSoon();
            }
        }

        private void AddCallAppMask()
        {
            if (_callAppMask == null) return;
            string mask = _callAppMask.Text.Trim(); if (mask.Length == 0) return;
            _settings.AddCallApp(mask); _callAppMask.Text = ""; RefreshCallAppsList(); SaveSoon();
        }

        private void RemoveSelectedCallApp()
        {
            if (_callAppsList == null) return;
            string name = _callAppsList.SelectedItem as string; if (name == null) return;
            _settings.RemoveCallApp(name); RefreshCallAppsList(); SaveSoon();
        }

        private void AddOverride()
        {
            string program; int effect; bool bend; bool shake; bool roll;
            if (!CloseOverrideDialog.ShowFor(this, _settings, "", out program, out effect, out bend, out shake, out roll)) return;
            _settings.SetAppProfile(program, effect, bend, shake, roll);
            RefreshOverrideList(); SaveSoon();
        }

        private void RemoveOverride()
        {
            int index = _overrideList.SelectedIndex; if (index < 0) return;
            string[] list = _settings.AppProfiles; if (index >= list.Length) return;
            string mask; int effect; bool bend; bool shake; bool roll;
            if (!AppSettings.ParseProfile(list[index], out mask, out effect, out bend, out shake, out roll)) return;
            _settings.RemoveCloseOverride(mask); RefreshOverrideList(); SaveSoon();
        }

        private void ExportSettings()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Loc.T("Экспорт настроек"); dialog.Filter = Loc.T("Настройки Окон (*.ini)|*.ini|Все файлы (*.*)|*.*"); dialog.FileName = "okna.ini";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _settings.Export(dialog.FileName);
                MessageBox.Show(this, Loc.T("Настройки сохранены в ") + dialog.FileName, Loc.T("Окна"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ImportSettings()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Loc.T("Импорт настроек"); dialog.Filter = Loc.T("Настройки Окон (*.ini)|*.ini|Все файлы (*.*)|*.*");
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!_settings.Import(dialog.FileName))
                { MessageBox.Show(this, Loc.T("В файле нет настроек Окон."), Loc.T("Окна"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                Persist(); SyncFromSettings(); if (_tray != null) _tray.ApplyHotkey(true);
                MessageBox.Show(this, Loc.T("Настройки загружены."), Loc.T("Окна"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ResetSettings()
        {
            if (MessageBox.Show(this, Loc.T("Вернуть все настройки к заводским?"), Loc.T("Окна"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _settings.ResetAll(); Persist(); SyncFromSettings(); if (_tray != null) _tray.ApplyHotkey(true);
        }

        internal string RunSmokeTest()
        {
            SmokeMode = true;
            System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
            int failed = 0;
            Action<string, bool> check = delegate(string name, bool ok) { if (ok) lines.Add("[ok] " + name); else { failed++; lines.Add("[FAIL] " + name); } };
            try
            {
                SelectPage(0); Application.DoEvents();
                for (int i = 0; i < _presets.Length; i++) { _presets[i].PerformClick(); Application.DoEvents(); check("preset " + i + " applies", _settings.CurrentPresetIndex == i); }
                _slider.Value = 150; Application.DoEvents(); check("the slider reaches the settings", _settings.Jelly == 150);

                SelectPage(1); Application.DoEvents();
                bool bend = _settings.Bend; _bendSwitch.Checked = !bend; Application.DoEvents(); check("the bend switch changes the setting", _settings.Bend == !bend); _bendSwitch.Checked = bend;
                bool roll = _settings.Roll; _rollSwitch.Checked = !roll; Application.DoEvents(); check("the roll switch changes the setting", _settings.Roll == !roll); _rollSwitch.Checked = roll;
                for (int i = 0; i < _directionButtons.Length; i++) { _directionButtons[i].PerformClick(); Application.DoEvents(); check("roll direction " + i + " applies", (int)_settings.RollDirection == i); }

                SelectPage(2); Application.DoEvents();
                check("close demo button exists", _closeDemo != null);
                _closeBurn.PerformClick(); Application.DoEvents(); check("burn is chosen alone", _settings.Burn && !_settings.Shatter);
                _closeShatter.PerformClick(); Application.DoEvents(); check("shards are chosen alone", _settings.Shatter && !_settings.Burn);
                _closeNone.PerformClick(); Application.DoEvents(); check("no close effect can be chosen", !_settings.Shatter && !_settings.Burn);

                SelectPage(3); Application.DoEvents();
                check("pause controls exist", _fullscreenSwitch != null && _resumeDelaySlider != null && _timedPauseButton != null);
                _resumeDelaySlider.Value = 5; Application.DoEvents(); check("resume delay setting applies", _settings.AutoPauseResumeDelaySeconds == 5);
                _settings.AddCallApp("smoke-call.exe"); RefreshCallAppsList(); check("call app list shows a custom app", _callAppsList != null && _callAppsList.Items.Count > 0);
                _settings.RemoveCallApp("smoke-call.exe"); RefreshCallAppsList();

                SelectPage(4); Application.DoEvents();
                _settings.AddExclude("smoke-test.exe"); RefreshExcludeList(); check("the exclusion list shows a new mask", _excludeList.Items.Count > 0); _settings.RemoveExclude("smoke-test.exe"); RefreshExcludeList();
                _settings.SetAppProfile("smoke-test.exe", 2, true, false, false); RefreshOverrideList(); check("the program list shows a new rule", _overrideList.Items.Count > 0); _settings.RemoveCloseOverride("smoke-test.exe"); RefreshOverrideList();

                SelectPage(5); Application.DoEvents();
                int originalLanguage = _settings.Language;
                _languageEn.PerformClick(); Application.DoEvents();
                check("English changes the language", _settings.Language == (int)UiLanguage.English);
                check("English translates a visible button", _presets[0].Text == "Calm");
                _languageRu.PerformClick(); Application.DoEvents();
                check("Russian comes back", _settings.Language == (int)UiLanguage.Russian && _presets[0].Text == "Спокойно");
                _settings.Language = originalLanguage; Loc.Configure(originalLanguage); Loc.Apply(this); SyncFromSettings();

                SelectPage(6); Application.DoEvents();
                check("system page is last with updates", _autoUpdateSwitch != null && _checkUpdateButton != null && _skipResetUpdateButton != null);
                SelectPage(0);

                string report = Diagnostics.BuildReport(_settings, _engine, "smoke");
                check("the diagnostics report has content", report.Length > 200);
                check("the diagnostics report names the program", report.IndexOf("Okna", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception ex) { failed++; lines.Add("[FAIL] the smoke test crashed: " + ex.Message); }
            finally { SmokeMode = true; }
            lines.Add(""); lines.Add(failed == 0 ? "All " + lines.Count + " UI checks passed." : failed + " of " + lines.Count + " UI checks failed.");
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        public void ShowSettings()
        {
            if (Visible) { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); BringToFront(); return; }
            Show(); WindowState = FormWindowState.Normal; Activate(); BringToFront();
            if (!_jellyDemoDone)
            {
                _jellyDemoDone = true;
                BeginInvoke(new MethodInvoker(delegate { if (Visible) _engine.ShakeWindow(Handle); }));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ReallyExit && e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.TaskManagerClosing)
            { e.Cancel = true; Persist(); BeginInvoke(new MethodInvoker(delegate { Hide(); })); return; }
            Persist(); base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (_statusTimer != null) _statusTimer.Dispose(); if (_saveTimer != null) _saveTimer.Dispose(); }
            base.Dispose(disposing);
        }

        private sealed class CategoryNavItem : Control
        {
            public string Title { get; set; }
            public string Hint { get; set; }
            public bool Active { get; set; }
            private bool _hover;

            public CategoryNavItem()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                TabStop = false;
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF rect = new RectangleF(0, 0, Width - 1, Height - 1);
                if (Active)
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(150, Theme.CardAlt))) Theme.FillRounded(g, fill, rect, 12f);
                    using (Pen border = new Pen(Color.FromArgb(120, Theme.Accent), 1f)) Theme.DrawRounded(g, border, rect, 12f);
                    using (SolidBrush bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, 2, 10, 3, Height - 20);
                }
                else if (_hover)
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(80, Theme.CardAlt))) Theme.FillRounded(g, fill, rect, 12f);
                }
                string title = string.IsNullOrEmpty(AccessibleDescription) ? (Title ?? Text ?? "") : Loc.T(AccessibleDescription);
                if (string.IsNullOrEmpty(AccessibleDescription) && !string.IsNullOrEmpty(Text)) title = Loc.T(Text);
                string hint = Loc.T(Hint ?? "");
                TextRenderer.DrawText(g, title, Theme.FontLabel, new Rectangle(14, 6, Width - 18, 20),
                    Active ? Theme.Text : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, hint, Theme.FontTiny, new Rectangle(14, 26, Width - 18, 16),
                    Theme.Faint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
}
