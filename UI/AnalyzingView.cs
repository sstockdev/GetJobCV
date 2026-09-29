namespace GetJobCV.UI
{
    /// <summary>
    /// Shown while the pipeline runs: one row per stage, checked off as it goes.
    /// </summary>
    internal sealed class AnalyzingView : UserControl
    {
        /// <summary>
        /// The status messages <c>Main.Analyze</c> reports, in order, with the label shown for each.
        /// </summary>
        private static readonly (string Status, string Label)[] Stages =
        [
            ("Extracting Text", "Extracting text"),
            ("Extracting Socials", "Finding GitHub and LinkedIn links"),
            ("Finding Sections", "Finding resume sections"),
            ("Parsing Resume", "Reading contact details, schools, and roles"),
            ("Running Named Entity Recognition", "Recognizing skills, people, and places"),
            ("Preprocessing Text", "Preparing text"),
            ("Scoring", "Scoring"),
        ];

        private readonly StepList _steps = new([.. Stages.Select(s => s.Label)]) { Dock = DockStyle.Top };
        private readonly Label _file = Theme.TextLabel("", Theme.Body(10.5f), Theme.Muted);

        /// <summary>
        /// Raised when Cancel is clicked.
        /// </summary>
        public event EventHandler? CancelRequested;

        public AnalyzingView()
        {
            BackColor = Theme.Background;
            Dock = DockStyle.Fill;

            TableLayoutPanel column = new() { ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Label title = Theme.TextLabel("Reading your resume", Theme.Display(20f), Theme.Ink);
            title.Margin = new Padding(0, 0, 0, 6);
            _file.Margin = new Padding(0, 0, 0, 24);

            Card card = new() { Dock = DockStyle.Top, Padding = new Padding(0, 8, 0, 8), Margin = new Padding(0) };
            _steps.Height = _steps.GetPreferredSize(Size.Empty).Height;
            card.Height = _steps.Height + card.Padding.Vertical;
            card.Controls.Add(_steps);

            column.Controls.Add(title, 0, 0);
            column.Controls.Add(_file, 0, 1);
            column.Controls.Add(card, 0, 2);

            Button cancel = Theme.Button("Cancel", Theme.ButtonKind.Secondary);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.Margin = new Padding(0, 20, 0, 0);
            cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
            column.Controls.Add(cancel, 0, 3);

            CenteredColumn center = new() { Dock = DockStyle.Fill, MaxContentWidth = 600, Padding = new Padding(24, 72, 24, 24) };
            center.Controls.Add(column);
            Controls.Add(center);
        }

        public static int StageCount => Stages.Length;

        public void Start(string resumePath)
        {
            _file.Text = $"{Path.GetFileName(resumePath)} · usually a few seconds";
            _steps.Current = 0;
        }

        /// <summary>
        /// Moves to the stage for a status message. Returns its 1-based number, or 0 if unknown.
        /// </summary>
        public int Report(string status)
        {
            int index = Array.FindIndex(Stages, s => s.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return 0;
            _steps.Current = index;
            return index + 1;
        }
    }
}
