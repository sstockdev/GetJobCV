using GetJobATS.Modules;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace GetJobATS
{
    public partial class Main : Form
    {
        public Main()
        {
            InitializeComponent();
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
                    StatusLabel.Text = "Extracting Socials";
                    Socials socials = SocialExtractor.Extract(allText, hyperlinks);
                    StatusLabel.Text = "Preprocessing Extracted Text";
                    string preprocessed = PreProcessor.PreprocessToString(allText);
                    StatusLabel.Text = "Ready";
                    DebugTextBox.Text =
        $"GitHub: {socials.GitHub}\r\nLinkedIn: {socials.LinkedIn}\r\n\r\n{preprocessed}";
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
