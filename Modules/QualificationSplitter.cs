using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GetJobCV.Modules
{
    /// <summary>
    /// Separates a job description's nice-to-have skills from its required ones.
    /// Pimpalkar et al. (Section III, step 5) rank minimum credentials before preferred
    /// ones; here a preferred skill just counts for less (<see cref="SkillMatcher.PreferredWeight"/>).
    /// </summary>
    public static partial class QualificationSplitter
    {
        /// <summary>
        /// Skills the job description only mentions as nice to have. A skill that is also
        /// mentioned as required anywhere is required.
        /// </summary>
        public static IReadOnlySet<string> PreferredSkills(string jobDescription, NerExtractor ner) =>
            PreferredSkills(jobDescription, ner.ExtractSkills);

        /// <param name="findSkills">Skills in one clause, written as named; a stub in tests</param>
        public static IReadOnlySet<string> PreferredSkills(
            string jobDescription, Func<string, IEnumerable<string>> findSkills) =>
            PreferredSkills(jobDescription, clause => findSkills(clause).Select(s => new NerExtractor.SkillMention(s, s)));

        /// <param name="findMentions">Skills in one clause, with how each is written</param>
        public static IReadOnlySet<string> PreferredSkills(
            string jobDescription, Func<string, IEnumerable<NerExtractor.SkillMention>> findMentions)
        {
            HashSet<string> preferred = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> required = new(StringComparer.OrdinalIgnoreCase);

            // A "Preferred qualifications" heading holds until the next heading, or a blank
            // line once the section has content (a blank right after the heading doesn't count)
            bool inPreferredSection = false;
            bool sectionHasContent = false;

            foreach (string rawLine in jobDescription.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    if (sectionHasContent)
                        inPreferredSection = false;
                    continue;
                }

                if (HeadingKind(line) is { } preferredHeading)
                {
                    inPreferredSection = preferredHeading;
                    sectionHasContent = false;
                    continue;
                }
                sectionHasContent = true;

                foreach (string clause in RequirementExtractor.SplitClauses(line))
                {
                    List<(int At, bool Preferred)> cues =
                    [
                        .. PreferredCueRegex().Matches(clause).Select(m => (m.Index, true)),
                        .. RequiredCueRegex().Matches(clause).Select(m => (m.Index, false)),
                    ];

                    foreach (NerExtractor.SkillMention skill in findMentions(clause))
                    {
                        int at = RequirementExtractor.WholeWordIndex(clause, skill.Text);
                        if (at < 0)
                            continue;

                        // With cues of both kinds ("Python required, Go preferred") the nearest decides
                        bool isPreferred = cues.Count == 0
                            ? inPreferredSection
                            : cues.MinBy(c => Math.Abs(c.At - at)).Preferred;
                        (isPreferred ? preferred : required).Add(skill.Name);
                    }
                }
            }

            preferred.ExceptWith(required);
            return preferred;
        }

        /// <summary>
        /// Whether a line is a section heading, and if so whether it starts a nice-to-have
        /// section. Null for any other line.
        /// <list type="bullet">
        /// <item>A known heading ("Preferred Qualifications", "Requirements:", "Nice to have")</item>
        /// <item>A short line ending in a colon with a preferred cue ("Bonus points if you have:")</item>
        /// </list>
        /// A lead-in like "Familiarity with:" isn't a heading, so it keeps the current section.
        /// "Nice to have: Rust" isn't either; it's a clause with a cue in it.
        /// </summary>
        private static bool? HeadingKind(string line)
        {
            if (Bullets.Any(line.StartsWith))
                return null;

            string name = line.TrimEnd(':', ' ');
            bool preferred = PreferredCueRegex().IsMatch(name);
            if (KnownHeadingRegex().IsMatch(name))
                return preferred;
            if (line.EndsWith(':') && preferred && name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8)
                return true;
            return null;
        }

        private static readonly string[] Bullets = ["•", "-", "*", "–", "▪", "◦", "●", "○", "■"];

        // Generate regex at compile time

        [GeneratedRegex(
            @"^(?:(?:preferred|minimum|basic|required|desired|additional|bonus|key)\s+)?(?:qualifications|requirements|skills(?:\s+and\s+experience)?)$|" +
            @"^(?:(?:nice|good)[\s-]to[\s-]haves?|must[\s-]haves?|pluses|bonus\s+points|responsibilities|" +
            @"what\s+you['’]ll\s+(?:need|bring|do)|what\s+we['’]re\s+looking\s+for|who\s+you\s+are|about\s+you|you\s+have)$",
            RegexOptions.IgnoreCase)]
        private static partial Regex KnownHeadingRegex();

        /// <summary>
        /// "a plus", "bonus", "nice to have", "preferred", "ideally", "not required", ...
        /// </summary>
        [GeneratedRegex(
            @"\b(?:a|big|huge|definite|strong)\s+plus\b|\bplus(?:es)?\s*(?:if\b|:)|\bpluses\b|\bbonus\b|" +
            @"\b(?:nice|good)[\s-]to[\s-]haves?\b|\bprefer(?:red|ably)?\b|\bdesir(?:ed|able)\b|\bideally\b|" +
            @"\boptional\b|\badvantage(?:ous)?\b|\bnot\s+(?:required|necessary|mandatory)\b|\bextra\s+credit\b",
            RegexOptions.IgnoreCase)]
        private static partial Regex PreferredCueRegex();

        /// <summary>
        /// "required", "must", "mandatory", but not "not required".
        /// </summary>
        [GeneratedRegex(@"(?<!\bnot\s)\b(?:required|must|mandatory|essential)\b", RegexOptions.IgnoreCase)]
        private static partial Regex RequiredCueRegex();

    }
}
