using System.Text.RegularExpressions;
using GetJobCV.Modules;

namespace GetJobCV.Tests
{
    /// <summary>
    /// Pairing logic with a stub skill finder, so these run without the NER model.
    /// </summary>
    public class RequirementExtractorTests
    {
        /// <summary>
        /// Stands in for NER: returns each known skill that appears in the clause as a word.
        /// </summary>
        private static Func<string, IEnumerable<string>> Finds(params string[] skills) => clause =>
            skills.Where(s => Regex.IsMatch(clause,
                @"(?<![\p{L}\p{N}])" + Regex.Escape(s) + @"(?![\p{L}\p{N}#+])", RegexOptions.IgnoreCase));

        [Fact]
        public void Pairs_each_years_phrase_with_its_own_sentence()
        {
            JobRequirements r = RequirementExtractor.Extract(
                "Backend engineer. 5+ years of experience. 3+ years of C#. Experience with SQL, Docker, Kubernetes, and Git.",
                Finds("C#", "SQL", "Docker", "Kubernetes", "Git"));

            Assert.Equal(5, r.OverallYears);
            Assert.Equal(3, r.SkillYears["C#"]);
            Assert.Single(r.SkillYears);
        }

        [Fact]
        public void Splits_sentences_that_start_with_a_number()
        {
            // As one clause, C# would take the only years phrase
            JobRequirements r = RequirementExtractor.Extract(
                "C# developer. 5+ years of experience.", Finds("C#"));

            Assert.Equal(5, r.OverallYears);
            Assert.Empty(r.SkillYears);
        }

        [Fact]
        public void Finds_a_skill_only_as_a_whole_word()
        {
            // A substring search finds "r" inside "years", next to "5+ years"
            JobRequirements r = RequirementExtractor.Extract(
                "5+ years of experience, including 2+ years of R", _ => ["R"]);

            Assert.Equal(2, r.SkillYears["R"]);
            Assert.Equal(5, r.OverallYears);
        }

        [Fact]
        public void Ignores_a_skill_that_only_appears_inside_another_word()
        {
            JobRequirements r = RequirementExtractor.Extract(
                "Backend role, 4+ years of experience", _ => ["C"]);

            Assert.Empty(r.SkillYears);
            Assert.Equal(4, r.OverallYears);
        }

        [Theory]
        [InlineData("3-5 years of Python", 3)]
        [InlineData("at least five years of Python", 5)]
        [InlineData("Python (4+ yrs)", 4)]
        public void Reads_the_lower_bound(string text, int years)
        {
            JobRequirements r = RequirementExtractor.Extract(text, Finds("Python"));
            Assert.Equal(years, r.SkillYears["Python"]);
        }
    }
}
