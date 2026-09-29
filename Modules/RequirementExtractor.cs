using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GetJobCV.Modules
{
    /// <summary>
    /// A minimum years-of-experience requirement from a job description.
    /// </summary>
    /// <param name="Skill">The skill it applies to, or null for overall experience</param>
    /// <param name="MinYears">The lower bound: "3-5 years" and "3+ years" are both 3</param>
    /// <param name="Text">The phrase as written, e.g. "3+ years"</param>
    public sealed record YearsRequirement(string? Skill, int MinYears, string Text);

    /// <summary>
    /// Years-of-experience requirements found in a job description.
    /// </summary>
    /// <param name="OverallYears">Required years of experience in general, if stated</param>
    /// <param name="SkillYears">Skill to required years, case-insensitive</param>
    /// <param name="All">Every requirement found, in document order</param>
    public sealed record JobRequirements(
        int? OverallYears,
        IReadOnlyDictionary<string, int> SkillYears,
        IReadOnlyList<YearsRequirement> All)
    {
        public static readonly JobRequirements None =
            new(null, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), []);
    }

    /// <summary>
    /// Finds years-of-experience requirements ("3+ years of Python", "at least five years
    /// of professional experience") in a job description.
    /// </summary>
    public static partial class RequirementExtractor
    {
        private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
            ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
        };

        /// <summary>
        /// Extract requirements. Each clause (line or sentence) with a years phrase is run
        /// through NER; each skill in it takes the nearest years phrase. A years phrase with
        /// no skill is an overall requirement if the clause mentions experience.
        /// </summary>
        public static JobRequirements Extract(string jobDescription, NerExtractor ner) =>
            Extract(jobDescription, clause => ner.Extract(clause).Skills);

        /// <param name="findSkills">Skills in one clause; NER in the app, a stub in tests</param>
        public static JobRequirements Extract(string jobDescription, Func<string, IEnumerable<string>> findSkills)
        {
            List<YearsRequirement> all = [];

            foreach (string clause in ClauseRegex().Split(jobDescription))
            {
                List<Match> mentions = YearsRegex().Matches(clause).ToList();
                if (mentions.Count == 0)
                    continue;

                HashSet<Match> claimed = [];
                foreach (string skill in findSkills(clause))
                {
                    int at = WholeWordIndex(clause, skill);
                    if (at < 0)
                        continue;

                    Match nearest = mentions.MinBy(m => Distance(m, at, skill.Length))!;
                    claimed.Add(nearest);
                    all.Add(new YearsRequirement(skill, ParseYears(nearest), nearest.Value.Trim()));
                }

                if (!clause.Contains("experience", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (Match m in mentions.Where(m => !claimed.Contains(m)))
                    all.Add(new YearsRequirement(null, ParseYears(m), m.Value.Trim()));
            }

            int? overall = all.Where(r => r.Skill is null).Select(r => (int?)r.MinYears).Max();

            Dictionary<string, int> skillYears = new(StringComparer.OrdinalIgnoreCase);
            foreach (YearsRequirement r in all.Where(r => r.Skill is not null))
                skillYears[r.Skill!] = Math.Max(r.MinYears, skillYears.GetValueOrDefault(r.Skill!));

            return new JobRequirements(overall, skillYears, all);
        }

        /// <summary>
        /// Characters between a skill and a years phrase (0 if they touch).
        /// </summary>
        private static int Distance(Match m, int start, int length) =>
            start >= m.Index + m.Length ? start - (m.Index + m.Length)
            : m.Index >= start + length ? m.Index - (start + length)
            : 0;

        /// <summary>
        /// Where <paramref name="skill"/> appears as a whole word, so "C" isn't found
        /// inside "Backend" or "C#". -1 if it doesn't.
        /// </summary>
        private static int WholeWordIndex(string clause, string skill)
        {
            Match m = Regex.Match(clause,
                @"(?<![\p{L}\p{N}])" + Regex.Escape(skill) + @"(?![\p{L}\p{N}#+])",
                RegexOptions.IgnoreCase);
            return m.Success ? m.Index : -1;
        }

        private static int ParseYears(Match m)
        {
            string n = m.Groups["n"].Value;
            return int.TryParse(n, out int years) ? years : NumberWords[n];
        }

        // Generate regex at compile time

        /// <summary>
        /// Splits on lines, bullets, semicolons, and sentence ends. A sentence can start
        /// with a number ("…experience. 3+ years of C#").
        /// </summary>
        [GeneratedRegex(@"\r?\n|[;•]|(?<=[.!?])\s+(?=[A-Z0-9])")]
        private static partial Regex ClauseRegex();

        /// <summary>
        /// "3+ years", "3-5 yrs", "at least five years", "minimum of 2 years", "(4+ years)".
        /// The lower bound is captured as "n".
        /// </summary>
        [GeneratedRegex(
            @"(?:\b(?:at\s+least|minimum(?:\s+of)?|min\.?|over|more\s+than)\s+)?" +
            @"\b(?<n>\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten)\s*(?:\+|plus)?" +
            @"(?:\s*(?:-|–|to)\s*(?:\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten)\s*\+?)?" +
            @"\s*(?:years?|yrs?)\b",
            RegexOptions.IgnoreCase)]
        private static partial Regex YearsRegex();
    }
}
