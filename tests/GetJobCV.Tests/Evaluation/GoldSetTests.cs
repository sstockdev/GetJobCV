using System.Text.Json;
using GetJobCV.Modules;
using GetJobCV.Tests.Evaluation;
using Xunit.Abstractions;

namespace GetJobCV.Tests
{
    /// <summary>
    /// The hand-labeled resumes and jobs in Evaluation/, run through the whole pipeline once.
    /// </summary>
    public sealed class GoldSetFixture
    {
        // "Present" in a resume means this date, so years of experience don't drift
        public static readonly DateOnly Today = new(2026, 9, 1);

        public sealed record Pair(string Job, string Resume, Fit Fit, double Cosine, double Coverage, double Overall, string Verdict);

        public IReadOnlyDictionary<string, IReadOnlySet<string>> GoldJobSkills { get; }
        public IReadOnlyDictionary<string, IReadOnlySet<string>> GoldResumeSkills { get; }
        public IReadOnlyDictionary<string, IReadOnlySet<string>> FoundJobSkills { get; }
        public IReadOnlyDictionary<string, IReadOnlySet<string>> FoundResumeSkills { get; }
        public IReadOnlyList<Pair> Pairs { get; }

        /// <summary>
        /// Gold skill names the gazetteer doesn't know under any name, so it can never find them.
        /// </summary>
        public IReadOnlyList<string> UnknownGoldSkills { get; }

        private sealed record Labels(
            Dictionary<string, string[]> Jobs,
            Dictionary<string, string[]> Resumes,
            List<LabeledPair> Pairs);

        private sealed record LabeledPair(string Job, string Resume, string Fit);

