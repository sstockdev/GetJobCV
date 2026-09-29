using System.Text;
using GetJobCV.Modules;
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

        public Main()
        {
            InitializeComponent();
            Load += Main_Load;
        }

        /// <summary>
        /// Loads models off the UI thread.
        /// </summary>
        private async void Main_Load(object? sender, EventArgs e)
        {
            // Nothing to score with until the NER model is ready
            SelectButton.Enabled = false;
            StatusLabel.Text = "Loading NER model";

            try
            {
                _skillTiers = SkillsGazetteer.LoadWeights();
                _ner = await NerExtractor.CreateAsync(
                    SkillsGazetteer.Load(), SkillsGazetteer.LoadCaseSensitive());
                StatusLabel.Text = "Ready";
                SelectButton.Enabled = true;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: Couldn't load NER model ({ex.Message})";
            }
        }

        private async void SelectButton_Click(object sender, EventArgs e)
        {
            if (_ner is null)
            {
                StatusLabel.Text = "Error: NER model is still loading";
                return;
            }

            if (String.IsNullOrWhiteSpace(JobDescriptionTextBox.Text))
            {
                StatusLabel.Text = "Error: Job description was empty";
                return;
            }

            using OpenFileDialog openFileDialog = new();
            openFileDialog.InitialDirectory = "C:\\";
            openFileDialog.Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*";
            openFileDialog.FilterIndex = 0;
            openFileDialog.RestoreDirectory = true;

            // User cancelled; not an error
            if (openFileDialog.ShowDialog() != DialogResult.OK)
                return;

            string filePath = openFileDialog.FileName;
            string jobDescription = JobDescriptionTextBox.Text;
            NerExtractor ner = _ner;

            // Progress<T> posts back to the UI thread so status updates actually paint
            IProgress<string> status = new Progress<string>(s => StatusLabel.Text = s);

            SelectButton.Enabled = false;
            try
            {
                AnalysisResult? result = await Task.Run(() =>
                    Analyze(filePath, jobDescription, ner, _skillTiers, status));

                if (result is null)
                {
                    StatusLabel.Text = "Error: Extracted text was null or invalid";
                    return;
                }

                double matchPct = result.Score * 100.0;
                double coveragePct = result.Skills.WeightCoverage * 100.0;

                ScoreCombiner.CombinedScore combined =
                    ScoreCombiner.Combine(result.Score, result.Skills.WeightCoverage);

                double overallPct = combined.Overall * 100.0;

                // Done
                StatusLabel.Text = "Ready";

                ResultLabel.Text = $"Overall: {overallPct:F0}% - {combined.Verdict}";

                DebugTextBox.Text =
                   FormatRecord(result.Record) +
                   FormatExperience(result.Experience) +
                   $"Sections: {FormatSections(result.Sections)}\r\n\r\n" +
                   $"People: {string.Join(", ", result.Ner.People)}\r\n" +
                   $"Orgs: {string.Join(", ", result.Ner.Organizations)}\r\n" +
                   $"Locations: {string.Join(", ", result.Ner.Locations)}\r\n\r\n" +
                   $"Cosine match: {matchPct:F1}%\r\n" +
                   $"Weighted skill coverage: {coveragePct:F0}%\r\n\r\n" +
                   $"Matched ({result.Skills.Matched.Count}): {FormatSkills(result.Skills.Matched)}\r\n\r\n" +
                   $"MISSING ({result.Skills.Missing.Count}): {FormatSkills(result.Skills.Missing)}\r\n\r\n" +
                   $"Extra ({result.Skills.Extra.Count}): {FormatSkills(result.Skills.Extra)}\r\n\r\n" +
                   $"Overall match: {overallPct:F0}% ({combined.Verdict})\r\n\r\n";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: Couldn't open PDF! ({ex.Message})";
            }
            finally
            {
                SelectButton.Enabled = true;
            }
        }

        /// <summary>
        /// Everything produced by one resume vs job description run.
        /// </summary>
        private sealed record AnalysisResult(
            Socials Socials,
            IReadOnlyList<ResumeSection> Sections,
            ResumeRecord Record,
            ExperienceSummary Experience,
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

            SkillMatcher.SkillReport skillReport =
                SkillMatcher.Match(resumeSkills, jdNer.Skills, skillTiers);

            return new AnalysisResult(socials, sections, record, experience, resumeNer, score, skillReport);
        }

        private static string FormatRecord(ResumeRecord record)
        {
            StringBuilder sb = new();
            ContactInfo c = record.Contact;
            sb.Append($"Name: {c.Name}\r\nEmail: {c.Email}\r\nPhone: {c.Phone}\r\n");
            sb.Append($"GitHub: {c.GitHub}\r\nLinkedIn: {c.LinkedIn}\r\n\r\n");

            sb.Append("Education:\r\n");
            foreach (EducationEntry e in record.Education)
                sb.Append($"  {e.Degree} - {e.School}" +
                    (e.Location is null ? "" : $" ({e.Location})") +
                    (e.Dates is null ? "" : $", {e.Dates.Text}") +
                    (e.Gpa is null ? "" : $", GPA {e.Gpa}") + "\r\n");

            sb.Append("Experience:\r\n");
            foreach (ExperienceEntry x in record.Experience)
                sb.Append($"  {x.Title} @ {x.Organization}" +
                    (x.Location is null ? "" : $" ({x.Location})") +
                    $", {x.Dates.Text}" +
                    (x.Section == SectionType.Experience ? "" : $" [{x.Section}]") + "\r\n");

            return sb.Append("\r\n").ToString();
        }

        private static string FormatExperience(ExperienceSummary experience)
        {
            string perSkill = experience.SkillMonths.Count == 0
                ? "(none)"
                : string.Join(", ", experience.SkillMonths.Select(s => $"{s.Skill} {s.Months / 12.0:0.#}y"));
            return $"Professional experience: {experience.TotalYears:0.#} years\r\n" +
                   $"By skill: {perSkill}\r\n\r\n";
        }

        private static string FormatSections(IReadOnlyList<ResumeSection> sections)
        {
            if (sections.Count == 0) return "(none)";
            return string.Join(", ", sections.Select(s => s.Type == SectionType.Contact || s.Heading.Equals(s.Type.ToString(), StringComparison.OrdinalIgnoreCase)
                ? s.Type.ToString()
                : $"{s.Type} (\"{s.Heading}\")"));
        }

        private static string FormatSkills(IReadOnlyList<SkillMatcher.ScoredSkill> skills)
        {
            if (skills.Count == 0) return "(none)";
            return string.Join(", ", skills.Select(s => s.Tier switch
            {
                2 => $"{s.Name} (hot)",
                1 => $"{s.Name} (in demand)",
                _ => s.Name
            } + WhereFound(s)));
        }

        /// <summary>
        /// Marks skills the resume doesn't show being used, since they count for less.
        /// </summary>
        private static string WhereFound(SkillMatcher.ScoredSkill skill) => skill.Section switch
        {
            null => "",
            SectionType.Skills => " [skills list only]",
            SectionType s when skill.Evidence < SkillMatcher.UsedEvidence => $" [{s}]",
            _ => ""
        };
    }
}
