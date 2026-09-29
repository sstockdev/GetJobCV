using GetJobCV.Modules;

namespace GetJobCV.UI
{
    /// <summary>
    /// The report's wording, shared by the results screen and the exported file so they
    /// always say the same thing.
    /// </summary>
    public static class ReportText
    {
        /// <summary>
        /// One or two sentences under the verdict: what's missing or weak.
        /// </summary>
        public static string Advice(SkillMatcher.SkillReport skills)
        {
            if (skills.Matched.Count == 0 && skills.Missing.Count == 0)
                return "No known skills were recognized in the job description, so the score is text similarity only.";

            List<string> notes = [];
            int missing = skills.Missing.Count(s => !s.Preferred && s.Group is null);
            if (missing > 0)
                notes.Add($"{missing} skill{(missing == 1 ? "" : "s")} the job requires {(missing == 1 ? "wasn't" : "weren't")} found in your resume.");

            // A list where any one skill counts is one requirement, however long it is
            List<SkillMatcher.ScoredSkill[]> unmet = [.. skills.Missing.Where(s => !s.Preferred && s.Group is not null)
                .GroupBy(s => s.Group).Select(g => g.ToArray())];
            if (unmet.Count == 1)
                notes.Add($"None of the {unmet[0].Length} options in a list where any one counts is in your resume.");
            else if (unmet.Count > 1)
                notes.Add($"{unmet.Count} lists where any one skill counts have no match in your resume.");

            int missingNice = skills.Missing.Count(s => s.Preferred);
            if (missingNice > 0)
                notes.Add($"{missingNice} nice-to-have skill{(missingNice == 1 ? " is" : "s are")} missing{(missing > 0 ? " too" : "")}.");

            int weak = skills.Matched.Count(s => s.Evidence < SkillMatcher.UsedEvidence);
            if (weak > 0)
                notes.Add($"{weak} matched skill{(weak == 1 ? " is" : "s are")} only listed or mentioned, not shown in use.");

            int shortYears = skills.Matched.Count(IsShortOnYears);
            if (shortYears > 0)
                notes.Add($"{shortYears} skill{(shortYears == 1 ? " shows" : "s show")} fewer years than the job asks for.");

            if (skills.OverallYears is { Credit: < 1.0 } overall)
                notes.Add($"The job asks for {overall.RequiredMonths / 12}+ years of experience.");

            return notes.Count > 0 ? string.Join(" ", notes) : "Every skill the job asks for is shown in use.";
        }

        /// <summary>
        /// "Hot skill", "In-demand skill", or "Skill".
        /// </summary>
        public static string Demand(SkillMatcher.ScoredSkill skill) =>
            skill.Tier switch { 2 => "Hot skill", 1 => "In-demand skill", _ => "Skill" };

        /// <summary>
        /// Where the resume shows the skill and what that's worth, and the more specific
        /// skills that showed it ("shown in use in Experience, through PostgreSQL, MySQL").
        /// </summary>
        public static string Evidence(SkillMatcher.ScoredSkill skill) => skill.Section switch
        {
            null => "not found in the resume",
            SectionType.Skills => "only in the Skills list, counts half",
            SectionType s when skill.Evidence < SkillMatcher.UsedEvidence => $"mentioned in {s}, counts {skill.Evidence:P0}",
            SectionType s => $"shown in use in {s}",
        } + (skill.ImpliedBy is { Count: > 0 } via ? $", through {string.Join(", ", via.Select(DisplayName))}" : "");

        /// <summary>
        /// "job asks for 3+ years, resume shows 1.7", or null without a years requirement.
        /// </summary>
        public static string? Years(SkillMatcher.ScoredSkill skill) => skill.RequiredMonths is { } months
            ? $"job asks for {months / 12}+ years" + (skill.Section is null ? "" : $", resume shows {skill.ShownMonths / 12.0:0.#}")
            : null;

        /// <summary>
        /// "nice to have, counts 50% of a required skill", or null for a required skill.
        /// </summary>
        public static string? NiceToHave(SkillMatcher.ScoredSkill skill) =>
            skill.Preferred ? $"nice to have, counts {SkillMatcher.PreferredWeight:P0} of a required skill" : null;

        /// <summary>
        /// "one of 11 options where any one counts", or null outside a group of alternatives.
        /// </summary>
        public static string? Alternative(SkillMatcher.ScoredSkill skill) => skill.GroupSize switch
        {
            0 => null,
            int n when skill.Section is null => $"one of {n} options where any one counts",
            int n => $"one of {n} options where any one counts; this one covers them",
        };

        /// <summary>
        /// Everything about one skill in a line: demand · evidence · years · nice to have · alternatives.
        /// </summary>
        public static string Explanation(SkillMatcher.ScoredSkill skill) =>
            string.Join(" · ", new[] { Demand(skill), Evidence(skill), Years(skill), NiceToHave(skill), Alternative(skill) }.OfType<string>());

