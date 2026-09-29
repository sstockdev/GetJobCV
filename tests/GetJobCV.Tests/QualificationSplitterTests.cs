using System.Text.RegularExpressions;
using GetJobCV.Modules;

namespace GetJobCV.Tests
{
    public class QualificationSplitterTests
    {
        private static readonly string[] Known = ["Python", "SQL", "Go", "Rust", "Docker", "Kubernetes", "AWS", "C#", "Terraform"];

        /// <summary>
        /// Stands in for NER: each known skill that appears in the clause as a word.
        /// </summary>
        private static IEnumerable<string> Find(string clause) =>
            Known.Where(s => Regex.IsMatch(clause,
                @"(?<![\p{L}\p{N}])" + Regex.Escape(s) + @"(?![\p{L}\p{N}#+])", RegexOptions.IgnoreCase));

        private static IReadOnlySet<string> Preferred(string jd) => QualificationSplitter.PreferredSkills(jd, Find);

        [Theory]
        [InlineData("Experience with SQL and Docker. Python is a plus.")]
        [InlineData("Experience with SQL and Docker. Bonus: Python.")]
        [InlineData("Experience with SQL and Docker. Python preferred.")]
        [InlineData("Experience with SQL and Docker. Ideally some Python.")]
        [InlineData("Experience with SQL and Docker; nice to have: Python")]
        [InlineData("Experience with SQL and Docker. Python experience is not required but helps.")]
        public void Finds_an_inline_nice_to_have(string jd)
        {
            Assert.Equal(["Python"], Preferred(jd));
        }

        [Fact]
        public void Uses_a_preferred_heading_until_the_next_heading()
        {
            IReadOnlySet<string> preferred = Preferred("""
                Requirements
                - 3+ years of Python
                - SQL
                Preferred Qualifications
                - Go or Rust
                - Familiarity with:
                - Kubernetes
                Responsibilities
                - Deploy with Terraform
                """);

            Assert.Equal(new HashSet<string>(["Go", "Rust", "Kubernetes"]), preferred.ToHashSet());
        }

        [Fact]
        public void Keeps_a_lead_in_line_inside_the_preferred_section()
        {
            IReadOnlySet<string> preferred = Preferred("Nice to have:\nFamiliarity with:\nAWS and Docker");
            Assert.Equal(new HashSet<string>(["AWS", "Docker"]), preferred.ToHashSet());
        }

        [Fact]
        public void The_nearest_cue_decides_in_a_mixed_sentence()
        {
            Assert.Equal(["Go"], Preferred("Python required, Go preferred"));
        }

        [Fact]
        public void A_skill_that_is_also_required_stays_required()
        {
            Assert.Empty(Preferred("Must know Python.\nNice to have:\n- Python 3.12 features"));
        }

        [Fact]
        public void Plain_requirements_are_not_preferred()
        {
            Assert.Empty(Preferred("Backend engineer. 5+ years of experience. 3+ years of C#. Experience with SQL, Docker, and Kubernetes."));
        }

        [Fact]
        public void A_sentence_ending_in_a_heading_word_is_not_a_heading()
        {
            // Read as a heading, "Strong Python skills" would be skipped and Python left preferred
            Assert.Empty(Preferred("Nice to have\n- Python\nRequirements\nStrong Python skills"));
        }

        [Fact]
        public void A_blank_line_after_the_section_ends_it()
        {
            Assert.Equal(["Go"], Preferred("Nice to have\n\n- Go\n\nStrong Python skills and SQL experience"));
        }
    }

    public class SkillMatcherPreferredTests
    {
        private static readonly Dictionary<string, int> NoTiers = [];

        [Fact]
        public void A_missing_nice_to_have_costs_less_than_a_missing_requirement()
        {
            (string, SectionType)[] resume = [("SQL", SectionType.Experience)];
            string[] job = ["SQL", "Python"];

            double required = SkillMatcher.Match(resume, job, NoTiers).WeightCoverage;
            double nice = SkillMatcher.Match(resume, job, NoTiers,
                preferred: new HashSet<string>(["python"])).WeightCoverage;

            // SQL weight 1 covered; Python weight 1, or 0.5 when only nice to have
            Assert.Equal(1.0 / 2.0, required, 6);
            Assert.Equal(1.0 / 1.5, nice, 6);
        }

        [Fact]
        public void Lists_required_skills_before_nice_to_haves()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match([], ["Go", "Python", "SQL"],
                new Dictionary<string, int> { ["Python"] = 2 },
                preferred: new HashSet<string>(["Python"]));

            Assert.Equal(["Go", "SQL", "Python"], report.Missing.Select(s => s.Name));
            Assert.True(report.Missing[2].Preferred);
        }

        [Fact]
        public void Extra_skills_are_never_marked_nice_to_have()
        {
            SkillMatcher.SkillReport report = SkillMatcher.Match([("Python", SectionType.Projects)], ["SQL"], NoTiers,
                preferred: new HashSet<string>(["Python"]));

            Assert.False(Assert.Single(report.Extra).Preferred);
        }
    }
}
