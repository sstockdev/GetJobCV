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
            StatusLabel.Text = "Loading NER model";
            _skillTiers = SkillsGazetteer.LoadWeights();
            _ner = await NerExtractor.CreateAsync(SkillsGazetteer.Load());
            StatusLabel.Text = "Ready";
        }

        private void SelectButton_Click(object sender, EventArgs e)
        {
            using OpenFileDialog openFileDialog = new();
            openFileDialog.InitialDirectory = "C:\\";
            openFileDialog.Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*";
            openFileDialog.FilterIndex = 0;
            openFileDialog.RestoreDirectory = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                if (String.IsNullOrWhiteSpace(JobDescriptionTextBox.Text))
                {
                    StatusLabel.Text = "Error: Job description was empty";
                    return;
                }

                StatusLabel.Text = "Extracting Text";
                string? filePath = openFileDialog.FileName;
                using PdfDocument document = PdfDocument.Open(filePath);
                string allText = "";
                List<string> hyperlinks = [];

                foreach (Page page in document.GetPages())
                {
                    allText += ContentOrderTextExtractor.GetText(page);

                    foreach (Annotation ann in page.GetAnnotations())
                    {
                        if (ann.Action is UriAction uri && !string.IsNullOrWhiteSpace(uri.Uri))
                            hyperlinks.Add(uri.Uri);
                    }
                }

                if (!String.IsNullOrWhiteSpace(allText))
                {
                    // Get socials
                    StatusLabel.Text = "Extracting Socials";
                    Socials socials = SocialExtractor.Extract(allText, hyperlinks);

                    // Run NER on text
                    StatusLabel.Text = "Running Named Entity Recognition";
                    NerResult ner = _ner?.Extract(allText) ?? NerResult.Empty;
                    NerResult jdNer = _ner?.Extract(JobDescriptionTextBox.Text) ?? NerResult.Empty;

                    // Run preprocessing on text
                    StatusLabel.Text = "Preprocessing Text";
                    string[] resumeTokens = PreProcessor.Preprocess(allText);
                    string[] jdTokens = PreProcessor.Preprocess(JobDescriptionTextBox.Text);

                    // Vectorize over one shared vocabulary
                    StatusLabel.Text = "Scoring";
                    var (_, vectors) = TfidfVectorizer.FitTransform([resumeTokens, jdTokens]);
                    double[] resumeVec = vectors[0];
                    double[] jdVec = vectors[1];

                    double score = Similarity.Cosine(resumeVec, jdVec);
                    double matchPct = score * 100.0;

                    SkillMatcher.SkillReport skillReport =
                        SkillMatcher.Match(ner.Skills, jdNer.Skills, _skillTiers);

                    double coveragePct = skillReport.WeightCoverage * 100.0;

                    ScoreCombiner.CombinedScore combined = ScoreCombiner.Combine(score, skillReport.WeightCoverage);

                    double overallPct = combined.Overall * 100.0;

                    // Done
                    StatusLabel.Text = "Ready";

                    ResultLabel.Text = $"Overall: {overallPct:F0}% - {combined.Verdict}";

                    DebugTextBox.Text =
                       $"GitHub: {socials.GitHub}\r\nLinkedIn: {socials.LinkedIn}\r\n\r\n" +
                       $"People: {string.Join(", ", ner.People)}\r\n" +
                       $"Orgs: {string.Join(", ", ner.Organizations)}\r\n" +
                       $"Locations: {string.Join(", ", ner.Locations)}\r\n\r\n" +
                       $"TF-IDF cosine match: {matchPct:F1}%\r\n" +
                       $"Weighted skill coverage: {coveragePct:F0}%\r\n\r\n" +
                       $"Matched ({skillReport.Matched.Count}): {FormatSkills(skillReport.Matched)}\r\n\r\n" +
                       $"MISSING ({skillReport.Missing.Count}): {FormatSkills(skillReport.Missing)}\r\n\r\n" +
                       $"Extra ({skillReport.Extra.Count}): {FormatSkills(skillReport.Extra)}\r\n\r\n" + 
                       $"Overall match: {overallPct:F0}% ({combined.Verdict})\r\n\r\n";
                }
                else
                {
                    StatusLabel.Text = "Error: Extracted text was null or invalid";
                }
            }
            else
            {
                StatusLabel.Text = "Error: Couldn't open PDF!";
            }
        }

        private static string FormatSkills(IReadOnlyList<SkillMatcher.ScoredSkill> skills)
        {
            if (skills.Count == 0) return "(none)";
            return string.Join(", ", skills.Select(s => s.Tier switch
            {
                2 => $"{s.Name} (hot)",
                1 => $"{s.Name} (in demand)",
                _ => s.Name
            }));
        }
    }
}
