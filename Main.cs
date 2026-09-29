using System.Text;
using GetJobCV.Modules;
using GetJobCV.UI;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using static GetJobCV.Modules.NerExtractor;

namespace GetJobCV
{
    public partial class Main : Form
    {
        // Build on load; happens once
        private NerExtractor? _ner;

        // O*NET skill name to demand tier conversion
        private IReadOnlyDictionary<string, int> _skillTiers = new Dictionary<string, int>();

        private readonly AppHeader _header = new();
        private readonly StartView _start = new();
        private readonly AnalyzingView _analyzing = new();
        private readonly ResultsView _results = new();

        // Where to go back to if a run fails
        private Control _inputs;

        public Main()
        {
            InitializeComponent();
            BackColor = Theme.Background;
            Font = Theme.Body(9.75f);

            Panel content = new() { Dock = DockStyle.Fill };
            content.Controls.AddRange([_start, _analyzing, _results]);
            Controls.Add(content);
            Controls.Add(_header);
            _inputs = _start;
            ShowView(_start);

            _start.AnalyzeRequested += (_, _) => RunAnalysis(_start.ResumePath, _start.JobDescription, _start);
            _results.RerunRequested += (_, _) => RunAnalysis(_results.ResumePath, _results.JobDescription, _results);
            _header.NewAnalysis.Click += (_, _) =>
            {
                _start.ResumePath = null;
                _start.JobDescription = "";
                ShowView(_start);
            };

            // A PDF dropped anywhere on the inputs becomes the resume
            Theme.AcceptPdfDrops(_start, path => _start.ResumePath = path);
            Theme.AcceptPdfDrops(_results, path => _results.ResumePath = path);

            Load += Main_Load;
        }

        private void ShowView(Control view)
        {
            foreach (Control v in new Control[] { _start, _analyzing, _results })
                v.Visible = v == view;
            _header.NewAnalysis.Visible = view == _results;
        }

        /// <summary>
        /// Loads models off the UI thread.
        /// </summary>
        private async void Main_Load(object? sender, EventArgs e)
        {
            // Nothing to score with until the NER model is ready
            _header.Status.Show(Pill.Kind.Loading, "Loading NER model");

            try
            {
                _skillTiers = SkillsGazetteer.LoadWeights();
                _ner = await NerExtractor.CreateAsync(
                    SkillsGazetteer.Load(), SkillsGazetteer.LoadCaseSensitive());
                _header.Status.Show(Pill.Kind.Ready, "Ready · models loaded");
                _start.ModelReady = true;
            }
            catch (Exception ex)
            {
                _header.Status.Show(Pill.Kind.Error, $"Couldn't load NER model ({ex.Message})");
            }
        }

        private async void RunAnalysis(string? filePath, string jobDescription, Control inputs)
        {
            if (_ner is null)
            {
                _header.Status.Show(Pill.Kind.Error, "The NER model is still loading");
                return;
            }
            if (filePath is null)
            {
                _header.Status.Show(Pill.Kind.Error, "Add a resume first");
                return;
            }
            if (string.IsNullOrWhiteSpace(jobDescription))
            {
                _header.Status.Show(Pill.Kind.Error, "The job description is empty");
                return;
            }

            NerExtractor ner = _ner;
            _inputs = inputs;
            _analyzing.Start(filePath);
            ShowView(_analyzing);
            _header.Status.Show(Pill.Kind.Working, "Analyzing");

            // Progress<T> posts back to the UI thread so status updates actually paint
            IProgress<string> status = new Progress<string>(s =>
            {
                int step = _analyzing.Report(s);
                if (step > 0)
                    _header.Status.Show(Pill.Kind.Working, $"Analyzing · step {step} of {AnalyzingView.StageCount}");
            });

            try
            {
                AnalysisResult? result = await Task.Run(() =>
                    Analyze(filePath, jobDescription, ner, _skillTiers, status));

                if (result is null)
                {
                    Fail("No text could be extracted from the PDF");
                    return;
                }

                _results.ShowResult(filePath, jobDescription, result.Record, result.Experience,
                    result.Sections, result.Skills, result.Score);
                ShowView(_results);
                _header.Status.Show(Pill.Kind.Ready, "Ready");
            }
            catch (Exception ex)
            {
                Fail($"Couldn't read the PDF ({ex.Message})");
            }
        }

        /// <summary>
        /// Goes back to the screen the run started from, inputs intact.
        /// </summary>
        private void Fail(string message)
        {
            ShowView(_inputs);
            _header.Status.Show(Pill.Kind.Error, message);
        }

