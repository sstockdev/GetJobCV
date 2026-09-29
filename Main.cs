using System.Text;
using GetJobCV.Modules;
using GetJobCV.UI;
using Microsoft.Web.WebView2.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using static GetJobCV.Modules.Analyzer;

namespace GetJobCV
{
    public partial class Main : Form
    {
        // Build on load; happens once
        private NerExtractor? _ner;

        // O*NET skill name to demand tier conversion
        private IReadOnlyDictionary<string, int> _skillTiers = new Dictionary<string, int>();

        // Skill to the more general skills it implies (PostgreSQL to SQL)
        private IReadOnlyDictionary<string, IReadOnlySet<string>> _skillParents = new Dictionary<string, IReadOnlySet<string>>();

        private readonly AppHeader _header = new();
        private readonly StartView _start = new();
        private readonly AnalyzingView _analyzing = new();
        private readonly ResultsView _results = new();

        // Where to go back to if a run fails or is cancelled
        private Control _inputs;

        // The run in progress, so Cancel can stop it and a new run can wait for it
        private CancellationTokenSource? _cancel;
        private Task _running = Task.CompletedTask;

        // The report on screen, for Export. Edits since the run aren't in it.
        private ReportInput? _report;

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
            _analyzing.CancelRequested += (_, _) =>
            {
                _cancel?.Cancel();
                ShowView(_inputs);
                _header.Status.Show(Pill.Kind.Ready, "Analysis cancelled");
            };
            _header.ExportReport.Click += (_, _) => ExportReport();
            _header.NewAnalysis.Click += (_, _) =>
            {
                _start.ResumePath = null;
                _start.JobDescription = "";
                ShowView(_start);
                // Don't carry an error from the last run over to a fresh start
                _header.Status.Show(Pill.Kind.Ready, "Ready");
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
            _header.ExportReport.Visible = view == _results;
        }

        /// <summary>
        /// Saves the report on screen as a PDF.
        /// </summary>
        private async void ExportReport()
        {
            if (_report is null)
                return;

            using SaveFileDialog dialog = new()
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"{Path.GetFileNameWithoutExtension(_report.ResumeFileName)} report.pdf",
                RestoreDirectory = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            string name = Path.GetFileName(dialog.FileName);
            _header.ExportReport.Enabled = false;
            _header.Status.Show(Pill.Kind.Working, $"Saving {name}");
            try
            {
                await ReportPdf.SaveAsync(this, ReportHtml.Build(_report), dialog.FileName);
                _header.Status.Show(Pill.Kind.Ready, $"Saved {name}");
            }
            catch (WebView2RuntimeNotFoundException)
            {
                _header.Status.Show(Pill.Kind.Error, "Saving a PDF needs the Microsoft Edge WebView2 Runtime");
            }
            catch (Exception ex)
            {
                _header.Status.Show(Pill.Kind.Error, $"Couldn't save {name} ({ex.Message})");
            }
            finally
            {
                _header.ExportReport.Enabled = true;
            }
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
                _skillParents = SkillsGazetteer.LoadParents();
                _ner = await NerExtractor.CreateAsync(
                    SkillsGazetteer.Load(), SkillsGazetteer.LoadCaseSensitive(), SkillsGazetteer.LoadAliases());
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
            if (!File.Exists(filePath))
            {
                _header.Status.Show(Pill.Kind.Error, $"Can't find {Path.GetFileName(filePath)}. It may have been moved or deleted");
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

            CancellationTokenSource cancel = new();
            _cancel = cancel;

            // A cancelled run only stops at its next step, and two runs mustn't share the
            // NER pipeline, so let the last one finish stopping first
            if (!_running.IsCompleted)
                await _running.ContinueWith(_ => { }, TaskScheduler.Default);

            // Progress<T> posts back to the UI thread so status updates actually paint.
            // A cancelled run's late updates are dropped.
            IProgress<string> status = new Progress<string>(s =>
            {
                if (cancel.IsCancellationRequested)
                    return;
                int step = _analyzing.Report(s);
                if (step > 0)
                    _header.Status.Show(Pill.Kind.Working, $"Analyzing · step {step} of {AnalyzingView.StageCount}");
            });

            try
            {
                Task<AnalysisResult?> run = Task.Run(() =>
                    Analyze(filePath, jobDescription, ner, _skillTiers, _skillParents, status, cancel.Token), cancel.Token);
                _running = run;
                AnalysisResult? result = await run;

                if (cancel.IsCancellationRequested)
                    return;
                if (result is null)
                {
                    Fail($"No text found in {Path.GetFileName(filePath)}. Scanned resumes aren't supported yet");
                    return;
                }

                _results.ShowResult(filePath, jobDescription, result.Record, result.Experience,
                    result.Sections, result.Skills, result.Score);
                _report = new ReportInput(Path.GetFileName(filePath), jobDescription, DateTime.Now,
                    result.Record, result.Experience, result.Sections, result.Skills, result.Score);
                ShowView(_results);
                _header.Status.Show(Pill.Kind.Ready, "Ready");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // Cancel already went back to the inputs
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    Fail($"Couldn't read {Path.GetFileName(filePath)} ({ex.Message})");
            }
            finally
            {
                if (_cancel == cancel)
                    _cancel = null;
                cancel.Dispose();
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
        /// Reads the PDF and runs the pipeline on its text. Runs off the UI thread,
        /// so it only touches the UI through <paramref name="status"/>.
        /// </summary>
        /// <param name="cancel">Checked between steps; a step already running finishes first</param>
        /// <returns>The result, or null if no text could be extracted</returns>
        private static AnalysisResult? Analyze(
            string filePath,
            string jobDescription,
            NerExtractor ner,
            IReadOnlyDictionary<string, int> skillTiers,
            IReadOnlyDictionary<string, IReadOnlySet<string>> skillParents,
            IProgress<string> status,
            CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
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

            return Analyzer.Analyze(resumeText, hyperlinks, jobDescription, ner, skillTiers, skillParents,
                DateOnly.FromDateTime(DateTime.Today), status, cancel);
        }
    }
}
