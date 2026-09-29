using System.ComponentModel;
using System.Diagnostics;
using GetJobCV.Modules;

namespace GetJobCV.UI
{
    /// <summary>
    /// The report: inputs on the left for re-runs, scores, skills, and the parsed resume on the right.
    /// </summary>
    internal sealed class ResultsView : UserControl
    {
        private readonly ToolTip _tips = new();

        // Left rail
        private readonly Label _fileName = Theme.TextLabel("", Theme.Body(10f, FontStyle.Bold), Theme.Ink);
        private readonly Label _fileInfo = Theme.TextLabel("", Theme.Body(8.25f), Theme.Muted);
        private readonly TextCard _jobDescription = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
        private readonly Label _staleNote = Theme.TextLabel("", Theme.Body(8.25f), Theme.Muted);

        // Scores
        private readonly ScoreRing _ring = new();
        private readonly Label _verdict = WrapLabel("", Theme.Display(15f), Theme.Ink);
        private readonly Label _advice = WrapLabel("", Theme.Body(9.75f), Theme.Muted);
        private readonly Label _coveragePct = Theme.TextLabel("", Theme.Mono(11f), Theme.Ink);
        private readonly MeterBar _coverageBar = new() { Margin = new Padding(0, 8, 0, 0) };
        private readonly Label _similarityPct = Theme.TextLabel("", Theme.Mono(11f), Theme.Ink);
        private readonly MeterBar _similarityBar = new() { ForeColor = Theme.AccentSoft, Margin = new Padding(0, 8, 0, 0) };

        // Skills
        private readonly (Label Title, FlowLayoutPanel Chips, ChipStyle Style)[] _groups =
        [
            (GroupTitle(Theme.Matched), ChipFlow(), Theme.Matched),
            (GroupTitle(Theme.Missing), ChipFlow(), Theme.Missing),
            (GroupTitle(Theme.Extra), ChipFlow(), Theme.Extra),
        ];

        // Parsed resume
        private readonly Stack _candidate = new() { Gap = 8 };
        private readonly Stack _experience = new() { Gap = 12 };
        private readonly Stack _education = new() { Gap = 12 };
        private readonly FlowLayoutPanel _sections = ChipFlow();

        private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
        private readonly Stack _report = new() { Gap = 20, Padding = new Padding(28, 24, 28, 28) };

        private string? _resumePath;
        private string _analyzedPath = "";
        private string _analyzedJobDescription = "";

        /// <summary>
        /// Raised when Re-run analysis is clicked.
        /// </summary>
        public event EventHandler? RerunRequested;

        public ResultsView()
        {
            BackColor = Theme.Background;
            Dock = DockStyle.Fill;

            _report.Controls.Add(BuildScoreRow());
            _report.Controls.Add(BuildSkillsCard());
            _report.Controls.Add(BuildResumeRow());
            _scroll.Controls.Add(_report);
            _scroll.Layout += (_, _) => LayoutReport();

            Controls.Add(_scroll);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border });
            Controls.Add(BuildRail());

            _jobDescription.Box.TextChanged += (_, _) => UpdateStaleNote();