        /// <summary>
        /// Everything produced by one resume vs job description run.
        /// </summary>
        private sealed record AnalysisResult(
            Socials Socials,
            IReadOnlyList<ResumeSection> Sections,
            ResumeRecord Record,
            ExperienceSummary Experience,
            JobRequirements Requirements,
            NerResult Ner,
            double Score,
            SkillMatcher.SkillReport Skills);

        /// <summary>
        /// Runs the whole pipeline. Runs off the UI thread, so it only touches
        /// the UI through <paramref name="status"/>.
        /// </summary>
        /// <returns>The result, or null if no text could be extracted</returns>
        private static AnalysisResult? Analyze(
            string filePath,
            string jobDescription,
            NerExtractor ner,
            IReadOnlyDictionary<string, int> skillTiers,
            IProgress<string> status)
        {
            status.Report("Extracting Text");
            using PdfDocument document = PdfDocument.Open(filePath);
            StringBuilder allText = new();
            List<string> hyperlinks = [];

            foreach (Page page in document.GetPages())
            {
                // Newline between pages so words don't glue together across page breaks
                allText.AppendLine(ContentOrderTextExtractor.GetText(page));

                foreach (Annotation ann in page.GetAnnotations())
                {
                    if (ann.Action is UriAction uri && !string.IsNullOrWhiteSpace(uri.Uri))
                        hyperlinks.Add(uri.Uri);
                }
            }

            string resumeText = allText.ToString();
            if (String.IsNullOrWhiteSpace(resumeText))
                return null;

            // Get socials
            status.Report("Extracting Socials");
            Socials socials = SocialExtractor.Extract(resumeText, hyperlinks);

            // Split into Education, Experience, Skills, ...
            status.Report("Finding Sections");
            IReadOnlyList<ResumeSection> sections = SectionSegmenter.Segment(resumeText);

            // Pull out contact details, schools, and roles
            status.Report("Parsing Resume");
            ResumeRecord record = ResumeParser.Parse(sections, socials);

            // Run NER per section so each skill is known with where it appears
            status.Report("Running Named Entity Recognition");
            var sectionNer = sections.Select(s => (s.Type, Ner: ner.Extract(s.Text))).ToList();
            NerResult resumeNer = NerResult.Merge(sectionNer.Select(s => s.Ner));
            NerResult jdNer = ner.Extract(jobDescription);

            // Per-role skills give each skill the months of the jobs that use it
            ExperienceSummary experience = ExperienceCalculator.Summarize(
                record.Experience
                    .Where(role => role.Section == SectionType.Experience)
                    .Select(role => (role, (IEnumerable<string>)ner.Extract(role.Text).Skills)),
                DateOnly.FromDateTime(DateTime.Today));

            // "3+ years of Python" and "5+ years of experience" in the job description
            JobRequirements requirements = RequirementExtractor.Extract(jobDescription, ner);

            // Run preprocessing on text
            status.Report("Preprocessing Text");
            string[] resumeTokens = PreProcessor.Preprocess(resumeText);
            string[] jdTokens = PreProcessor.Preprocess(jobDescription);

            // Vectorize over one shared vocabulary. IDF is off: fitted on only these
            // two docs it would down-weight exactly the shared terms we're scoring on.
            status.Report("Scoring");
            var (_, vectors) = TfidfVectorizer.FitTransform(
                [resumeTokens, jdTokens], useIdf: false, sublinearTf: true);

            double score = Similarity.Cosine(vectors[0], vectors[1]);

            List<(string Skill, SectionType Section)> resumeSkills =
                [.. sectionNer.SelectMany(s => s.Ner.Skills.Select(skill => (skill, s.Type)))];

            // A GitHub profile link is evidence of GitHub even though NER can't see into URLs
            if (socials.GitHub is not null)
                resumeSkills.Add(("GitHub", SectionType.Contact));

            Dictionary<string, int> shownMonths = experience.SkillMonths
                .ToDictionary(s => s.Skill, s => s.Months, StringComparer.OrdinalIgnoreCase);

            SkillMatcher.YearsCheck? overallYears = requirements.OverallYears is { } years
                ? new SkillMatcher.YearsCheck(years * 12, experience.TotalMonths)
                : null;

            SkillMatcher.SkillReport skillReport = SkillMatcher.Match(
                resumeSkills, jdNer.Skills, skillTiers, requirements.SkillYears, shownMonths, overallYears);

            return new AnalysisResult(
                socials, sections, record, experience, requirements, resumeNer, score, skillReport);
        }
    }
}
