using System.Text;
using GetJobCV.Modules;
using GetJobCV.Tests.Evaluation;
using Xunit.Abstractions;

namespace GetJobCV.Tests
{
    /// <summary>
    /// Scores the pipeline on the resume/job fit dataset at
    /// https://huggingface.co/datasets/cnamuangtoun/resume-job-description-fit
    /// (about 1,800 test pairs over 71 job descriptions, each labeled No, Potential, or Good Fit).
    /// The dataset has no license, so it isn't in the repo: download test.csv and set
    /// GETJOBCV_FIT_CSV to its path. GETJOBCV_FIT_LIMIT caps the rows for a quick run.
    /// Without GETJOBCV_FIT_CSV the test does nothing.
    /// <c>dotnet test --filter Category=Benchmark --logger "console;verbosity=detailed"</c>
    /// </summary>
    [Trait("Category", "Benchmark")]
    public class FitDatasetBenchmark(NerFixture fixture, ITestOutputHelper output) : IClassFixture<NerFixture>
    {
        private sealed record Scored(string Job, Fit Fit, double Cosine, double Coverage, double Overall);

        [Fact]
        public void Resume_job_fit_dataset()
        {
            string? path = Environment.GetEnvironmentVariable("GETJOBCV_FIT_CSV");
            if (string.IsNullOrEmpty(path))
            {
                output.WriteLine("GETJOBCV_FIT_CSV isn't set, so the benchmark was skipped.");
                return;
            }
            int limit = int.TryParse(Environment.GetEnvironmentVariable("GETJOBCV_FIT_LIMIT"), out int n) ? n : int.MaxValue;

            IReadOnlyDictionary<string, int> tiers = SkillsGazetteer.LoadWeights();
            IReadOnlyDictionary<string, IReadOnlySet<string>> parents = SkillsGazetteer.LoadParents();

            List<Scored> scored = [];
            foreach (string[] row in ReadCsv(File.ReadAllText(path)).Skip(1).Take(limit))
            {
                (string resume, string job, string label) = (row[0], row[1], row[2]);
                Analyzer.AnalysisResult result = Analyzer.Analyze(
                    resume, [], job, fixture.Ner, tiers, parents, GoldSetFixture.Today);
                double overall = ScoreCombiner.Combine(result.Score, result.Skills.WeightCoverage).Overall;
                scored.Add(new Scored(job, ParseLabel(label), result.Score, result.Skills.WeightCoverage, overall));
            }

            output.WriteLine($"{scored.Count} pairs over {scored.Select(s => s.Job).Distinct().Count()} job descriptions");
            foreach (var f in scored.GroupBy(s => s.Fit).OrderByDescending(g => g.Key))
                output.WriteLine($"  {f.Key,-9} {f.Count(),5} pairs, mean overall {f.Average(s => s.Overall):P1}");
            output.WriteLine("");
            output.WriteLine($"{"Signal",-10} {"Concordance",12} {"Good/No AUC",12} {"NDCG",8}");
            foreach (var (name, score) in new (string, Func<Scored, double>)[]
                     { ("Cosine", s => s.Cosine), ("Coverage", s => s.Coverage), ("Overall", s => s.Overall) })
            {
                double concordance = EvalMetrics.Concordance(scored.Select(s => (s.Job, s.Fit, score(s))));
                double auc = EvalMetrics.Auc(
                    scored.Where(s => s.Fit == Fit.Good).Select(score), scored.Where(s => s.Fit == Fit.No).Select(score));
                double ndcg = scored.GroupBy(s => s.Job)
                    .Select(g => EvalMetrics.Ndcg(g.Select(s => (s.Fit, score(s)))))
                    .Where(v => !double.IsNaN(v)).Average();
                output.WriteLine($"{name,-10} {concordance,12:P1} {auc,12:P1} {ndcg,8:P1}");
            }
        }

        private static Fit ParseLabel(string label) => label.Trim() switch
        {
            "Good Fit" => Fit.Good,
            "Potential Fit" => Fit.Potential,
            "No Fit" => Fit.No,
            _ => throw new FormatException($"Unknown label '{label}'"),
        };

        /// <summary>
        /// Splits RFC 4180 CSV: quoted fields may hold commas, newlines, and doubled quotes.
        /// </summary>
        private static IEnumerable<string[]> ReadCsv(string text)
        {
            List<string> row = [];
            StringBuilder field = new();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c != '"')
                        field.Append(c);
                    else if (i + 1 < text.Length && text[i + 1] == '"')
                        field.Append(text[++i]);
                    else
                        quoted = false;
                }
                else if (c == '"')
                    quoted = true;
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n')
                {
                    row.Add(field.ToString().TrimEnd('\r'));
                    field.Clear();
                    yield return [.. row];
                    row.Clear();
                }
                else
                    field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                yield return [.. row];
            }
        }
    }
}
