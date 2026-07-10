using UglyToad.PdfPig;
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
                foreach (Page page in document.GetPages())
                {
                    string text = ContentOrderTextExtractor.GetText(page);
                    allText += text;
                }

                if (!String.IsNullOrWhiteSpace(allText))
                {
                    StatusLabel.Text = "Processing Text";

                }
            }
            else
            {
                StatusLabel.Text = "Couldn't open PDF!";
            }
        }
    }
}