            // Names for screen readers where there's no visible label to read
            _jobDescription.Box.AccessibleName = "Job description";
            foreach ((Label _, FlowLayoutPanel chips, ChipStyle style) in _groups)
                chips.AccessibleRole = AccessibleRole.List;
            _sections.AccessibleName = "Sections found";
            _sections.AccessibleRole = AccessibleRole.List;
        }

        public string JobDescription => _jobDescription.Box.Text;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? ResumePath
        {
            get => _resumePath;
            set
            {
                _resumePath = value;
                _fileName.Text = value is null ? "No resume" : Path.GetFileName(value);
                _fileInfo.Text = value is null ? "" : StartView.DescribeFile(value);
                UpdateStaleNote();
            }
        }

        /// <summary>
        /// Fills the report from one analysis run.
        /// </summary>
        public void ShowResult(
            string resumePath,
            string jobDescription,
            ResumeRecord record,
            ExperienceSummary experience,
            IReadOnlyList<ResumeSection> sections,
            SkillMatcher.SkillReport skills,
            double cosine)
        {
            SuspendLayout();
            _tips.RemoveAll();
            _analyzedPath = resumePath;
            _analyzedJobDescription = jobDescription;
            _jobDescription.Box.Text = jobDescription;
            ResumePath = resumePath;

            ScoreCombiner.CombinedScore combined = ScoreCombiner.Combine(cosine, skills.WeightCoverage);
            _ring.Value = combined.Overall;
            _verdict.Text = combined.Verdict;
            _advice.Text = ReportText.Advice(skills);
            _coveragePct.Text = $"{skills.WeightCoverage * 100:F0}%";
            _coverageBar.Value = skills.WeightCoverage;
            _similarityPct.Text = $"{cosine * 100:F0}%";
            _similarityBar.Value = cosine;

            FillGroup(0, "Matched", skills.Matched);
            FillGroup(1, "Missing", skills.Missing);
            FillGroup(2, "Extra", skills.Extra);

            FillCandidate(record.Contact);
            FillExperience(record.Experience, experience, skills.OverallYears);
            FillEducation(record.Education, sections);

            _scroll.AutoScrollPosition = Point.Empty;
            ResumeLayout(true);
            LayoutReport();
        }

        private void LayoutReport()
        {
            // Leave room for the vertical scrollbar up front, so showing it doesn't
            // make the report too wide and add a horizontal one
            int width = _scroll.Width;
            int height = Sizing.HeightFor(_report, width);
            if (height > _scroll.Height)
            {
                width -= SystemInformation.VerticalScrollBarWidth;
                height = Sizing.HeightFor(_report, width);
            }
            Point offset = _scroll.AutoScrollPosition;
            _report.SetBounds(offset.X, offset.Y, width, height);
            Relayout(_report);
        }

        /// <summary>
        /// Lays out top down. A child whose bounds didn't change wouldn't lay out its
        /// new contents on its own.
        /// </summary>
        private static void Relayout(Control control)
        {
            control.PerformLayout();
            foreach (Control child in control.Controls)
                Relayout(child);
        }

        private void UpdateStaleNote()
        {
            bool stale = _resumePath != _analyzedPath || JobDescription != _analyzedJobDescription;
            _staleNote.Text = stale ? "Changed since the last run" : "";
        }


        // ---- Building ----

        private Control BuildRail()
        {
            TableLayoutPanel rail = new()
            {
                Dock = DockStyle.Left,
                Width = 340,
                BackColor = Theme.Rail,
                Padding = new Padding(24),
                ColumnCount = 1,
                RowCount = 6,
            };
            rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rail.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Button replace = Theme.Button("Replace", Theme.ButtonKind.Link);
            replace.AccessibleName = "Replace resume";
            replace.Click += (_, _) =>
            {
                if (Theme.BrowseForResume(this) is { } path)
                    ResumePath = path;
            };

            Label badge = new()
            {
                Text = "PDF",
                Font = Theme.Mono(8.25f),
                ForeColor = Theme.Missing.Ink,
                BackColor = Theme.Missing.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(40, 48),
                Margin = new Padding(0),
                AccessibleRole = AccessibleRole.Graphic,
                AccessibleName = "PDF file",
            };
            _fileName.AutoSize = false;
            _fileName.AutoEllipsis = true;
            _fileInfo.AutoSize = false;
            Stack names = new() { Gap = 2, Tag = Row.Fill };
            names.Controls.Add(_fileName);
            names.Controls.Add(_fileInfo);
            Row fileRow = new() { Gap = 12 };
            fileRow.Controls.Add(new FixedWidth(badge, fixedHeight: true));
            fileRow.Controls.Add(names);
            fileRow.Controls.Add(replace);
            Card fileCard = new() { Dock = DockStyle.Fill, Padding = new Padding(12), Radius = 10, Margin = new Padding(0, 0, 0, 20) };
            fileCard.Controls.Add(fileRow);
            fileRow.Dock = DockStyle.Fill;

            Label jdCaption = Theme.CaptionLabel("Job description");
            _staleNote.Margin = new Padding(0, 8, 0, 16);

            Button rerun = Theme.Button("Re-run analysis", Theme.ButtonKind.Dark);
            rerun.AutoSize = false;
            rerun.Dock = DockStyle.Fill;
            rerun.Margin = new Padding(0);
            rerun.Click += (_, _) => RerunRequested?.Invoke(this, EventArgs.Empty);

            rail.Controls.Add(Theme.CaptionLabel("Resume"), 0, 0);
            rail.Controls.Add(fileCard, 0, 1);
            rail.Controls.Add(jdCaption, 0, 2);
            rail.Controls.Add(_jobDescription, 0, 3);
            rail.Controls.Add(_staleNote, 0, 4);
            rail.Controls.Add(rerun, 0, 5);
            return rail;
        }

        private Control BuildScoreRow()
        {
            _ring.Size = new Size(124, 124);
            Stack verdict = new() { Gap = 6, Tag = Row.Fill };
            verdict.Controls.Add(_verdict);
            verdict.Controls.Add(_advice);
            Row ring = new() { Gap = 20 };
            ring.Controls.Add(_ring);
            ring.Controls.Add(verdict);
            Card scoreCard = CardOf(ring);

            Stack meters = new() { Gap = 18 };
            meters.Controls.Add(Meter("Weighted skill coverage",
                "Job skills found, weighted by demand and where they appear", _coveragePct, _coverageBar));
            meters.Controls.Add(Meter("Text similarity",
                "Cosine similarity of the two documents' term vectors", _similarityPct, _similarityBar));
            meters.Controls.Add(WrapLabel(
                $"Overall blends {ScoreCombiner.DefaultCoverageWeight:P0} skill coverage with " +
                $"{ScoreCombiner.DefaultCosineWeight:P0} text similarity.", Theme.Body(8.25f), Theme.Muted));
            Card meterCard = CardOf(meters);
            meterCard.Padding = new Padding(24, 20, 24, 20);

            return new Columns { MinColumnWidth = 380, Controls = { scoreCard, meterCard } };
        }

        private static Stack Meter(string name, string description, Label pct, MeterBar bar)
        {
            Label title = Theme.TextLabel(name, Theme.Body(10.5f, FontStyle.Bold), Theme.Ink);
            title.AutoSize = false;
            Label desc = WrapLabel(description, Theme.Body(8.25f), Theme.Muted);
            pct.AutoSize = false;

            Row header = new() { Gap = 8 };
            Stack text = new() { Gap = 2, Tag = Row.Fill };
            text.Controls.Add(title);
            text.Controls.Add(desc);
            header.Controls.Add(text);
            header.Controls.Add(pct);

            Stack meter = new();
            meter.Controls.Add(header);
            meter.Controls.Add(bar);
            return meter;
        }

        private Control BuildSkillsCard()
        {
            Label title = Theme.TextLabel("Skills", Theme.Display(14f), Theme.Ink);
            title.AutoSize = false;
            Label legend = Theme.TextLabel("■ Hot    □ In demand    Dashed = nice to have    Tagged = only listed or mentioned",
                Theme.Body(8.25f), Theme.Muted);
            legend.AutoSize = false;
            legend.Tag = Row.Fill;
            legend.TextAlign = ContentAlignment.MiddleRight;
            Row header = new() { Gap = 16 };
            header.Controls.Add(title);
            header.Controls.Add(legend);

            Columns groups = new() { Gap = 20 };
            foreach ((Label groupTitle, FlowLayoutPanel chips, ChipStyle style) in _groups)
            {
                Stack group = new() { Gap = 10 };
                group.Controls.Add(groupTitle);
                group.Controls.Add(new Panel { Height = 2, BackColor = style.Border, Margin = new Padding(0) });
                group.Controls.Add(chips);
                groups.Controls.Add(group);
            }

            Stack body = new() { Gap = 16 };
            body.Controls.Add(header);
            body.Controls.Add(groups);
            Card card = CardOf(body);
            card.Padding = new Padding(24, 20, 24, 20);
            return card;
        }

        private Control BuildResumeRow()
        {
            return new Columns
            {
                MinColumnWidth = 220,
                // Contact, roles, and schools differ a lot in length; stretching leaves empty cards
                EqualHeights = false,
                Controls =
                {
                    CardOf(Titled("Candidate", _candidate)),
                    CardOf(Titled("Experience", _experience)),
                    CardOf(Titled("Education", _education, ("Sections found", _sections))),
                },
            };
        }

        private static Stack Titled(string caption, Control body, params (string Caption, Control Body)[] more)
        {
            Stack stack = new() { Gap = 10 };
            stack.Controls.Add(CaptionOf(caption));
            stack.Controls.Add(body);
            foreach ((string c, Control b) in more)
            {
                Label extra = CaptionOf(c);
                extra.Margin = new Padding(0, 10, 0, 0);
                stack.Controls.Add(extra);
                stack.Controls.Add(b);
            }
            return stack;
        }

        private static Label CaptionOf(string text)
        {
            Label label = Theme.CaptionLabel(text);
            label.AutoSize = false;
            label.Margin = new Padding(0);
            return label;
        }

        private static Card CardOf(Control body)
        {
            Card card = new() { Radius = 14, Padding = new Padding(20, 18, 20, 18) };
            card.Controls.Add(body);
            body.Dock = DockStyle.Fill;
            return card;
        }

        private static Label WrapLabel(string text, Font font, Color color)
        {
            Label label = Theme.TextLabel(text, font, color);
            label.AutoSize = false;
            return label;
        }

        private static Label GroupTitle(ChipStyle style)
        {
            Label label = WrapLabel("", Theme.Body(10.5f, FontStyle.Bold), style.Ink);
            return label;
        }

        private static FlowLayoutPanel ChipFlow() => new()
        {
            WrapContents = true,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        // ---- Filling ----

        private void FillGroup(int index, string name, IReadOnlyList<SkillMatcher.ScoredSkill> skills)
        {
            (Label title, FlowLayoutPanel chips, ChipStyle style) = _groups[index];
            title.Text = $"{name}  {skills.Count}";
            chips.AccessibleName = $"{name} skills, {skills.Count}";
            Replace(chips, skills.Count == 0
                ? [Muted("None")]
                : skills.Select(s => Chip(s, style)));
            SkillChip.MakeGroupTabStop(chips);
        }

        private SkillChip Chip(SkillMatcher.ScoredSkill skill, ChipStyle style)
        {
            SkillChip chip = new(skill.Name, skill.Tier, ReportText.Tag(skill), style) { Dashed = skill.Preferred };
            string explanation = ReportText.Explanation(skill);
            _tips.SetToolTip(chip, explanation);

            // Keyboard focus gets the hover explanation too. It goes in the name because
            // screen readers always read the name; support for descriptions varies.
            chip.AccessibleName = $"{skill.Name}. {explanation}";
            chip.GotFocus += (_, _) => _tips.Show(explanation, chip, 0, chip.Height + 4, 5000);
            chip.LostFocus += (_, _) => _tips.Hide(chip);
            return chip;
        }

        private void FillCandidate(ContactInfo contact)
        {
            List<Control> rows = [WrapLabel(contact.Name ?? "Name not found", Theme.Display(13f), Theme.Ink)];
            rows.Add(Field("Email", contact.Email, null));
            rows.Add(Field("Phone", contact.Phone, null));
            rows.Add(Field("GitHub", contact.GitHub, contact.GitHub));
            rows.Add(Field("LinkedIn", contact.LinkedIn, contact.LinkedIn));
            Replace(_candidate, rows);
        }

        private static Row Field(string name, string? value, string? url)
        {
            Label key = WrapLabel(name, Theme.Body(9.75f), Theme.Muted);
            key.Width = 72;
            key.AutoSize = false;
            Label shown;
            if (value is not null && url is not null && ReportText.WebUri(url) is { } uri)
            {
                LinkLabel link = new()
                {
                    Text = value,
                    Font = Theme.Body(9.75f),
                    LinkColor = Theme.Accent,
                    ActiveLinkColor = Theme.Ink,
                    AutoSize = false,
                    AutoEllipsis = true,
                    Margin = new Padding(0),
                };
                link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                shown = link;
            }
            else
            {
                shown = WrapLabel(value ?? "—", Theme.Body(9.75f), value is null ? Theme.Muted : Theme.Ink);
            }
            shown.Tag = Row.Fill;

            Row row = new() { Gap = 8 };
            row.Controls.Add(new FixedWidth(key));
            row.Controls.Add(shown);
            return row;
        }

        private void FillExperience(IReadOnlyList<ExperienceEntry> roles, ExperienceSummary summary,
            SkillMatcher.YearsCheck? required)
        {
            List<Control> rows = [];
            if (summary.TotalMonths > 0 || required is not null)
            {
                Stack total = new() { Gap = 2 };
                total.Controls.Add(WrapLabel($"{summary.TotalYears:0.#} years of professional experience",
                    Theme.Body(9.75f, FontStyle.Bold), Theme.Ink));
                if (ReportText.OverallYears(required) is { } overall)
                    total.Controls.Add(WrapLabel(overall,
                        Theme.Body(9f, FontStyle.Bold), required!.Credit >= 1.0 ? Theme.Matched.Ink : Theme.Missing.Ink));
                rows.Add(total);
            }

            foreach (ExperienceEntry role in roles)
                rows.Add(Entry(role.Title ?? role.Organization ?? "Untitled role", ReportText.RoleDetails(role)));

            if (ReportText.MostTimeWith(summary) is { } mostTime)
                rows.Add(WrapLabel(mostTime, Theme.Body(8.25f), Theme.Muted));

            if (rows.Count == 0)
                rows.Add(Muted("No roles found"));
            Replace(_experience, rows);
        }

        private void FillEducation(IReadOnlyList<EducationEntry> schools, IReadOnlyList<ResumeSection> sections)
        {
            List<Control> rows = [.. schools.Select(e => Entry(e.Degree ?? e.School ?? "Unnamed school", ReportText.SchoolDetails(e)))];
            if (rows.Count == 0)
                rows.Add(Muted("No schools found"));
            Replace(_education, rows);

            Replace(_sections, sections.Count == 0
                ? [Muted("None")]
                : sections.Select(s => (Control)new SkillChip(ReportText.SectionName(s), 0, null, Theme.Extra)));
        }

        private static Stack Entry(string title, string meta)
        {
            Stack entry = new() { Gap = 2 };
            entry.Controls.Add(WrapLabel(title, Theme.Body(10f, FontStyle.Bold), Theme.Ink));
            if (meta.Length > 0)
                entry.Controls.Add(WrapLabel(meta, Theme.Body(9f), Theme.Muted));
            return entry;
        }

        private static Label Muted(string text) => Theme.TextLabel(text, Theme.Body(9.75f), Theme.Muted);

        private static void Replace(Control parent, IEnumerable<Control> children)
        {
            Control[] old = [.. parent.Controls.Cast<Control>()];
            parent.Controls.Clear();
            foreach (Control c in old)
                c.Dispose();
            parent.Controls.AddRange([.. children]);
        }

        /// <summary>
        /// Reports its child's width as its own preferred width, so a <see cref="Row"/> keeps it fixed.
        /// </summary>
        private sealed class FixedWidth : Panel
        {
            private readonly bool _fixedHeight;

            /// <param name="fixedHeight">Keep the child's height too instead of fitting its text</param>
            public FixedWidth(Control child, bool fixedHeight = false)
            {
                _fixedHeight = fixedHeight;
                Size = child.Size;
                Margin = new Padding(0);
                child.Dock = DockStyle.Fill;
                Controls.Add(child);
            }

            public override Size GetPreferredSize(Size proposedSize) =>
                new(Width, _fixedHeight ? Height : Sizing.HeightFor(Controls[0], Width));
        }
    }
}
