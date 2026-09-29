using GetJobCV.Modules;

namespace GetJobCV.Tests
{
    /// <summary>
    /// Loads the real NER model once for every test in the class.
    /// </summary>
    public sealed class NerFixture
    {
        public NerExtractor Ner { get; }

        public NerFixture()
        {
            // The gazetteer is read from Resources/ relative to the working directory
            Environment.CurrentDirectory = AppContext.BaseDirectory;
            Ner = NerExtractor.CreateAsync(
                    SkillsGazetteer.Load(), SkillsGazetteer.LoadCaseSensitive(), SkillsGazetteer.LoadAliases())
                .GetAwaiter().GetResult();
        }
    }

    [Trait("Category", "Ner")]
    public class NerExtractorTests(NerFixture fixture) : IClassFixture<NerFixture>
    {
        private readonly NerExtractor _ner = fixture.Ner;

        [Theory]
        [InlineData("3+ years of C#.", "C#")]
        [InlineData("Strong C++, plus Python.", "C++")]
        [InlineData("We use C# and F#; also SQL.", "C#")]
        [InlineData("3+ years of JS.", "JavaScript")]
        [InlineData("Deploys to k8s.", "Kubernetes")]
        public void Keeps_symbol_skills_before_punctuation(string text, string skill)
        {
            IReadOnlyList<string> skills = _ner.Extract(text).Skills;
            Assert.Contains(skill, skills, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("C", skills, StringComparer.Ordinal);
        }

        [Fact]
        public void Reads_years_requirements_from_a_job_description()
        {
            JobRequirements r = RequirementExtractor.Extract(
                "Backend engineer. 5+ years of experience. 3+ years of C#. " +
                "Experience with SQL, Docker, Kubernetes, and Git. Python is a plus.",
                _ner);

            Assert.Equal(5, r.OverallYears);
            Assert.Equal(3, r.SkillYears["C#"]);
            Assert.False(r.SkillYears.ContainsKey("C"));
        }

        [Fact]
        public void Reads_nice_to_have_skills_from_a_job_description()
        {
            IReadOnlySet<string> preferred = QualificationSplitter.PreferredSkills(
                "Backend engineer. 5+ years of experience. 3+ years of C#. " +
                "Experience with SQL, Docker, Kubernetes, and Git. Python is a plus.",
                _ner);

            Assert.Equal(["Python"], preferred);
        }
    }
}
