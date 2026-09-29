using GetJobCV.Modules;
using GetJobCV.UI;

namespace GetJobCV.Tests
{
    /// <summary>
    /// The bundled overlay's "Specific > General" lines, as loaded from Resources/.
    /// </summary>
    public class SkillsGazetteerHierarchyTests
    {
        public SkillsGazetteerHierarchyTests() => Environment.CurrentDirectory = AppContext.BaseDirectory;

        [Theory]
        [InlineData("PostgreSQL", "SQL")]
        [InlineData("GitHub", "Git")]
        [InlineData("TypeScript", "JavaScript")]
        [InlineData("Scrum", "Agile")]
        [InlineData("Datadog", "Monitoring")]
        public void Maps_a_skill_to_the_general_skill_it_implies(string specific, string general)
        {
            Assert.Contains(general, SkillsGazetteer.LoadParents()[specific]);
        }

        [Theory]
        [InlineData("Next.js", "JavaScript")]
        [InlineData("Spring Boot", "Java")]
        [InlineData("GitHub", "version control")]
        [InlineData("PyTorch", "Machine Learning")]
        [InlineData("Blazor", ".NET")]
        public void Follows_chains(string specific, string general)
        {
            Assert.Contains(general, SkillsGazetteer.LoadParents()[specific]);
        }

        [Fact]
        public void Does_not_imply_the_other_way()
        {
            Assert.False(SkillsGazetteer.LoadParents().ContainsKey("SQL"));
        }

        [Fact]
        public void Hierarchy_lines_are_not_spotted_as_skills_or_aliases()
        {
            Assert.DoesNotContain(SkillsGazetteer.Load(), n => n.Contains(SkillsGazetteer.HierarchySeparator));
            Assert.DoesNotContain(SkillsGazetteer.LoadCaseSensitive(), n => n.Contains(SkillsGazetteer.HierarchySeparator));
            Assert.DoesNotContain(SkillsGazetteer.LoadAliases(), p => p.Key.Contains(SkillsGazetteer.HierarchySeparator));
        }

        [Fact]
        public void Every_name_in_the_hierarchy_is_a_skill_that_can_be_reported()
        {
            // A typo'd or alias name would never match what NER reports
            HashSet<string> known = new(SkillsGazetteer.Load(), StringComparer.OrdinalIgnoreCase);
            known.UnionWith(SkillsGazetteer.LoadCaseSensitive());
            known.UnionWith(SkillsGazetteer.LoadAliases().Values);
            IReadOnlyDictionary<string, string> aliases = SkillsGazetteer.LoadAliases();

            foreach (var (specific, generals) in SkillsGazetteer.LoadParents())
                foreach (string name in generals.Prepend(specific))
                {
                    Assert.True(known.Contains(name), $"{name} isn't in the gazetteer");
                    Assert.False(aliases.TryGetValue(name, out string? canonical) && !canonical.Equals(name, StringComparison.OrdinalIgnoreCase),
                        $"{name} is an alias of {canonical}");
                }
        }
    }

    [Trait("Category", "Ner")]
    public class NerHierarchyTests(NerFixture fixture) : IClassFixture<NerFixture>
    {
        private readonly NerExtractor _ner = fixture.Ner;

        [Fact]
        public void Covers_general_job_skills_with_specific_resume_skills()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match(
                _ner.Extract("Built reporting on Postgres and shipped a TypeScript frontend").Skills.Select(s => (s, SectionType.Experience)),
                _ner.Extract("Experience with SQL and JavaScript").Skills,
                SkillsGazetteer.LoadWeights(),
                parents: SkillsGazetteer.LoadParents());

