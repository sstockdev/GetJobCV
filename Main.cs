using GetJobATS.Modules;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using static GetJobATS.Modules.NerExtractor;

namespace GetJobATS
{
    public partial class Main : Form
    {
        // Build on load; happens once
        private NerExtractor? _ner;
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

                    // Run preprocessing on extracted text
                    StatusLabel.Text = "Preprocessing Extracted Text";
                    string preprocessed = PreProcessor.PreprocessToString(allText);

                    // Done
                    StatusLabel.Text = "Ready";
                    DebugTextBox.Text =
                       $"GitHub: {socials.GitHub}\r\nLinkedIn: {socials.LinkedIn}\r\n\r\n" +
                       $"People: {string.Join(", ", ner.People)}\r\n" +
                       $"Orgs: {string.Join(", ", ner.Organizations)}\r\n" +
                       $"Locations: {string.Join(", ", ner.Locations)}\r\n" +
                       $"Skills: {string.Join(", ", ner.Skills)}\r\n\r\n" +
                       $"{preprocessed}";
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
    }
}
