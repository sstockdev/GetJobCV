using GetJobCV.Modules;

namespace GetJobCV.Tests
{
    public class ResumeParserTests
    {
        private static ResumeRecord Parse(SectionType type, string text) =>
            ResumeParser.Parse([new ResumeSection(type, type.ToString(), text)], new Socials(null, null));

        private static ExperienceEntry OnlyRole(string text) =>
            Assert.Single(Parse(SectionType.Experience, text).Experience);

        private static EducationEntry OnlySchool(string text) =>
            Assert.Single(Parse(SectionType.Education, text).Education);

        // ---- Roles ----

        [Theory]
        [InlineData("Software Engineer, Acme Corp, Remote  Jan 2022 - Present", "Software Engineer", "Acme Corp", "Remote")]
        [InlineData("Junior Developer, Initech, Austin, TX  Jun 2020 - Dec 2021", "Junior Developer", "Initech", "Austin, TX")]
        [InlineData("Data Analyst | Globex | Chicago, IL  2019 - 2020", "Data Analyst", "Globex", "Chicago, IL")]
        [InlineData("Software Engineer at Acme Corp  Jan 2022 - Present", "Software Engineer", "Acme Corp", null)]
        [InlineData("Acme Corp — Senior Engineer  2018 - 2021", "Senior Engineer", "Acme Corp", null)]
        [InlineData("Backend Developer, Hooli, Inc., Palo Alto, CA  2017 - 2018", "Backend Developer", "Hooli, Inc.", "Palo Alto, CA")]
        public void Splits_a_one_line_role_header(string header, string title, string org, string? location)
        {
            ExperienceEntry role = OnlyRole(header + "\n- Built things");
            Assert.Equal(title, role.Title);
            Assert.Equal(org, role.Organization);
            Assert.Equal(location, role.Location);
            Assert.True(role.Dates.Start.HasValue);
        }

        [Fact]
        public void Keeps_the_two_line_title_then_org_layout()
        {
            ExperienceEntry role = OnlyRole(
                "Software Engineering Intern Jun 2024 - Aug 2024\nDigiKey | Thief River Falls, MN\n• Wrote tools");
            Assert.Equal("Software Engineering Intern", role.Title);
            Assert.Equal("DigiKey", role.Organization);
            Assert.Equal("Thief River Falls, MN", role.Location);
        }

        [Fact]
        public void Keeps_the_two_line_org_then_title_layout()
        {
            ExperienceEntry role = OnlyRole(
                "DigiKey | Thief River Falls, MN   May 2023 - Aug 2023\nIT Intern\n• Fixed laptops");
            Assert.Equal("IT Intern", role.Title);
            Assert.Equal("DigiKey", role.Organization);
            Assert.Equal("Thief River Falls, MN", role.Location);
        }

        [Fact]
        public void Reads_a_line_without_a_title_as_org_and_location()
        {
            ExperienceEntry role = OnlyRole("Initech | Austin, TX  2019 - 2020\n- Did work");
            Assert.Null(role.Title);
            Assert.Equal("Initech", role.Organization);
            Assert.Equal("Austin, TX", role.Location);
        }

        // ---- Education ----

        [Fact]
        public void Splits_degree_school_dates_and_gpa_on_one_line()
        {
            EducationEntry e = OnlySchool("B.S. Computer Science, State University  2016 - 2020  GPA 3.7");
            Assert.Equal("B.S. Computer Science", e.Degree);
            Assert.Equal("State University", e.School);
            Assert.Null(e.Location);
            Assert.Equal("2016 - 2020", e.Dates?.Text);
            Assert.Equal("3.7", e.Gpa);
        }

        [Fact]
        public void Splits_a_school_first_line_with_a_state_code()
        {
            EducationEntry e = OnlySchool("Boston University, Boston, MA, B.A. Economics  2015 - 2019");
            Assert.Equal("B.A. Economics", e.Degree);
            Assert.Equal("Boston University", e.School);
            Assert.Equal("Boston, MA", e.Location);
        }

        [Fact]
        public void Reads_the_location_after_the_school()
        {
            EducationEntry e = OnlySchool("Bachelor of Science in Physics, University of Minnesota, Minneapolis, MN  May 2021");
            Assert.Equal("Bachelor of Science in Physics", e.Degree);
            Assert.Equal("University of Minnesota", e.School);
            Assert.Equal("Minneapolis, MN", e.Location);
        }

        [Fact]
        public void Keeps_separate_degree_and_school_lines()
        {
            EducationEntry e = OnlySchool("State University | Springfield, IL\nB.S. Computer Science May 2020\nGPA: 3.8/4.0");
            Assert.Equal("B.S. Computer Science", e.Degree);
            Assert.Equal("State University", e.School);
            Assert.Equal("Springfield, IL", e.Location);
            Assert.Equal("May 2020", e.Dates?.Text);
            Assert.Equal("3.8/4.0", e.Gpa);
        }
    }
}