        /// <summary>
        /// The short tag on a chip: the specific skill that covered it, where it was found
        /// when that counts for less, and a years shortfall ("via PostgreSQL, skills list ·
        /// 1.7 of 3+ yrs"). Null when there's nothing to flag.
        /// </summary>
        public static string? Tag(SkillMatcher.ScoredSkill skill)
        {
            string? years = skill.RequiredMonths is not { } required ? null
                : skill.Section is null ? $"{required / 12}+ yrs"
                : IsShortOnYears(skill) ? $"{skill.ShownMonths / 12.0:0.#} of {required / 12}+ yrs"
                : null;
            // A missing option from a list where any one counts
            if (skill.Section is null && skill.GroupSize > 0)
                years = years is null ? $"any of {skill.GroupSize}" : $"any of {skill.GroupSize} · {years}";
            string? where = skill.Section switch
            {
                null => null,
                SectionType.Skills => "skills list",
                SectionType.Contact => "profile link",
                SectionType s when skill.Evidence < SkillMatcher.UsedEvidence => s.ToString().ToLowerInvariant(),
                _ => null,
            };
            // Only the skill whose section counts; the rest are in the evidence text
            if (skill.ImpliedBy is { Count: > 0 } via)
            {
                string by = via.Count > 1 ? $"via {DisplayName(via[0])} +{via.Count - 1}" : $"via {DisplayName(via[0])}";
                where = where is null ? by : $"{by}, {where}";
            }
            return where is null ? years : years is null ? where : $"{where} · {years}";
        }

        public static bool IsShortOnYears(SkillMatcher.ScoredSkill skill) =>
            skill.Section is not null && skill.RequiredMonths is { } required && skill.ShownMonths < required;

        /// <summary>
        /// "Experience", or "Experience ("Work History")" when the heading said something else.
        /// </summary>
        public static string SectionName(ResumeSection s) =>
            s.Type == SectionType.Contact || s.Heading.Equals(s.Type.ToString(), StringComparison.OrdinalIgnoreCase)
                ? s.Type.ToString()
                : $"{s.Type} (\"{s.Heading}\")";

        /// <summary>
        /// "Acme Corp · Jan 2022 – Present · Remote", plus the section for Leadership or Volunteer roles.
        /// </summary>
        public static string RoleDetails(ExperienceEntry role)
        {
            List<string> parts = [.. new[] { role.Organization, role.Dates.Text, role.Location }
                .OfType<string>().Where(s => !string.IsNullOrWhiteSpace(s))];
            if (role.Section != SectionType.Experience)
                parts.Add(role.Section.ToString());
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// "State University · 2016 – 2020 · GPA 3.7"
        /// </summary>
        public static string SchoolDetails(EducationEntry e) =>
            string.Join(" · ", new[] { e.Degree is null ? null : e.School, e.Dates?.Text, e.Gpa is null ? null : $"GPA {e.Gpa}" }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        /// <summary>
        /// "Most time with: SQL 6.3 yrs, .NET 4.8 yrs", or null without per-skill months.
        /// </summary>
        public static string? MostTimeWith(ExperienceSummary summary) => summary.SkillMonths.Count == 0 ? null
            : "Most time with: " + string.Join(", ", summary.SkillMonths.Take(5).Select(s => $"{DisplayName(s.Skill)} {s.Months / 12.0:0.#} yrs"));

        /// <summary>
        /// A skill name for display. Names written all in lowercase ("agile", "design
        /// patterns") get a capital first letter to match the rest; others keep their casing
        /// ("iOS", "CI/CD").
        /// </summary>
        public static string DisplayName(string skill) =>
            skill.Length > 0 && !skill.Any(char.IsUpper) ? char.ToUpperInvariant(skill[0]) + skill[1..] : skill;

        /// <summary>
        /// A link as people write it: "https://www.linkedin.com/in/jane/" → "linkedin.com/in/jane".
        /// </summary>
        public static string ShortUrl(string url)
        {
            string s = url.Trim();
            foreach (string prefix in new[] { "https://", "http://", "www." })
                if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    s = s[prefix.Length..];
            return s.TrimEnd('/');
        }

        /// <summary>
        /// A profile link as a web address ("github.com/jane" → https://github.com/jane).
        /// Null unless it's http or https, so a resume can't smuggle in another scheme.
        /// </summary>
        public static Uri? WebUri(string url)
        {
            string withScheme = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
            return Uri.TryCreate(withScheme, UriKind.Absolute, out Uri? uri) &&
                   (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;
        }

        /// <summary>
        /// "Meets the job's 5+ years" or "Job asks for 5+ years", or null without a requirement.
        /// </summary>
        public static string? OverallYears(SkillMatcher.YearsCheck? required) => required is null ? null
            : required.Credit >= 1.0 ? $"Meets the job's {required.RequiredMonths / 12}+ years"
            : $"Job asks for {required.RequiredMonths / 12}+ years";
    }
}
