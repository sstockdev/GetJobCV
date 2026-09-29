using GetJobCV.Modules;

namespace GetJobCV.Tests
{
    /// <summary>
    /// Plural, hyphenated and verb forms of skills, as loaded from Resources/.
    /// </summary>
    public class SkillsGazetteerWordFormTests
    {
        public SkillsGazetteerWordFormTests() => Environment.CurrentDirectory = AppContext.BaseDirectory;

        [Theory]
        [InlineData("code reviews", "code review")]
        [InlineData("code-review", "code review")]
        [InlineData("code-reviews", "code review")]
        [InlineData("distributed-systems", "distributed systems")]
        [InlineData("communicate", "communication")]
        [InlineData("troubleshot", "Troubleshooting")]
        public void Maps_a_word_form_to_its_skill(string form, string skill)
        {
            Assert.Contains(form, SkillsGazetteer.Load(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(skill, SkillsGazetteer.LoadAliases()[form]);
        }

        [Theory]
        [InlineData("springs")]
        [InlineData("expressed")]
        [InlineData("distributed systemses")]
        [InlineData("next.jss")]
        public void Leaves_one_word_and_tech_names_alone(string form)
        {
            Assert.DoesNotContain(form, SkillsGazetteer.Load(), StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_listed_alias_wins_over_a_generated_form()
        {
            Assert.Equal("REST API", SkillsGazetteer.LoadAliases()["rest apis"]);
        }
    }

    [Trait("Category", "Ner")]
    public class NerWordFormTests(NerFixture fixture) : IClassFixture<NerFixture>
    {
        private readonly NerExtractor _ner = fixture.Ner;

        [Theory]
        [InlineData("Participate in code reviews and design discussions", "code review")]
        [InlineData("Collaborate and communicate effectively with the team", "collaboration")]
        [InlineData("Collaborate and communicate effectively with the team", "communication")]
        [InlineData("Work with a distributed-systems scope", "distributed systems")]
        [InlineData("Troubleshot cluster jobs and documented the fixes", "Troubleshooting")]
        [InlineData("Troubleshot cluster jobs and documented the fixes", "documentation")]
        public void Finds_a_skill_written_in_another_form(string text, string skill)
        {
            Assert.Contains(skill, _ner.Extract(text).Skills, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Visited the hot springs and expressed interest")]
        [InlineData("Read the documents and reviewed the code")]
        public void Does_not_find_skills_in_ordinary_words(string text)
        {
            Assert.Empty(_ner.Extract(text).Skills);
        }
    }
}