            Assert.Equal(["JavaScript", "SQL"], report.Matched.Select(s => s.Name).Order(StringComparer.OrdinalIgnoreCase));
            Assert.Empty(report.Missing);
        }
    }

    public class SkillMatcherHierarchyTests
    {
        private static readonly Dictionary<string, int> NoTiers = [];

        private static readonly Dictionary<string, IReadOnlySet<string>> Parents = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PostgreSQL"] = new HashSet<string> { "SQL" },
            ["MySQL"] = new HashSet<string> { "SQL" },
            ["Next.js"] = new HashSet<string> { "React", "JavaScript" },
        };

        [Fact]
        public void A_specific_skill_covers_the_general_one_the_job_asks_for()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match(
                [("PostgreSQL", SectionType.Experience)], ["SQL"], NoTiers, parents: Parents);

            SkillMatcher.ScoredSkill sql = Assert.Single(report.Matched);
            Assert.Equal("SQL", sql.Name);
            Assert.Equal(["PostgreSQL"], sql.ImpliedBy!);
            Assert.Equal(SkillMatcher.UsedEvidence, sql.Evidence);
            Assert.Empty(report.Missing);
            Assert.Equal(1.0, report.WeightCoverage);
        }

        [Fact]
        public void The_skill_that_covered_it_is_not_extra()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match(
                [("PostgreSQL", SectionType.Experience), ("Docker", SectionType.Experience)], ["SQL"], NoTiers, parents: Parents);

            Assert.Equal(["Docker"], report.Extra.Select(s => s.Name));
        }

        [Fact]
        public void A_general_skill_does_not_cover_a_specific_one()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match(
                [("SQL", SectionType.Experience)], ["PostgreSQL"], NoTiers, parents: Parents);

            Assert.Equal(["PostgreSQL"], report.Missing.Select(s => s.Name));
        }

        [Fact]
        public void Keeps_the_stronger_of_the_name_and_the_specific_skill()
        {
            // SQL is only listed, but PostgreSQL is shown in use
            SkillMatcher.ScoredSkill used = Assert.Single(SkillMatcher.Match(
                [("SQL", SectionType.Skills), ("PostgreSQL", SectionType.Experience)], ["SQL"], NoTiers, parents: Parents).Matched);
            Assert.Equal(SectionType.Experience, used.Section);
            Assert.Equal(["PostgreSQL"], used.ImpliedBy!);

            // By name wins a tie, so the report doesn't credit something else
            SkillMatcher.ScoredSkill named = Assert.Single(SkillMatcher.Match(
                [("SQL", SectionType.Experience), ("PostgreSQL", SectionType.Experience)], ["SQL"], NoTiers, parents: Parents).Matched);
            Assert.Null(named.ImpliedBy);
        }

        [Fact]
        public void Takes_the_strongest_specific_skill()
        {
            SkillMatcher.ScoredSkill sql = Assert.Single(SkillMatcher.Match(
                [("MySQL", SectionType.Skills), ("PostgreSQL", SectionType.Projects)], ["SQL"], NoTiers, parents: Parents).Matched);
            Assert.Equal(["PostgreSQL", "MySQL"], sql.ImpliedBy!);
            Assert.Equal(SectionType.Projects, sql.Section);
        }

        [Fact]
        public void No_skill_that_covered_it_is_extra()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match(
                [("MySQL", SectionType.Skills), ("PostgreSQL", SectionType.Skills), ("Docker", SectionType.Skills)],
                ["SQL"], NoTiers, parents: Parents);

            Assert.Equal(["MySQL", "PostgreSQL"], Assert.Single(report.Matched).ImpliedBy!);
            Assert.Equal(["Docker"], report.Extra.Select(s => s.Name));
        }

        [Fact]
        public void Skills_that_imply_a_skill_matched_by_name_are_still_extra()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match(
                [("SQL", SectionType.Experience), ("PostgreSQL", SectionType.Skills)], ["SQL"], NoTiers, parents: Parents);

            Assert.Null(Assert.Single(report.Matched).ImpliedBy);
            Assert.Equal(["PostgreSQL"], report.Extra.Select(s => s.Name));
        }

        [Fact]
        public void Years_shown_with_a_specific_skill_count_for_the_general_one()
        {
            SkillMatcher.ScoredSkill sql = Assert.Single(SkillMatcher.Match(
                [("PostgreSQL", SectionType.Experience), ("MySQL", SectionType.Experience)], ["SQL"], NoTiers,
                requiredYears: new Dictionary<string, int> { ["SQL"] = 3 },
                shownMonths: new Dictionary<string, int> { ["PostgreSQL"] = 40, ["MySQL"] = 12 },
                parents: Parents).Matched);

            Assert.Equal(40, sql.ShownMonths);
            Assert.Equal(1.0, sql.YearsFactor);
        }

        [Fact]
        public void Covers_an_option_in_a_list_where_any_one_counts()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match(
                [("Next.js", SectionType.Experience)], ["React", "Vue.js"], NoTiers,
                alternatives: [new HashSet<string> { "React", "Vue.js" }], parents: Parents);

            Assert.Equal(["React"], report.Matched.Select(s => s.Name));
            Assert.Empty(report.Missing);
            Assert.Equal(1.0, report.WeightCoverage);
        }

        [Fact]
        public void The_report_names_the_skill_that_covered_it()
        {
            SkillMatcher.ScoredSkill sql = Assert.Single(SkillMatcher.Match(
                [("PostgreSQL", SectionType.Skills)], ["SQL"], NoTiers, parents: Parents).Matched);

            Assert.Equal("only in the Skills list, counts half, through PostgreSQL", ReportText.Evidence(sql));
            Assert.Equal("via PostgreSQL, skills list", ReportText.Tag(sql));
        }

        [Fact]
        public void The_report_names_every_skill_that_covered_it()
        {
            SkillMatcher.ScoredSkill sql = Assert.Single(SkillMatcher.Match(
                [("PostgreSQL", SectionType.Skills), ("MySQL", SectionType.Skills)], ["SQL"], NoTiers, parents: Parents).Matched);

            Assert.Equal("only in the Skills list, counts half, through MySQL, PostgreSQL", ReportText.Evidence(sql));
            Assert.Equal("via MySQL +1, skills list", ReportText.Tag(sql));
        }
    }
}
