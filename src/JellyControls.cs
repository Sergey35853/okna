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
                else if (_pressed) fill = Theme.AccentDark;
                else if (_hover) fill = Theme.Blend(Theme.Accent, Color.White, 0.08f);
                else fill = Theme.Accent;
                textColor = Enabled ? Color.White : Theme.Faint;
            }
            else
            {
                if (!Enabled) fill = Theme.Card;
                else if (_pressed) fill = Theme.CardAlt;
                else if (_hover) fill = Theme.Blend(Theme.Card, Theme.Border, 0.35f);
                else fill = Theme.Card;
                textColor = Enabled ? Theme.Text : Theme.Faint;
            }

            radius = 6f;
            using (SolidBrush brush = new SolidBrush(fill))
                Theme.FillRounded(g, brush, rect, radius);

            Color border = Accent ? Color.FromArgb(40, 0, 0, 0) : Theme.Border;
            if (!Accent && _hover) border = Theme.Blend(Theme.Border, Theme.Accent, 0.25f);
            using (Pen pen = new Pen(border))
                Theme.DrawRounded(g, pen, rect, radius);

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
        private bool _showTicks;

        public event EventHandler ValueChanged;

        public JellySlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Height = 26;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        /// <summary>Show subtle tick marks (useful for discrete ranges).</summary>
        public bool ShowTicks
        {
            get { return _showTicks; }
            set { _showTicks = value; Invalidate(); }
        }

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; if (_value < _minimum) Value = _minimum; Invalidate(); }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = value; if (_value > _maximum) Value = _maximum; Invalidate(); }
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
                Focus();
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
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = (_maximum - _minimum) > 60 ? 5 : 1;
            if (e.KeyCode == Keys.Left) { Value -= step; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Value += step; e.Handled = true; }
            else if (e.KeyCode == Keys.Home) { Value = _minimum; e.Handled = true; }
            else if (e.KeyCode == Keys.End) { Value = _maximum; e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) { Value -= 10; e.Handled = true; }
            else if (e.KeyCode == Keys.PageUp) { Value += 10; e.Handled = true; }
            base.OnKeyDown(e);
        }

        private void SetFromX(int x)
        {
            float left = 8f;
            float right = Width - 8f;
            if (right <= left) return;
            float t = (x - left) / (right - left);
            t = Theme.Clamp(t, 0f, 1f);
            Value = _minimum + (int)Math.Round(t * (_maximum - _minimum));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);

            float left = 8f;
            float right = Width - 8f;
            float cy = Height / 2f;
            float trackH = (_hover || _dragging) ? 4f : 3f;
            RectangleF track = new RectangleF(left, cy - trackH / 2f, right - left, trackH);

            Color trackBg = Theme.DarkMode ? Color.FromArgb(70, 70, 70) : Color.FromArgb(220, 220, 220);
            using (SolidBrush brush = new SolidBrush(trackBg))
                Theme.FillRounded(g, brush, track, trackH / 2f);

            float t = _maximum == _minimum ? 0f : (_value - _minimum) / (float)(_maximum - _minimum);
            float knobX = left + (right - left) * t;
            if (knobX > left + 1)
            {
                RectangleF fill = new RectangleF(left, cy - trackH / 2f, knobX - left, trackH);
                using (SolidBrush brush = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted))
                    Theme.FillRounded(g, brush, fill, trackH / 2f);
            }

            float r = (_hover || _dragging) ? 8f : 7f;
            Color thumbFill = Theme.DarkMode ? Color.FromArgb(250, 250, 250) : Color.White;
            if (!Enabled) thumbFill = Theme.CardAlt;
            using (SolidBrush knob = new SolidBrush(thumbFill))
                g.FillEllipse(knob, knobX - r, cy - r, r * 2, r * 2);
            using (Pen ring = new Pen(Enabled ? Theme.Accent : Theme.Border, _dragging ? 2f : 1.5f))
                g.DrawEllipse(ring, knobX - r, cy - r, r * 2, r * 2);

            if (Focused && ShowFocusCues)
            {
                using (Pen pen = new Pen(Color.FromArgb(120, Theme.Accent), 1f))
                    g.DrawEllipse(pen, knobX - r - 3, cy - r - 3, (r + 3) * 2, (r + 3) * 2);
            }
        }
    }

    internal sealed class StatusPill : Control
    {
        private string _status = "Готово";

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(132, 30);
            BackColor = Color.Transparent;
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
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool paused = _status == "Пауза" || _status == "Ошибка" || _status == "Конфликт";
            Color dotColor = paused ? Theme.Warm : Color.FromArgb(15, 123, 15);
            string text = _status;
            if (text == "Готово") text = "Активно";
            if (text == "Удержание") text = "Активно";
            text = Loc.Status(text);

            RectangleF rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (SolidBrush fill = new SolidBrush(Theme.Card))
                Theme.FillRounded(g, fill, rect, 6f);
            using (Pen border = new Pen(Theme.Border))
                Theme.DrawRounded(g, border, rect, 6f);

            float cy = Height / 2f;
            using (SolidBrush dot = new SolidBrush(dotColor))
                g.FillEllipse(dot, 12, cy - 4, 8, 8);

            TextRenderer.DrawText(g, text, Font, new Rectangle(26, 0, Width - 30, Height),
                Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class CardPanel : Panel
    {
        public string SectionTitle { get; set; }

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
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color pageBg = Parent != null ? Parent.BackColor : Theme.Bg;
            if (pageBg.A != 255) pageBg = Theme.Bg;
            g.Clear(pageBg);

            RectangleF rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (SolidBrush fill = new SolidBrush(Theme.Card))
                Theme.FillRounded(g, fill, rect, 8f);
            using (Pen pen = new Pen(Theme.Border))
                Theme.DrawRounded(g, pen, rect, 8f);
        }
    }
}
