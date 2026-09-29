using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace GetJobCV.UI
{
    /// <summary>
    /// A panel with a rounded border, used for every card and input box.
    /// </summary>
    internal class Card : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = 12;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Theme.Border;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public DashStyle BorderDash { get; set; } = DashStyle.Solid;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float BorderWidth { get; set; } = 1f;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            Padding = new Padding(20);
        }

        /// <summary>
        /// With one child, as tall as that child needs at the proposed width.
        /// </summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            if (Controls.Count != 1) return base.GetPreferredSize(proposedSize);
            int inner = Math.Max(0, proposedSize.Width - Padding.Horizontal);
            return new Size(proposedSize.Width,
                Sizing.HeightFor(Controls[0], inner) + Padding.Vertical);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float inset = BorderWidth / 2f;
            RectangleF r = new(inset, inset, Width - BorderWidth - 0.5f, Height - BorderWidth - 0.5f);
            using GraphicsPath path = Theme.RoundRect(r, LogicalToDeviceUnits(Radius));
            using SolidBrush fill = new(BackColor);
            g.FillPath(fill, path);
            using Pen pen = new(BorderColor, BorderWidth) { DashStyle = BorderDash };
            g.DrawPath(pen, path);
        }
    }

    /// <summary>
    /// A multi-line text box drawn inside a rounded card.
    /// </summary>
    internal sealed class TextCard : Card
    {
        public TextBox Box { get; } = new()
        {
            Multiline = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = Theme.Body(10f),
            ForeColor = Theme.Ink,
            AcceptsReturn = true,
        };

        public TextCard()
        {
            Radius = 10;
            BorderColor = Theme.ControlBorder;
            Padding = new Padding(12, 10, 4, 10);
            BackColor = Color.White;
            Box.BackColor = BackColor;
            Controls.Add(Box);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]

        public bool ReadOnly
        {
            get => Box.ReadOnly;
            set
            {
                Box.ReadOnly = value;
                BackColor = Box.BackColor = value ? Theme.Extra.Fill : Color.White;
                Box.ForeColor = value ? Theme.Muted : Theme.Ink;
                Invalidate();
            }
        }
    }

    /// <summary>
    /// The status pill in the header: a colored dot and a short message.
    /// </summary>
    internal sealed class Pill : Control
    {
        public enum Kind { Ready, Loading, Working, Error }

        private Kind _kind = Kind.Loading;

        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = Theme.Body(9.75f, FontStyle.Bold);
        }

        public void Show(Kind kind, string text)
        {
            _kind = kind;
            Text = text;
            Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }

        private (Color Fore, Color Back) Colors => _kind switch
        {
            Kind.Ready => (Color.FromArgb(0x2F, 0x5A, 0x2A), Color.FromArgb(0xEA, 0xF1, 0xE8)),
            Kind.Loading => (Color.FromArgb(0x6B, 0x4A, 0x10), Color.FromArgb(0xF5, 0xEC, 0xD9)),
            Kind.Working => (Theme.Matched.Ink, Theme.Matched.Fill),
            _ => (Color.FromArgb(0x8A, 0x1C, 0x1C), Color.FromArgb(0xF8, 0xE3, 0xE1)),
        };

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font);
            int max = LogicalToDeviceUnits(640);
            return new Size(Math.Min(max, text.Width + LogicalToDeviceUnits(36)), text.Height + LogicalToDeviceUnits(10));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            (Color fore, Color back) = Colors;

            using (GraphicsPath path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
            using (SolidBrush fill = new(back))
                g.FillPath(fill, path);

            int dot = LogicalToDeviceUnits(8);
            int left = LogicalToDeviceUnits(12);
            using (SolidBrush dotBrush = new(fore))
                g.FillEllipse(dotBrush, left, (Height - dot) / 2f, dot, dot);

            Rectangle textRect = new(left + dot + LogicalToDeviceUnits(6), 0, Width - left - dot - LogicalToDeviceUnits(14), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// The overall score as a ring with the percentage in the middle.
    /// </summary>
    internal sealed class ScoreRing : Control
    {
        private double _value;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]

        public double Value
        {
            get => _value;
            set { _value = Math.Clamp(value, 0, 1); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]

        public Font NumberFont { get; set; } = Theme.Display(24f);

        public ScoreRing()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = Theme.Body(8.25f);
            Size = new Size(124, 124);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int side = Math.Min(Width, Height);
            float stroke = side * 0.1f;
            RectangleF r = new(stroke / 2, stroke / 2, side - stroke, side - stroke);

            using (Pen track = new(Theme.Track, stroke))
                g.DrawEllipse(track, r);
            if (_value > 0)
            {
                using Pen arc = new(Theme.Accent, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(arc, r, -90, (float)(360 * _value));
            }

            string number = $"{_value * 100:F0}%";
            Size numberSize = TextRenderer.MeasureText(number, NumberFont);
            Size captionSize = TextRenderer.MeasureText("overall", Font);
            int top = (side - numberSize.Height - captionSize.Height) / 2;
            TextRenderer.DrawText(g, number, NumberFont, new Rectangle(0, top, side, numberSize.Height),
                Theme.Ink, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, "overall", Font, new Rectangle(0, top + numberSize.Height, side, captionSize.Height),
                Theme.Muted, TextFormatFlags.HorizontalCenter);
        }
    }

    /// <summary>
    /// A thin horizontal progress bar.
    /// </summary>
    internal sealed class MeterBar : Control
    {
        private double _value;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]

        public double Value
        {
            get => _value;
            set { _value = Math.Clamp(value, 0, 1); Invalidate(); }
        }

        public MeterBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            ForeColor = Theme.Accent;
            Height = 10;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = Height / 2f;

            using (GraphicsPath track = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), radius))
            using (SolidBrush brush = new(Theme.Track))
                g.FillPath(brush, track);

            float filled = (float)((Width - 1) * _value);
            if (filled < 1) return;
            using GraphicsPath bar = Theme.RoundRect(new RectangleF(0, 0, Math.Max(filled, Height), Height - 1), radius);
            using SolidBrush fill = new(ForeColor);
            g.FillPath(fill, bar);
        }
    }

    /// <summary>
    /// One skill: a filled square for hot skills, a hollow one for in-demand skills,
    /// and an optional tag saying where it was found when that counts for less.
    /// </summary>
    internal sealed class SkillChip : Control
    {
        private readonly ChipStyle _style;
        private readonly int _tier;
        private readonly string? _tag;
        private static readonly Font TagFont = Theme.Mono(7.5f);

        public SkillChip(string name, int tier, string? tag, ChipStyle style)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Text = name;
            _tier = tier;
            _tag = tag;
            _style = style;
            Font = Theme.Body(9.75f, FontStyle.Bold);
            Margin = new Padding(0, 0, 6, 6);
            Size = GetPreferredSize(Size.Empty);
        }

        /// <summary>
        /// Draw as a nice-to-have skill: dashed border, no fill.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Dashed { get; set; }

        private int Marker => _tier > 0 ? LogicalToDeviceUnits(8) + LogicalToDeviceUnits(6) : 0;

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int width = LogicalToDeviceUnits(20) + Marker + text.Width;
            if (_tag is not null)
                width += LogicalToDeviceUnits(6) + TextRenderer.MeasureText(_tag, TagFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            return new Size(width, text.Height + LogicalToDeviceUnits(12));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), LogicalToDeviceUnits(6)))
            {
                // Nice to have: no fill and a dashed ink border, so it reads apart from color alone
                using SolidBrush fill = new(Dashed ? Theme.Surface : _style.Fill);
                g.FillPath(fill, path);
                using Pen border = Dashed
                    ? new(_style.Ink, 1.25f) { DashStyle = DashStyle.Dash }
                    : new(_style.Border);
                g.DrawPath(border, path);
            }

            int x = LogicalToDeviceUnits(10);
            if (_tier > 0)
            {
                int size = LogicalToDeviceUnits(_tier == 2 ? 8 : 7);
                RectangleF box = new(x, (Height - size) / 2f, size, size);
                if (_tier == 2)
                {
                    using SolidBrush ink = new(_style.Ink);
                    g.FillRectangle(ink, box);
                }
                else
                {
                    using Pen ink = new(_style.Ink, 1.25f);
                    g.DrawRectangle(ink, box.X, box.Y, box.Width, box.Height);
                }
                x += Marker;
            }

            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, text.Width + 2, Height), _style.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (_tag is not null)
            {
                x += text.Width + LogicalToDeviceUnits(6);
                TextRenderer.DrawText(g, _tag, TagFont, new Rectangle(x, 0, Width - x, Height), _style.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>
    /// The pipeline steps with a check for each finished one and a spinner on the current one.
    /// </summary>
    internal sealed class StepList : Control
    {
        private readonly System.Windows.Forms.Timer _spin = new() { Interval = 40 };
        private float _angle;
        private int _current;

        public IReadOnlyList<string> Steps { get; }

        public StepList(IReadOnlyList<string> steps)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Steps = steps;
            Font = Theme.Body(10.5f);
            _spin.Tick += (_, _) => { _angle = (_angle + 12) % 360; Invalidate(); };
        }

        private int RowHeight => LogicalToDeviceUnits(52);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]

        public int Current
        {
            get => _current;
            set { _current = Math.Clamp(value, 0, Steps.Count); Invalidate(); }
        }

        public override Size GetPreferredSize(Size proposedSize) =>
            new(proposedSize.Width, RowHeight * Steps.Count);

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            _spin.Enabled = Visible;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _spin.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int row = RowHeight;
            int pad = LogicalToDeviceUnits(20);
            int icon = LogicalToDeviceUnits(22);
            using Font bold = new(Font, FontStyle.Bold);

            for (int i = 0; i < Steps.Count; i++)
            {
                int top = i * row;
                RectangleF circle = new(pad, top + (row - icon) / 2f, icon, icon);

                if (i == _current)
                {
                    using SolidBrush tint = new(Theme.AccentTint);
                    g.FillRectangle(tint, 0, top, Width, row);
                    using Pen arc = new(Theme.Accent, LogicalToDeviceUnits(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    RectangleF inner = RectangleF.Inflate(circle, -2, -2);
                    g.DrawArc(arc, inner, _angle, 280);
                }
                else if (i < _current)
                {
                    using SolidBrush fill = new(Theme.Accent);
                    g.FillEllipse(fill, circle);
                    using Pen check = new(Color.White, LogicalToDeviceUnits(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                    float s = icon / 24f;
                    g.DrawLines(check, [
                        new PointF(circle.X + 7 * s, circle.Y + 12 * s),
                        new PointF(circle.X + 10.5f * s, circle.Y + 15.5f * s),
                        new PointF(circle.X + 17 * s, circle.Y + 8.5f * s)]);
                }
                else
                {
                    using Pen ring = new(Theme.ControlBorder, LogicalToDeviceUnits(2));
                    g.DrawEllipse(ring, RectangleF.Inflate(circle, -2, -2));
                }

                int textLeft = pad + icon + LogicalToDeviceUnits(14);
                TextRenderer.DrawText(g, Steps[i], i == _current ? bold : Font,
                    new Rectangle(textLeft, top, Width - textLeft - pad, row),
                    i > _current ? Theme.Muted : Theme.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>
    /// Keeps its one child centered horizontally at no more than <see cref="MaxContentWidth"/>.
    /// </summary>
    internal sealed class CenteredColumn : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int MaxContentWidth { get; set; } = 1040;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Controls.Count == 0) return;
            Control child = Controls[0];
            int width = Math.Min(LogicalToDeviceUnits(MaxContentWidth), ClientSize.Width - Padding.Horizontal);
            child.SetBounds((ClientSize.Width - width) / 2, Padding.Top, Math.Max(0, width),
                Math.Max(0, ClientSize.Height - Padding.Vertical));
        }
    }
}
