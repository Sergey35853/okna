using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Okna
{
    internal sealed class JellyButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public bool Accent { get; set; }

        public JellyButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.FontButton;
            Cursor = Cursors.Hand;
            Height = 34;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            RectangleF rect = new RectangleF(0, 0, Width - 1, Height - 1);
            float radius = 10f;
            Color fill;
            Color textColor;

            if (Accent)
            {
                if (!Enabled) fill = Theme.CardAlt;
                else if (_pressed) fill = Theme.Blend(Theme.Accent, Color.Black, 0.16f);
                else if (_hover) fill = Theme.Blend(Theme.Accent, Color.White, 0.10f);
                else fill = Theme.Accent;
                textColor = Enabled ? Theme.Bg : Theme.Faint;
            }
            else
            {
                if (!Enabled) fill = Theme.Card;
                else if (_pressed) fill = Theme.Blend(Theme.CardAlt, Color.Black, 0.12f);
                else if (_hover) fill = Theme.Blend(Theme.CardAlt, Theme.Accent, 0.07f);
                else fill = Theme.CardAlt;
                textColor = Enabled ? Theme.Text : Theme.Faint;
            }

            using (SolidBrush brush = new SolidBrush(fill))
                Theme.FillRounded(g, brush, rect, radius);

            if (!Accent)
            {
                using (Pen pen = new Pen(_hover ? Theme.Blend(Theme.Border, Theme.Accent, 0.25f) : Theme.Border))
                    Theme.DrawRounded(g, pen, rect, radius);
            }

            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(rect), textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(Rectangle.Round(rect), -4, -4);
                using (Pen pen = new Pen(Color.FromArgb(Accent ? 100 : 80, Theme.Text)))
                    Theme.DrawRounded(g, pen, focus, 7f);
            }
        }
    }

    internal sealed class JellySwitch : Control
    {
        private bool _checked;
        private float _knob;
        private float _target;
        private readonly Timer _timer;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public JellySwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(50, 28);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            TabStop = true;
            _knob = 0f;
            _target = 0f;

            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += delegate
            {
                float delta = _target - _knob;
                if (Math.Abs(delta) < 0.03f)
                {
                    _knob = _target;
                    _timer.Stop();
                }
                else
                {
                    _knob += delta * 0.35f;
                }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _target = value ? 1f : 0f;
                _timer.Start();
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float h = Math.Min(24f, Height - 4f);
            RectangleF track = new RectangleF(1, (Height - h) / 2f, Width - 2, h);
            Color off = _hover ? Theme.Blend(Theme.CardAlt, Theme.Muted, 0.10f) : Theme.CardAlt;
            Color on = _hover ? Theme.Blend(Theme.AccentDark, Theme.Accent, 0.24f) : Theme.AccentDark;
            Color trackColor = Theme.Blend(off, on, _knob);

            using (SolidBrush brush = new SolidBrush(trackColor))
                Theme.FillRounded(g, brush, track, h / 2f);

            using (Pen pen = new Pen(Color.FromArgb(90, Theme.Border)))
                Theme.DrawRounded(g, pen, track, h / 2f);

            float knobSize = h - 6f;
            float left = track.Left + 3f;
            float right = track.Right - 3f - knobSize;
            float kx = left + (right - left) * _knob;
            float ky = track.Top + 3f;

            if (_checked)
            {
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(35, Theme.Accent)))
                    g.FillEllipse(glow, kx - 5, ky - 5, knobSize + 10, knobSize + 10);
            }

            using (SolidBrush knob = new SolidBrush(_checked ? Color.FromArgb(241, 253, 249) : Color.FromArgb(205, 221, 224)))
                g.FillEllipse(knob, kx, ky, knobSize, knobSize);
            using (Pen edge = new Pen(Color.FromArgb(70, 6, 16, 20)))
                g.DrawEllipse(edge, kx, ky, knobSize, knobSize);

            if (Focused && ShowFocusCues)
            {
                using (Pen pen = new Pen(Color.FromArgb(110, Theme.Accent)))
                    g.DrawEllipse(pen, 0.5f, 0.5f, Width - 1, Height - 1);
            }
        }
    }

    internal sealed class JellySlider : Control
    {
        private int _minimum;
        private int _maximum = 100;
        private int _value = 50;
        private bool _dragging;
        private bool _hover;

        public event EventHandler ValueChanged;

        public JellySlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 28;
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; if (_value < _minimum) Value = _minimum; }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = value; if (_value > _maximum) Value = _maximum; }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < _minimum) v = _minimum;
                if (v > _maximum) v = _maximum;
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                _dragging = true;
                Capture = true;
                SetFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging && Enabled)
                SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { Value -= 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Value += 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Home) { Value = _minimum; e.Handled = true; }
            else if (e.KeyCode == Keys.End) { Value = _maximum; e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) { Value -= 10; e.Handled = true; }
            else if (e.KeyCode == Keys.PageUp) { Value += 10; e.Handled = true; }
            base.OnKeyDown(e);
        }

        private void SetFromX(int x)
        {
            float left = 10f;
            float right = Width - 10f;
            if (right <= left) return;
            float t = (x - left) / (right - left);
            t = Theme.Clamp(t, 0f, 1f);
            Value = _minimum + (int)Math.Round(t * (_maximum - _minimum));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float left = 10f;
            float right = Width - 10f;
            float cy = Height / 2f;
            RectangleF track = new RectangleF(left, cy - 3f, right - left, 6f);

            using (SolidBrush brush = new SolidBrush(Theme.CardAlt))
                Theme.FillRounded(g, brush, track, 3f);

            float t = _maximum == _minimum ? 0f : (_value - _minimum) / (float)(_maximum - _minimum);
            float knobX = left + (right - left) * t;
            if (knobX > left + 2)
            {
                RectangleF fill = new RectangleF(left, cy - 3f, knobX - left, 6f);
                using (LinearGradientBrush brush = new LinearGradientBrush(fill,
                    Theme.AccentDark, Theme.Accent, LinearGradientMode.Horizontal))
                    Theme.FillRounded(g, brush, fill, 3f);
            }

            float knobRadius = _hover || _dragging ? 9f : 8f;
            if (_hover || _dragging)
            {
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(35, Theme.Accent)))
                    g.FillEllipse(glow, knobX - knobRadius - 5, cy - knobRadius - 5, (knobRadius + 5) * 2, (knobRadius + 5) * 2);
            }

            using (SolidBrush knob = new SolidBrush(Color.FromArgb(238, 251, 248)))
                g.FillEllipse(knob, knobX - knobRadius, cy - knobRadius, knobRadius * 2, knobRadius * 2);
            using (Pen ring = new Pen(Theme.Accent, 2.2f))
                g.DrawEllipse(ring, knobX - knobRadius, cy - knobRadius, knobRadius * 2, knobRadius * 2);

            if (Focused && ShowFocusCues)
            {
                using (Pen pen = new Pen(Color.FromArgb(100, Theme.Accent), 1f))
                    g.DrawEllipse(pen, knobX - knobRadius - 4, cy - knobRadius - 4, (knobRadius + 4) * 2, (knobRadius + 4) * 2);
            }
        }
    }

    internal sealed class StatusPill : Control
    {
        private string _status = "Готово";

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(138, 28);
            BackColor = Theme.Bg;
            Font = Theme.FontSmall;
        }

        public void SetStatus(string status)
        {
            if (status == null) status = "";
            if (_status == status) return;
            _status = status;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool paused = _status == "Пауза" || _status == "Ошибка" || _status == "Конфликт";
            Color dotColor = paused ? Theme.Warm : Theme.Accent;
            string text = _status;
            if (text == "Готово") text = "Активно";
            if (text == "Удержание") text = "Активно";

            RectangleF rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(150, Theme.CardAlt)))
                Theme.FillRounded(g, fill, rect, Height / 2f);
            using (Pen border = new Pen(Color.FromArgb(100, Theme.Border)))
                Theme.DrawRounded(g, border, rect, Height / 2f);

            float cy = Height / 2f;
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(paused ? 22 : 34, dotColor)))
                g.FillEllipse(glow, 10, cy - 8, 16, 16);
            using (SolidBrush dot = new SolidBrush(dotColor))
                g.FillEllipse(dot, 14, cy - 4, 8, 8);

            TextRenderer.DrawText(g, text, Font, new Rectangle(28, 0, Width - 34, Height),
                paused ? Theme.Warm : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);

            RectangleF rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                Theme.FillRounded(g, shadow, new RectangleF(2, 5, Width - 5, Height - 5), 16f);
            using (SolidBrush fill = new SolidBrush(Theme.Card))
                Theme.FillRounded(g, fill, rect, 16f);
            using (Pen pen = new Pen(Theme.BorderSoft))
                Theme.DrawRounded(g, pen, rect, 16f);
        }
    }

    internal sealed class JellyPreview : Control
    {
        private const float StageW = 512f;
        private const float StageH = 156f;
        private const float MiniW = 214f;
        private const float MiniH = 112f;

        private readonly AppSettings _settings;
        private readonly Timer _timer;
        private double _x = 150;
        private double _y = 29;
        private double _vx;
        private double _vy;
        private double _targetX = 150;
        private double _targetY = 29;
        private bool _dragging;
        private float _grabX;
        private float _grabY;

        public JellyPreview(AppSettings settings)
        {
            _settings = settings;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;

            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += delegate { Step(); };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (!Visible) _timer.Stop();
            base.OnVisibleChanged(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            PointF p = ToStage(e.Location);
            if (p.X >= _x && p.X <= _x + MiniW && p.Y >= _y && p.Y <= _y + MiniH)
            {
                _dragging = true;
                _grabX = p.X - (float)_x;
                _grabY = p.Y - (float)_y;
                Capture = true;
                Cursor = Cursors.SizeAll;
                _timer.Start();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            PointF p = ToStage(e.Location);
            if (_dragging)
            {
                _targetX = Theme.Clamp(p.X - _grabX, 18, StageW - MiniW - 18);
                _targetY = Theme.Clamp(p.Y - _grabY, 22, StageH - MiniH - 16);
                _timer.Start();
            }
            else
            {
                bool over = p.X >= _x && p.X <= _x + MiniW && p.Y >= _y && p.Y <= _y + MiniH;
                Cursor = over ? Cursors.Hand : Cursors.Default;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                Cursor = Cursors.Hand;
                _timer.Start();
            }
            base.OnMouseUp(e);
        }

        private PointF ToStage(Point p)
        {
            float scale = Math.Min(Width / StageW, Height / StageH);
            if (scale <= 0) scale = 1;
            float ox = (Width - StageW * scale) / 2f;
            float oy = (Height - StageH * scale) / 2f;
            return new PointF((p.X - ox) / scale, (p.Y - oy) / scale);
        }

        private void Step()
        {
            double k = _settings.SpringK * 0.85;
            double c = _settings.SpringC * 0.85;
            double dt = 0.012;
            int steps = 6;
            double h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                double ax = k * (_targetX - _x) - c * _vx;
                double ay = k * (_targetY - _y) - c * _vy;
                _vx += ax * h;
                _vy += ay * h;
                _x += _vx * h;
                _y += _vy * h;
            }

            double dist = Math.Sqrt((_targetX - _x) * (_targetX - _x) + (_targetY - _y) * (_targetY - _y));
            double speed = Math.Sqrt(_vx * _vx + _vy * _vy);
            if (!_dragging && dist < 0.15 && speed < 1.0)
            {
                _x = _targetX;
                _y = _targetY;
                _vx = 0;
                _vy = 0;
                _timer.Stop();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            RectangleF full = new RectangleF(0, 0, Width - 1, Height - 1);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(13, 24, 32)))
                Theme.FillRounded(g, fill, full, 16f);
            using (Pen border = new Pen(Theme.BorderSoft))
                Theme.DrawRounded(g, border, full, 16f);

            float scale = Math.Min(Width / StageW, Height / StageH);
            if (scale <= 0) scale = 1;
            float ox = (Width - StageW * scale) / 2f;
            float oy = (Height - StageH * scale) / 2f;
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(scale, scale);

            // Dot grid
            using (SolidBrush dots = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
            {
                for (float x = 18; x < StageW; x += 26)
                    for (float y = 18; y < StageH; y += 26)
                        g.FillEllipse(dots, x, y, 1.4f, 1.4f);
            }

            // Soft accent glow in the upper right.
            for (int i = 5; i >= 1; i--)
            {
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(4 + i * 2, Theme.Accent)))
                    g.FillEllipse(glow, StageW - 118 - i * 10, -95 - i * 8, 190 + i * 20, 190 + i * 20);
            }

            using (SolidBrush label = new SolidBrush(Theme.Accent))
                g.DrawString("ПОПРОБУЙТЕ", Theme.FontTiny, label, 18, 14);

            using (SolidBrush hint = new SolidBrush(Theme.Faint))
                g.DrawString("Потяните мини-окно", Theme.FontSmall, hint, 18, StageH - 27);

            DrawMiniWindow(g);
        }

        private void DrawMiniWindow(Graphics g)
        {
            float speed = (float)Math.Sqrt(_vx * _vx + _vy * _vy);

            // Drop shadow
            for (int i = 5; i >= 1; i--)
            {
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(8 + i * 3, 0, 0, 0)))
                    Theme.FillRounded(g, shadow,
                        new RectangleF((float)_x - i, (float)_y - i + 8, MiniW + i * 2, MiniH + i * 2), 16f);
            }

            GraphicsState state = g.Save();
            float cx = (float)_x + MiniW / 2f;
            float cy = (float)_y + MiniH / 2f;
            float angle = Theme.Clamp((float)_vx * 0.0042f, -3.2f, 3.2f);
            float stretch = Theme.Clamp((float)_vx * 0.000035f, -0.035f, 0.035f);
            float squash = Theme.Clamp((float)_vy * 0.00003f, -0.028f, 0.028f);
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            g.ScaleTransform(1f + stretch, 1f - stretch * 0.55f + squash);
            g.TranslateTransform(-cx, -cy);

            RectangleF body = new RectangleF((float)_x, (float)_y, MiniW, MiniH);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(243, 249, 247)))
                Theme.FillRounded(g, fill, body, 14f);
            using (Pen edge = new Pen(Color.FromArgb(210, 229, 225)))
                Theme.DrawRounded(g, edge, body, 14f);

            // Title bar with rounded top corners only
            float titleH = 28f;
            using (GraphicsPath path = new GraphicsPath())
            {
                float d = 28f;
                path.AddArc((float)_x, (float)_y, d, d, 180, 90);
                path.AddArc((float)_x + MiniW - d, (float)_y, d, d, 270, 90);
                path.AddLine((float)_x + MiniW, (float)_y + titleH, (float)_x, (float)_y + titleH);
                path.CloseFigure();
                using (SolidBrush bar = new SolidBrush(Theme.Accent))
                    g.FillPath(bar, path);
            }
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(16, 48, 44)))
            {
                g.FillEllipse(dot, (float)_x + 13, (float)_y + 10, 7, 7);
                g.FillEllipse(dot, (float)_x + 26, (float)_y + 10, 7, 7);
                g.FillEllipse(dot, (float)_x + 39, (float)_y + 10, 7, 7);
            }

            using (SolidBrush title = new SolidBrush(Color.FromArgb(19, 50, 46)))
                g.DrawString("Окно", Theme.FontMini, title, (float)_x + 64, (float)_y + 6);

            using (SolidBrush bar = new SolidBrush(Color.FromArgb(214, 227, 224)))
            {
                g.FillRectangle(bar, (float)_x + 16, (float)_y + 44, 126, 7);
                g.FillRectangle(bar, (float)_x + 16, (float)_y + 58, 168, 7);
            }
            using (SolidBrush bar = new SolidBrush(Color.FromArgb(232, 240, 238)))
                g.FillRectangle(bar, (float)_x + 16, (float)_y + 72, 96, 7);

            using (SolidBrush pill = new SolidBrush(Theme.Accent))
                Theme.FillRounded(g, pill, new RectangleF((float)_x + 16, (float)_y + 88, 58, 15), 7.5f);
            using (SolidBrush pillText = new SolidBrush(Color.FromArgb(15, 48, 44)))
                g.DrawString("ok", Theme.FontTiny, pillText, (float)_x + 36, (float)_y + 88);

            // Jelly blob
            using (SolidBrush blob = new SolidBrush(Color.FromArgb(35, Theme.Accent)))
                g.FillEllipse(blob, (float)_x + MiniW - 52, (float)_y + MiniH - 52, 34, 34);
            using (Pen blobEdge = new Pen(Color.FromArgb(120, Theme.AccentDark), 2f))
                g.DrawEllipse(blobEdge, (float)_x + MiniW - 45, (float)_y + MiniH - 45, 20, 20);

            g.Restore(state);
        }
    }
}
