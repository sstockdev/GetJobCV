using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace GetJobCV.UI
{
    /// <summary>
    /// First screen: paste the job description, add the resume, then analyze.
    /// </summary>
    internal sealed class StartView : UserControl
    {
        private readonly TextCard _jobDescription = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
        private readonly Label _dropTitle = Theme.TextLabel("Drop a PDF here", Theme.Body(12f, FontStyle.Bold), Theme.Ink);
        private readonly Label _dropHint = Theme.TextLabel("PDF only for now", Theme.Body(9f), Theme.Muted);
        private readonly Button _browse = Theme.Button("Browse files", Theme.ButtonKind.Secondary);
        private readonly Label _analyzeHint = Theme.TextLabel("", Theme.Body(9.75f), Theme.Muted);
        private readonly Button _analyze = Theme.Button("Analyze", Theme.ButtonKind.Primary);

        private string? _resumePath;
        private bool _modelReady;

        /// <summary>
        /// Raised when Analyze is clicked with a job description and a resume.
        /// </summary>
        public event EventHandler? AnalyzeRequested;

        public StartView()
        {
            BackColor = Theme.Background;
            Dock = DockStyle.Fill;

            TableLayoutPanel column = new() { ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label title = Theme.TextLabel("How well does your resume fit the job?", Theme.Display(26f), Theme.Ink);
            title.Margin = new Padding(0, 0, 0, 6);
            Label subtitle = Theme.TextLabel("Everything runs on this computer. Nothing is uploaded.", Theme.Body(11f), Theme.Muted);
            subtitle.Margin = new Padding(0, 0, 0, 28);

            TableLayoutPanel cards = new() { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0) };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Card jdCard = StepCard(1, "Paste the job description", _jobDescription);
            jdCard.Margin = new Padding(0, 0, 12, 0);
            Card resumeCard = StepCard(2, "Add your resume", BuildDropZone());
            resumeCard.Margin = new Padding(12, 0, 0, 0);
            cards.Controls.Add(jdCard, 0, 0);
            cards.Controls.Add(resumeCard, 1, 0);

            TableLayoutPanel footer = new() { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 24, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _analyzeHint.Anchor = AnchorStyles.Right;
            _analyzeHint.Margin = new Padding(0, 0, 16, 0);
            _analyze.MinimumSize = new Size(140, 48);
            _analyze.Margin = new Padding(0);
            footer.Controls.Add(_analyzeHint, 0, 0);
            footer.Controls.Add(_analyze, 1, 0);

            column.Controls.Add(title, 0, 0);
            column.Controls.Add(subtitle, 0, 1);
            column.Controls.Add(cards, 0, 2);
            column.Controls.Add(footer, 0, 3);

            CenteredColumn center = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 48, 24, 32) };
            center.Controls.Add(column);
            Controls.Add(center);

            _jobDescription.Box.PlaceholderText = "Paste the full posting, including requirements and nice-to-haves";
            _jobDescription.Box.AccessibleName = "Job description";
            _jobDescription.Box.AccessibleDescription = "Paste the full posting, including requirements and nice-to-haves";
            _jobDescription.Box.TextChanged += (_, _) => UpdateState();
            _browse.Click += (_, _) =>
            {
                if (Theme.BrowseForResume(this) is { } path)
                    ResumePath = path;
            };
            _analyze.Click += (_, _) => AnalyzeRequested?.Invoke(this, EventArgs.Empty);

            UpdateState();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string JobDescription
        {
            get => _jobDescription.Box.Text;
            set => _jobDescription.Box.Text = value;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? ResumePath
        {
            get => _resumePath;
            set
            {
                _resumePath = value;
                if (value is null)
                {
                    _dropTitle.Text = "Drop a PDF here";
                    _dropHint.Text = "PDF only for now";
                    _browse.Text = "Browse files";
                }
                else
                {
                    _dropTitle.Text = Path.GetFileName(value);
                    _dropHint.Text = DescribeFile(value);
                    _browse.Text = "Choose a different file";
                }
                UpdateState();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ModelReady
        {
            get => _modelReady;
            set { _modelReady = value; UpdateState(); }
        }

        internal static string DescribeFile(string path)
        {
            try
            {
                long bytes = new FileInfo(path).Length;
                return $"PDF · {Math.Max(1, bytes / 1024):N0} KB";
            }
            catch (IOException)
            {
                return "PDF";
            }
        }

        private void UpdateState()
        {
            bool hasJd = !string.IsNullOrWhiteSpace(JobDescription);
            bool hasResume = _resumePath is not null;
            _analyze.Enabled = _modelReady && hasJd && hasResume;
            _analyzeHint.Text = !_modelReady ? "Available once the language model finishes loading"
                : !hasJd ? "Paste a job description to continue"
                : !hasResume ? "Add a resume to continue"
                : "";
        }

        private Control BuildDropZone()
        {
            Card zone = new()
            {
                Dock = DockStyle.Fill,
                BorderDash = DashStyle.Dash,
                BorderWidth = 2f,
                BorderColor = Theme.ControlBorder,
                BackColor = Theme.Rail,
                Radius = 10,
                Margin = new Padding(0),
            };

            TableLayoutPanel stack = new() { ColumnCount = 1, RowCount = 5, Dock = DockStyle.Fill, BackColor = Theme.Rail };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            _dropTitle.Anchor = AnchorStyles.None;
            _dropTitle.Margin = new Padding(0, 0, 0, 14);
            _browse.Anchor = AnchorStyles.None;
            _browse.Margin = new Padding(0, 0, 0, 14);
            _dropHint.Anchor = AnchorStyles.None;

            stack.Controls.Add(_dropTitle, 0, 1);
            stack.Controls.Add(_browse, 0, 2);
            stack.Controls.Add(_dropHint, 0, 3);
            zone.Controls.Add(stack);
            return zone;
        }

        private static Card StepCard(int number, string heading, Control body)
        {
            Card card = new() { Dock = DockStyle.Fill, Padding = new Padding(24) };
            TableLayoutPanel layout = new() { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            FlowLayoutPanel header = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 14) };
            header.Controls.Add(new StepBadge(number) { Margin = new Padding(0, 0, 12, 0) });
            Label label = Theme.TextLabel(heading, Theme.Body(12f, FontStyle.Bold), Theme.Ink);
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 3, 0, 0);
            header.Controls.Add(label);

            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(body, 0, 1);
            card.Controls.Add(layout);
            return card;
        }

        /// <summary>
        /// A dark circle with the step number.
        /// </summary>
        private sealed class StepBadge : Label
        {
            public StepBadge(int number)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                AutoSize = false;
                Text = number.ToString();
                AccessibleName = $"Step {number}";
                Font = Theme.Mono(9.75f);
                Size = new Size(28, 28);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using SolidBrush fill = new(Theme.Ink);
                e.Graphics.FillEllipse(fill, 0, 0, Width - 1, Height - 1);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Theme.Surface,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
