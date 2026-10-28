using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Okna
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly JellyEngine _engine;
        private readonly Icon _icon;

        private JellyPreview _preview;
        private JellySwitch _effectSwitch;
        private JellySwitch _wobbleSwitch;
        private JellySwitch _bendSwitch;
        private JellySwitch _maxShakeSwitch;
        private JellySwitch _shatterSwitch;
        private JellySwitch _startupSwitch;
        private JellySlider _slider;
        private Label _valueLabel;
        private StatusPill _statusPill;
        private JellyButton[] _presets;
        private Timer _statusTimer;
        private Timer _saveTimer;

        public bool ReallyExit { get; set; }

        public SettingsForm(AppSettings settings, JellyEngine engine, Icon icon)
        {
            _settings = settings;
            _engine = engine;
            _icon = icon;

            Text = "Окна";
            Icon = icon;
            ClientSize = new Size(560, 804);
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
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw, true);

            BuildUi();
            WireEvents();

            _statusTimer = new Timer();
            _statusTimer.Interval = 350;
            _statusTimer.Tick += delegate
            {
                _statusPill.SetStatus(_settings.Enabled ? _engine.Status : "Пауза");
            };
            _statusTimer.Start();

            _saveTimer = new Timer();
            _saveTimer.Interval = 350;
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                _settings.Save();
            };

            _effectSwitch.Checked = _settings.Enabled;
            _wobbleSwitch.Checked = _settings.ReleaseWobble;
            _bendSwitch.Checked = _settings.Bend;
            _maxShakeSwitch.Checked = _settings.MaxShake;
            _shatterSwitch.Checked = _settings.Shatter;
            _startupSwitch.Checked = _settings.StartWithWindows;
            _slider.Value = _settings.Jelly;
            UpdateJellyLabel();
            UpdatePresetStyles();
            _statusPill.SetStatus(_engine.Status);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.EnableDarkTitleBar(Handle);
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

            _presets = new JellyButton[3];
            string[] names = new string[] { "Спокойно", "Баланс", "Желе" };
            for (int i = 0; i < 3; i++)
            {
                JellyButton b = new JellyButton();
                b.Text = names[i];
                b.Location = new Point(20 + i * 161, 160);
                b.Size = new Size(150, 28);
                b.Font = Theme.FontSmall;
                b.Tag = i;
                _presets[i] = b;
                moveCard.Controls.Add(b);
            }

            CardPanel optionsCard = new CardPanel();
            optionsCard.Location = new Point(24, 470);
            optionsCard.Size = new Size(512, 260);
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

            Label shatterTitle = MakeLabel("Разбивать окно при закрытии", 20, 166, Theme.FontCardTitle, Theme.Text);
            optionsCard.Controls.Add(shatterTitle);
            Label shatterSub = MakeLabel("Осколки стекла разлетаются от кнопки закрытия", 20, 188, Theme.FontSmall, Theme.Muted);
            optionsCard.Controls.Add(shatterSub);

            _shatterSwitch = new JellySwitch();
            _shatterSwitch.Location = new Point(432, 170);
            optionsCard.Controls.Add(_shatterSwitch);

            Panel divider5 = new Panel();
            divider5.Location = new Point(20, 206);
            divider5.Size = new Size(472, 1);
            divider5.BackColor = Theme.BorderSoft;
            optionsCard.Controls.Add(divider5);

            Label startupTitle = MakeLabel("Запускать с Windows", 20, 214, Theme.FontCardTitle, Theme.Text);
            optionsCard.Controls.Add(startupTitle);
            Label startupSub = MakeLabel("Тихо стартовать вместе с системой", 20, 236, Theme.FontSmall, Theme.Muted);
            optionsCard.Controls.Add(startupSub);

            _startupSwitch = new JellySwitch();
            _startupSwitch.Location = new Point(432, 218);
            optionsCard.Controls.Add(_startupSwitch);

            Label hint = MakeLabel("Shift при перетаскивании — обычное поведение Windows", 24, 742, Theme.FontSmall, Theme.Faint);
            Controls.Add(hint);

            Label version = MakeLabel(Updater.CurrentLabel + "  ·  живёт в трее", 24, 770, Theme.FontTiny, Theme.Faint);
            Controls.Add(version);

            JellyButton shake = new JellyButton();
            shake.Text = "Потрясти окно";
            shake.Location = new Point(280, 754);
            shake.Size = new Size(150, 38);
            shake.Click += delegate { _engine.ShakeWindow(Handle); };
            Controls.Add(shake);

            JellyButton done = new JellyButton();
            done.Text = "Готово";
            done.Accent = true;
            done.Location = new Point(440, 754);
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
            tip.SetToolTip(_shatterSwitch, "Окно рассыпается осколками, когда закрывается кнопкой или по Alt+F4");
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
                _statusPill.SetStatus(_settings.Enabled ? _engine.Status : "Пауза");
            };

            _wobbleSwitch.CheckedChanged += delegate
            {
                _settings.ReleaseWobble = _wobbleSwitch.Checked;
                SaveSoon();
            };

            _bendSwitch.CheckedChanged += delegate
            {
                _settings.Bend = _bendSwitch.Checked;
                SaveSoon();
            };

            _maxShakeSwitch.CheckedChanged += delegate
            {
                _settings.MaxShake = _maxShakeSwitch.Checked;
                SaveSoon();
            };

            _shatterSwitch.CheckedChanged += delegate
            {
                _settings.Shatter = _shatterSwitch.Checked;
                SaveSoon();
            };

            _startupSwitch.CheckedChanged += delegate
            {
                _settings.StartWithWindows = _startupSwitch.Checked;
                AppSettings.SetAutoStart(_startupSwitch.Checked, Application.ExecutablePath);
                SaveSoon();
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
                    int value = index == 0 ? 18 : (index == 1 ? 70 : 92);
                    _slider.Value = value;
                };
            }
        }

        public void SyncFromSettings()
        {
            _effectSwitch.Checked = _settings.Enabled;
            _wobbleSwitch.Checked = _settings.ReleaseWobble;
            _bendSwitch.Checked = _settings.Bend;
            _maxShakeSwitch.Checked = _settings.MaxShake;
            _shatterSwitch.Checked = _settings.Shatter;
            _startupSwitch.Checked = _settings.StartWithWindows;
            _slider.Value = _settings.Jelly;
            UpdateJellyLabel();
            UpdatePresetStyles();
            _statusPill.SetStatus(_settings.Enabled ? _engine.Status : "Пауза");
        }

        private void UpdateJellyLabel()
        {
            _valueLabel.Text = _settings.JellyLabel;
        }

        private void UpdatePresetStyles()
        {
            int value = _slider.Value;
            int selected = value < 42 ? 0 : (value < 82 ? 1 : 2);
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
            _saveTimer.Stop();
            _saveTimer.Start();
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

