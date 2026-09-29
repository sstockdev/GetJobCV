using System.Text.RegularExpressions;
using GetJobCV.Modules;
using static GetJobCV.Modules.SkillMatcher;

namespace GetJobCV.Tests
{
    public class AlternativeGroupsTests
    {
        private static readonly string[] Known =
        [
            "Machine Learning", "Distributed Systems", "Quantum Computing", "Game Development", "Mobile Development",
            "Embedded Systems", "Python", "Java", "Go", "SQL", "Docker", "Git", "AWS", "Azure", "Google Cloud", "agile",
        ];

        private static IEnumerable<string> Find(string clause) =>
            Known.Where(s => Regex.IsMatch(clause,
                @"(?<![\p{L}\p{N}])" + Regex.Escape(s) + @"(?![\p{L}\p{N}#+])", RegexOptions.IgnoreCase));

        private static List<HashSet<string>> Groups(string jd) =>
            [.. AlternativeGroups.Find(jd, Find).Select(g => g.ToHashSet(StringComparer.OrdinalIgnoreCase))];

        [Fact]
        public void Reads_an_inline_topic_list_across_lines_as_one_group()
        {
            List<HashSet<string>> groups = Groups("""
                • Machine Learning • Distributed Systems and Data Management • Database Systems • Quantum Computing
                • Network Development • Query Processing and Optimization • Embedded Systems •
                Data Engineering • Mobile Development • Game Development
                • Work in an agile environment practicing CI/CD principles.
                """);

            HashSet<string> group = Assert.Single(groups);
            Assert.Equal(6, group.Count);
            Assert.DoesNotContain("agile", group);
        }

        [Theory]
        [InlineData("Experience with a cloud provider such as AWS, Azure, or Google Cloud.")]
        [InlineData("Experience with a cloud provider (e.g. AWS, Azure, Google Cloud).")]
        [InlineData("Experience with one of the following: AWS, Azure, Google Cloud")]
        [InlineData("Hands-on AWS, Azure, or Google Cloud experience.")]
        public void Reads_a_choice_in_a_sentence(string jd)
        {
            Assert.Equal(new HashSet<string>(["AWS", "Azure", "Google Cloud"]), Assert.Single(Groups(jd)));
        }

        [Fact]
        public void Reads_bullets_under_a_lead_in_line()
        {
            List<HashSet<string>> groups = Groups("Strong skills in any of the following:\n- Python\n- Java\n- Go\n\nExperience with SQL");
            Assert.Equal(new HashSet<string>(["Python", "Java", "Go"]), Assert.Single(groups));
        }

        [Theory]
        [InlineData("Experience with SQL, Docker, and Git.")]
        [InlineData("Requirements\n• Python\n• SQL\n• Docker")]
        [InlineData("Stack: Python | SQL | Docker | Git")]
        [InlineData("• Python • SQL • Docker (all required)")]
        public void Leaves_plain_requirement_lists_alone(string jd)
        {
            Assert.Empty(Groups(jd));
        }

        [Fact]
        public void A_skill_required_on_its_own_leaves_its_group()
        {
            // Python or Java, but Python is also required outright
            List<HashSet<string>> groups = Groups("Python or Java or Go.\nStrong Python skills.");
            Assert.Equal(new HashSet<string>(["Java", "Go"]), Assert.Single(groups));
        }
    }

    public class SkillMatcherAlternativeTests
    {
        private static readonly Dictionary<string, int> NoTiers = [];
        private static readonly IReadOnlyList<IReadOnlySet<string>> Topics =
            [new HashSet<string>(["Machine Learning", "Quantum Computing", "Game Development"])];
        private static readonly string[] Job = ["Machine Learning", "Quantum Computing", "Game Development", "SQL"];

        [Fact]
        public void One_match_covers_the_group_and_the_others_are_not_missing()
        {
            SkillReport report = Match([("Machine Learning", SectionType.Projects), ("SQL", SectionType.Experience)],
                Job, NoTiers, alternatives: Topics);

            Assert.Empty(report.Missing);
            ScoredSkill ml = Assert.Single(report.Matched, s => s.Name == "Machine Learning");
            Assert.Equal(3, ml.GroupSize);
            // Two requirements (the group and SQL), both fully covered
            Assert.Equal(1.0, report.WeightCoverage, 6);
        }

        [Fact]
        public void An_unmet_group_counts_once()
        {
            SkillReport report = Match([("SQL", SectionType.Experience)], Job, NoTiers, alternatives: Topics);

            Assert.Equal(3, report.Missing.Count);
            Assert.All(report.Missing, s => Assert.Equal(3, s.GroupSize));
            // SQL covered, the group not: 1 of 2, not 1 of 4
            Assert.Equal(0.5, report.WeightCoverage, 6);
        }

        [Fact]
        public void Without_groups_every_skill_counts()
        {
            SkillReport report = Match([("SQL", SectionType.Experience)], Job, NoTiers);
            Assert.Equal(0.25, report.WeightCoverage, 6);
        }
    }
}