        public GoldSetFixture()
        {
            NerExtractor ner = new NerFixture().Ner;
            string dir = Path.Combine(AppContext.BaseDirectory, "Evaluation");

            Labels labels = JsonSerializer.Deserialize<Labels>(
                File.ReadAllText(Path.Combine(dir, "labels.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            Func<string, string> canonical = Canonicalizer();
            IReadOnlySet<string> Normalize(IEnumerable<string> names) => names.Select(canonical).ToHashSet();

            GoldJobSkills = labels.Jobs.ToDictionary(j => j.Key, j => Normalize(j.Value));
            GoldResumeSkills = labels.Resumes.ToDictionary(r => r.Key, r => Normalize(r.Value));

            Dictionary<string, string> jobText = labels.Jobs.Keys.ToDictionary(
                id => id, id => File.ReadAllText(Path.Combine(dir, "jobs", id + ".txt")));
            Dictionary<string, string> resumeText = labels.Resumes.Keys.ToDictionary(
                id => id, id => File.ReadAllText(Path.Combine(dir, "resumes", id + ".txt")));

            IReadOnlyDictionary<string, int> tiers = SkillsGazetteer.LoadWeights();
            IReadOnlyDictionary<string, IReadOnlySet<string>> parents = SkillsGazetteer.LoadParents();

            // What the app sees: the job description the way it runs NER on it
            FoundJobSkills = jobText.ToDictionary(j => j.Key, j => Normalize(ner.Extract(j.Value).Skills));

            Dictionary<string, IReadOnlySet<string>> foundResume = [];
            List<Pair> pairs = [];
            foreach (LabeledPair p in labels.Pairs)
            {
                Analyzer.AnalysisResult result = Analyzer.Analyze(
                    resumeText[p.Resume], [], jobText[p.Job], ner, tiers, parents, Today);
                ScoreCombiner.CombinedScore combined = ScoreCombiner.Combine(result.Score, result.Skills.WeightCoverage);

                foundResume[p.Resume] = Normalize(result.Ner.Skills);
                pairs.Add(new Pair(p.Job, p.Resume, Enum.Parse<Fit>(p.Fit, ignoreCase: true),
                    result.Score, result.Skills.WeightCoverage, combined.Overall, combined.Verdict));
            }
            FoundResumeSkills = foundResume;
            Pairs = pairs;

            HashSet<string> known = [.. SkillsGazetteer.Load().Concat(SkillsGazetteer.LoadCaseSensitive())
                .Concat(SkillsGazetteer.LoadAliases().Values).Select(canonical)];
            UnknownGoldSkills = [.. GoldJobSkills.Values.Concat(GoldResumeSkills.Values)
                .SelectMany(s => s).Where(s => !known.Contains(s)).Distinct().Order()];
        }

        /// <summary>
        /// Maps a skill name to one key, so an alias in the labels ("ASP.NET Core")
        /// matches the canonical name NER reports ("ASP.NET").
        /// </summary>
        private static Func<string, string> Canonicalizer()
        {
            Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (alias, name) in SkillsGazetteer.LoadAliases())
                aliases.TryAdd(alias, name);
            return name => (aliases.TryGetValue(name, out string? c) ? c : name).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Measures the pipeline against the gold set and fails if it gets worse. Each test
    /// prints its full report, so run with a detailed logger to see it:
    /// <c>dotnet test --filter Category=Evaluation --logger "console;verbosity=detailed"</c>.
    /// When a change improves a number, raise its floor to lock the gain in.
    /// </summary>
    [Trait("Category", "Evaluation")]
    public class GoldSetTests(GoldSetFixture gold, ITestOutputHelper output) : IClassFixture<GoldSetFixture>
    {
        // Floors sit just under the measured values, so only a real regression fails
        private const double JobSkillF1Floor = 0.95;
        private const double ResumeSkillF1Floor = 0.92;
        private const double ConcordanceFloor = 0.95;
        private const double GoodVsNoAucFloor = 0.95;
        private const double VerdictAgreementFloor = 0.90;

        [Fact]
        public void Labels_reference_every_document_and_only_known_skills()
        {
            Assert.All(gold.Pairs, p => Assert.Contains(p.Job, gold.GoldJobSkills.Keys));
            Assert.All(gold.Pairs, p => Assert.Contains(p.Resume, gold.GoldResumeSkills.Keys));
            Assert.All(gold.GoldResumeSkills.Keys, r => Assert.Contains(gold.Pairs, p => p.Resume == r));

            // A gold skill the gazetteer can't name is a gazetteer gap, not a labeling error,
            // but list them so a typo doesn't pass as one
            output.WriteLine($"Gold skills the gazetteer doesn't know ({gold.UnknownGoldSkills.Count}):");
            foreach (string s in gold.UnknownGoldSkills)
                output.WriteLine("  " + s);
        }

        [Fact]
        public void Job_description_skill_extraction()
        {
            SetScore total = Report("Job descriptions", gold.GoldJobSkills, gold.FoundJobSkills);
            Assert.True(total.F1 >= JobSkillF1Floor, $"Job skill F1 {total.F1:P1} fell below {JobSkillF1Floor:P1}");
        }

        [Fact]
        public void Resume_skill_extraction()
        {
            SetScore total = Report("Resumes", gold.GoldResumeSkills, gold.FoundResumeSkills);
            Assert.True(total.F1 >= ResumeSkillF1Floor, $"Resume skill F1 {total.F1:P1} fell below {ResumeSkillF1Floor:P1}");
        }

        [Fact]
        public void Fit_ranking()
        {
            double concordance = EvalMetrics.Concordance(gold.Pairs.Select(p => (p.Job, p.Fit, p.Overall)));
            double auc = EvalMetrics.Auc(
                gold.Pairs.Where(p => p.Fit == Fit.Good).Select(p => p.Overall),
                gold.Pairs.Where(p => p.Fit == Fit.No).Select(p => p.Overall));
            double ndcg = gold.Pairs.GroupBy(p => p.Job).Average(g => EvalMetrics.Ndcg(g.Select(p => (p.Fit, p.Overall))));
            int verdictsRight = gold.Pairs.Count(p => VerdictFit(p.Verdict) == p.Fit);

            output.WriteLine($"{"Job",-18} {"Resume",-18} {"Label",-10} {"Cosine",7} {"Coverage",9} {"Overall",8}  Verdict");
            foreach (var p in gold.Pairs.OrderBy(p => p.Job).ThenByDescending(p => p.Overall))
                output.WriteLine($"{p.Job,-18} {p.Resume,-18} {p.Fit,-10} {p.Cosine,7:P0} {p.Coverage,9:P0} {p.Overall,8:P0}  {p.Verdict}");
            output.WriteLine("");
            foreach (var f in gold.Pairs.GroupBy(p => p.Fit).OrderByDescending(g => g.Key))
                output.WriteLine($"Mean overall, {f.Key}: {f.Average(p => p.Overall):P0}");
            output.WriteLine($"Within-job concordance: {concordance:P1} (cosine alone {Concordance(p => p.Cosine):P1}, coverage alone {Concordance(p => p.Coverage):P1})");
            output.WriteLine($"Good vs No AUC: {auc:P1}");
            output.WriteLine($"Mean NDCG per job: {ndcg:P1}");
            output.WriteLine($"Verdict matches label: {verdictsRight}/{gold.Pairs.Count}");

            Assert.True(concordance >= ConcordanceFloor, $"Concordance {concordance:P1} fell below {ConcordanceFloor:P1}");
            Assert.True(auc >= GoodVsNoAucFloor, $"Good vs No AUC {auc:P1} fell below {GoodVsNoAucFloor:P1}");
            double agreement = (double)verdictsRight / gold.Pairs.Count;
            Assert.True(agreement >= VerdictAgreementFloor, $"Verdict agreement {agreement:P1} fell below {VerdictAgreementFloor:P1}");
        }

        private double Concordance(Func<GoldSetFixture.Pair, double> score) =>
            EvalMetrics.Concordance(gold.Pairs.Select(p => (p.Job, p.Fit, score(p))));

        /// <summary>
        /// The label a verdict claims. "Needs improvement!" and "Awful" both say no fit.
        /// </summary>
        private static Fit VerdictFit(string verdict) =>
            verdict == ScoreCombiner.Verdict(1) ? Fit.Good
            : verdict == ScoreCombiner.Verdict(0.5) ? Fit.Potential
            : Fit.No;

        private SetScore Report(
            string title,
            IReadOnlyDictionary<string, IReadOnlySet<string>> goldSkills,
            IReadOnlyDictionary<string, IReadOnlySet<string>> found)
        {
            SetScore total = default;
            foreach (var (id, expected) in goldSkills.OrderBy(d => d.Key))
            {
                IReadOnlySet<string> got = found[id];
                SetScore s = SetScore.Compare(expected, got);
                total += s;
                output.WriteLine($"{id}: {s}");
                output.WriteLine($"  missed: {string.Join(", ", expected.Where(e => !got.Contains(e)).Order())}");
                output.WriteLine($"  extra:  {string.Join(", ", got.Where(g => !expected.Contains(g)).Order())}");
            }
            output.WriteLine("");
            output.WriteLine($"{title}: {total}");
            return total;
        }
    }
}
