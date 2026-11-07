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
            text = Loc.Status(text);

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
}
