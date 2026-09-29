namespace GetJobCV.UI
{
    /// <summary>
    /// Top bar: wordmark, status pill, and the New analysis button.
    /// </summary>
    internal sealed class AppHeader : Panel
    {
        public Pill Status { get; } = new() { Anchor = AnchorStyles.Left, Margin = new Padding(16, 0, 0, 0) };
        public Button NewAnalysis { get; } = Theme.Button("New analysis", Theme.ButtonKind.Primary);

        public AppHeader()
        {
            Dock = DockStyle.Top;
            Height = 60;
            BackColor = Theme.Surface;

            Wordmark wordmark = new() { Anchor = AnchorStyles.Left, Margin = new Padding(0) };

            NewAnalysis.Anchor = AnchorStyles.Right;
            NewAnalysis.Height = 40;
            NewAnalysis.Margin = new Padding(0);

            TableLayoutPanel bar = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(24, 0, 24, 0) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bar.Controls.Add(wordmark, 0, 0);
            bar.Controls.Add(Status, 1, 0);
            bar.Controls.Add(NewAnalysis, 3, 0);

            Controls.Add(bar);
            Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        }

        /// <summary>
        /// "GetJob" in ink and "CV" in the accent, drawn without the gap two labels leave.
        /// </summary>
        private sealed class Wordmark : Label
        {
            private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;

            public Wordmark()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                AutoSize = false;
                Font = Theme.Display(16f);
                Text = "GetJobCV";
                Size = GetPreferredSize(Size.Empty);
            }

            public override Size GetPreferredSize(Size proposedSize)
            {
                Size text = TextRenderer.MeasureText("GetJobCV", Font, Size.Empty, Flags);
                return new Size(text.Width + 2, text.Height);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
                int split = TextRenderer.MeasureText(e.Graphics, "GetJob", Font, Size.Empty, Flags).Width;
                TextRenderer.DrawText(e.Graphics, "GetJob", Font, new Rectangle(0, 0, split, Height), Theme.Ink, Flags);
                TextRenderer.DrawText(e.Graphics, "CV", Font, new Rectangle(split, 0, Width - split, Height), Theme.Accent, Flags);
            }
        }
    }
}
