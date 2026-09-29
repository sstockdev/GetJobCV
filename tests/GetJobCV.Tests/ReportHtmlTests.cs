using GetJobCV.Modules;
using GetJobCV.UI;
using static GetJobCV.Modules.SkillMatcher;

namespace GetJobCV.Tests
{
    public class ReportHtmlTests
    {
        private static ReportInput Input(ContactInfo? contact = null, string jd = "3+ years of C#. Python is a plus.") => new(
            "jane-doe.pdf",
            jd,
            new DateTime(2026, 9, 29, 14, 5, 0),
            new ResumeRecord(
                contact ?? new ContactInfo("Jane Doe", "jane@example.com", null, "github.com/janedoe", null),
                [new EducationEntry("B.S. Computer Science", "State University", null, new DateRange("2016 - 2020", null, null, false), "3.7")],
                [new ExperienceEntry("Software Engineer", "Acme Corp", "Remote", new DateRange("Jan 2022 - Present", null, null, true), SectionType.Experience, "")]),
            new ExperienceSummary(40, [("C#", 40)]),
            [new ResumeSection(SectionType.Experience, "Work History", ""), new ResumeSection(SectionType.Skills, "Skills", "")],
            new SkillReport(
                [new ScoredSkill("C#", 2, SectionType.Experience, 36, 40), new ScoredSkill("Python", 2, SectionType.Skills, null, 0, true)],
                [new ScoredSkill("Kubernetes", 2)],
                [new ScoredSkill("React", 1, SectionType.Projects)],
                new YearsCheck(60, 40),
                0.7),
            0.4);

        [Fact]
        public void Shows_the_score_verdict_skills_and_resume()
        {
            string html = ReportHtml.Build(Input());

            // 0.5 × 70% coverage + 0.5 × 40% similarity = 55%
            Assert.Contains("aria-label=\"Overall score 55%\"", html);
            Assert.Contains(ScoreCombiner.Verdict(0.55), html);
            Assert.Contains("<th scope=\"row\">Kubernetes</th>", html);
            Assert.Contains("job asks for 3+ years, resume shows 3.3", html);
            Assert.Contains("Software Engineer", html);
            Assert.Contains("Experience (&quot;Work History&quot;)", html);
            Assert.Contains("Job asks for 5+ years", html);
            Assert.Contains("September 29, 2026 at 2:05 PM", html);
        }

        [Fact]
        public void Marks_nice_to_have_rows()
        {
            string html = ReportHtml.Build(Input());
            Assert.Contains("<tr class=\"nice\"><th scope=\"row\">Python</th>", html);
            Assert.Contains("nice to have, counts 50% of a required skill", html);
        }

        [Fact]
        public void Encodes_text_from_the_resume_and_job_description()
        {
            string html = ReportHtml.Build(Input(
                new ContactInfo("<script>alert(1)</script>", null, null, null, null),
                jd: "Needs <b>C#</b> & \"SQL\""));

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.Contains("Needs &lt;b&gt;C#&lt;/b&gt; &amp; &quot;SQL&quot;", html);
        }

        [Fact]
        public void Links_only_web_addresses()
        {
            string html = ReportHtml.Build(Input(new ContactInfo("Jane", null, null, "github.com/janedoe", "javascript:alert(1)")));

            Assert.Contains("<a href=\"https://github.com/janedoe\">github.com/janedoe</a>", html);
            Assert.DoesNotContain("href=\"javascript:", html);
            Assert.Contains("<dd>javascript:alert(1)</dd>", html);
        }

        [Fact]
        public void Shows_links_short_and_keeps_the_full_address()
        {
            string html = ReportHtml.Build(Input(new ContactInfo("Jane", null, null, null, "https://www.linkedin.com/in/jane-doe/")));
            Assert.Contains("<a href=\"https://www.linkedin.com/in/jane-doe/\">linkedin.com/in/jane-doe</a>", html);
        }

        [Theory]
        [InlineData("agile", "Agile")]
        [InlineData("design patterns", "Design patterns")]
        [InlineData("CI/CD", "CI/CD")]
        [InlineData("iOS", "iOS")]
        [InlineData(".NET", ".NET")]
        public void Capitalizes_only_all_lowercase_skill_names(string name, string shown)
        {
            Assert.Equal(shown, ReportText.DisplayName(name));
        }

        [Fact]
        public void Loads_nothing_from_outside_the_file()
        {
            string html = ReportHtml.Build(Input());
            Assert.DoesNotContain("<script", html);
            Assert.DoesNotContain("<link", html);
            Assert.DoesNotContain("src=", html);
            Assert.DoesNotContain("url(", html);
        }
    }
}
