using GetJobCV.Modules;

namespace GetJobCV.Tests
{
    /// <summary>
    /// The bundled overlay's aliases, as loaded from Resources/.
    /// </summary>
    public class SkillsGazetteerAliasTests
    {
        public SkillsGazetteerAliasTests() => Environment.CurrentDirectory = AppContext.BaseDirectory;

        [Theory]
        [InlineData("JS", "JavaScript")]
        [InlineData("k8s", "Kubernetes")]
        [InlineData("golang", "Go")]
        [InlineData("Postgres", "PostgreSQL")]
        [InlineData("Amazon Web Services AWS software", "AWS")]
        [InlineData("ML", "Machine Learning")]
        public void Maps_an_alias_to_its_canonical_name(string alias, string canonical)
        {
            Assert.Equal(canonical, SkillsGazetteer.LoadAliases()[alias]);
        }

        [Theory]
        [InlineData("AWS")]
        [InlineData("SQL")]
        [InlineData("Java")]
        [InlineData("Kafka")]
        [InlineData("Jira")]
        public void A_canonical_name_takes_the_tier_of_its_O_NET_alias(string canonical)
        {
            Assert.Equal(2, SkillsGazetteer.LoadWeights()[canonical]);
        }

        [Fact]
        public void An_alias_line_does_not_make_its_canonical_name_spotted()
        {
            // "golang<TAB>Go" must not add a case-insensitive "Go"; only "=Go" spots it, exactly
            Assert.DoesNotContain("Go", SkillsGazetteer.Load(), StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Go", SkillsGazetteer.LoadCaseSensitive());
            Assert.Contains("golang", SkillsGazetteer.Load(), StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Aliases_point_at_names_that_are_not_themselves_aliases()
        {
            IReadOnlyDictionary<string, string> aliases = SkillsGazetteer.LoadAliases();
            Assert.All(aliases, pair => Assert.False(
                aliases.TryGetValue(pair.Value, out string? next) && !next.Equals(pair.Value, StringComparison.OrdinalIgnoreCase),
                $"{pair.Key} → {pair.Value} → {next}"));
        }
    }

    [Trait("Category", "Ner")]
    public class NerAliasTests(NerFixture fixture) : IClassFixture<NerFixture>
    {
        private readonly NerExtractor _ner = fixture.Ner;

        [Theory]
        [InlineData("Built dashboards in JS and React.js", "JavaScript", "React")]
        [InlineData("Deployed to k8s on Amazon Web Services", "Kubernetes", "AWS")]
        [InlineData("Wrote services in golang backed by Postgres", "Go", "PostgreSQL")]
        public void Reports_aliases_under_their_canonical_names(string text, string first, string second)
        {
            IReadOnlyList<string> skills = _ner.Extract(text).Skills;
            Assert.Contains(first, skills);
            Assert.Contains(second, skills);
        }

        [Fact]
        public void Keeps_how_each_skill_was_written()
        {
            NerExtractor.SkillMention mention = Assert.Single(
                _ner.Extract("3+ years of JS").SkillMentions!, m => m.Name == "JavaScript");
            Assert.Equal("JS", mention.Text);
        }

        [Fact]
        public void Lowercase_go_is_not_a_skill()
        {
            Assert.DoesNotContain("Go", _ner.Extract("Ready to go the extra mile").Skills, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Matches_a_resume_alias_against_a_job_description_name()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match(
                _ner.Extract("Built APIs in JS on k8s").Skills.Select(s => (s, SectionType.Experience)),
                _ner.Extract("Experience with JavaScript and Kubernetes").Skills,
                SkillsGazetteer.LoadWeights());

            Assert.Equal(["JavaScript", "Kubernetes"], report.Matched.Select(s => s.Name).Order());
            Assert.Empty(report.Missing);
        }

        [Fact]
        public void Reads_a_years_requirement_written_with_an_alias()
        {
            JobRequirements r = RequirementExtractor.Extract("3+ years of JS. Postgres is a plus.", _ner);
            Assert.Equal(3, r.SkillYears["JavaScript"]);

            IReadOnlySet<string> preferred = QualificationSplitter.PreferredSkills("3+ years of JS. Postgres is a plus.", _ner);
            Assert.Equal(["PostgreSQL"], preferred);
        }
    }
}
