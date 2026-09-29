using System;
using System.Collections.Generic;
using System.Text;

namespace GetJobCV.Modules
{
    /// <summary>
    /// This class compares skills found in a resume against the skills founf in a job description.
    /// It produces a weighted match report.
    /// </summary>
    public static class SkillMatcher
    {
        /// <summary>
        /// Evidence factor for a skill the resume shows being used (Experience, Projects, ...).
        /// </summary>
        public const double UsedEvidence = 1.0;

        /// <summary>
        /// Evidence factor for a skill mentioned outside a skills list but not shown in use
        /// (Education, Summary, Certifications, a profile link in Contact, ...).
        /// </summary>
        public const double MentionedEvidence = 0.75;

        /// <summary>
        /// Evidence factor for a skill that only appears in the Skills list.
        /// </summary>
        public const double ListedEvidence = 0.5;

        /// <summary>
        /// For a skill with a years requirement, the share of its credit that depends on
        /// showing those years. The rest is for having the skill at all.
        /// </summary>
        public const double YearsShare = 0.5;

        /// <summary>
        /// Coverage weight of an overall years-of-experience requirement ("5+ years of
        /// experience"), the same as a hot skill.
        /// </summary>
        public const int OverallYearsWeight = 3;

        /// <summary>
        /// A skill the job description only lists as nice to have counts this share of a
        /// required skill's weight, so missing it costs less.
        /// </summary>
        public const double PreferredWeight = 0.5;

        /// <summary>
        /// An overall years-of-experience requirement and what the resume shows.
        /// </summary>
        public sealed record YearsCheck(int RequiredMonths, int ShownMonths)
        {
            /// <summary>
            /// Share of the requirement met, in <c>[0, 1]</c>
            /// </summary>
            public double Credit => RequiredMonths > 0 ? Math.Min(1.0, (double)ShownMonths / RequiredMonths) : 1.0;
        }

        /// <summary>
        /// A skill with it's O*NET demand tier and where the resume shows it
        /// </summary>
        /// <param name="Name">Skill</param>
        /// <param name="Tier">O*NET demand tier: 2 = hot, 1 = in demand, 0 = other</param>
        /// <param name="Section">The strongest resume section the skill appears in;
        /// null for a job description skill the resume doesn't have</param>
        /// <param name="RequiredMonths">Months of experience the job description asks for, if any</param>
        /// <param name="ShownMonths">Months of roles in the resume that mention the skill</param>
        /// <param name="Preferred">The job description only lists it as nice to have</param>
        /// <param name="Group">Which group of alternatives it's in ("one of the following"), if any</param>
        /// <param name="GroupSize">How many job skills are in that group; 0 when not in one</param>
        /// <param name="ImpliedBy">The more specific resume skills that cover this one
        /// ("PostgreSQL" for "SQL"), when the resume doesn't show it as strongly by name.
        /// Strongest evidence first; the first is the one whose section counts.</param>
        public sealed record ScoredSkill(
            string Name, int Tier, SectionType? Section = null, int? RequiredMonths = null, int ShownMonths = 0,
            bool Preferred = false, int? Group = null, int GroupSize = 0, IReadOnlyList<string>? ImpliedBy = null)
        {
            /// <summary>
            /// Demand weight: <c>Tier + 1</c>
            /// </summary>
            public int Weight => Tier + 1;

            /// <summary>
            /// The weight used for coverage: <see cref="Weight"/>, scaled by
            /// <see cref="PreferredWeight"/> for a nice-to-have skill
            /// </summary>
            public double CoverageWeight => Preferred ? Weight * PreferredWeight : Weight;

            /// <summary>
            /// How strongly the resume backs this skill, from <see cref="Section"/>
            /// </summary>
            public double Evidence => Section is { } s ? EvidenceFor(s) : 0.0;

            /// <summary>
            /// 1 without a years requirement. With one, <see cref="YearsShare"/> of the
            /// credit scales with how much of the required time the resume shows.
            /// </summary>
            public double YearsFactor => RequiredMonths is > 0 and { } required
                ? 1 - YearsShare + YearsShare * Math.Min(1.0, (double)ShownMonths / required)
                : 1.0;
        }

        /// <summary>
        /// This is the result of comparing.
        /// </summary>
        /// <param name="Matched">Job description skills the resume has, important first</param>
        /// <param name="Missing">Job descrption skills the resume is missing, most important first</param>
        /// <param name="Extra">Resume skills the job descrption does not ask for, most important first</param>
        /// <param name="OverallYears">The job's overall years requirement, if it has one</param>
        /// <param name="WeightCoverage">The share of total job description requirement weight the resume
        /// covers (skills scaled by evidence and years, plus <paramref name="OverallYears"/>), in <c>[0, 1]</c></param>
        public sealed record SkillReport(
            IReadOnlyList<ScoredSkill> Matched,
            IReadOnlyList<ScoredSkill> Missing,
            IReadOnlyList<ScoredSkill> Extra,
            YearsCheck? OverallYears,
            double WeightCoverage);

        /// <summary>
        /// How strongly a skill found in this section shows the candidate has it.
        /// </summary>
        public static double EvidenceFor(SectionType section) => section switch
        {
            SectionType.Experience or SectionType.Projects or SectionType.Leadership
                or SectionType.Volunteer or SectionType.Publications => UsedEvidence,
            SectionType.Skills => ListedEvidence,
            _ => MentionedEvidence
        };

        /// <param name="resumeMentions">Every (skill, section) pair found in the resume.
        /// A skill in several sections counts at its strongest one.</param>
        /// <param name="jobSkills">Skills found in the job description</param>
        /// <param name="tiers">O*NET skill name to demand tier</param>
        /// <param name="requiredYears">Skill to years the job description asks for (case-insensitive)</param>
        /// <param name="shownMonths">Skill to months of resume roles that mention it (case-insensitive)</param>
        /// <param name="overallYears">The job's overall years-of-experience requirement, if any</param>
        /// <param name="preferred">Job skills that are only nice to have (case-insensitive)</param>
        /// <param name="alternatives">Groups of job skills where any one will do (from
        /// <see cref="AlternativeGroups"/>). A group counts as one requirement: its weight is
        /// its heaviest member's, its credit the best matched member's. Once one member
        /// matches, the others aren't missing.</param>
        /// <param name="parents">Skill to the more general skills it implies (from
        /// <see cref="SkillsGazetteer.LoadParents"/>). A resume skill covers a job skill it
        /// implies at its own evidence and months, and then isn't counted as extra.</param>
        public static SkillReport Match(
            IEnumerable<(string Skill, SectionType Section)> resumeMentions,
            IEnumerable<string> jobSkills,
            IReadOnlyDictionary<string, int> tiers,
            IReadOnlyDictionary<string, int>? requiredYears = null,
            IReadOnlyDictionary<string, int>? shownMonths = null,
            YearsCheck? overallYears = null,
            IReadOnlySet<string>? preferred = null,
            IReadOnlyList<IReadOnlySet<string>>? alternatives = null,
            IReadOnlyDictionary<string, IReadOnlySet<string>>? parents = null)
        {
            Dictionary<string, SectionType> resume = BestSections(resumeMentions);
            var (implied, impliedMonths) = Implied(resume, parents, shownMonths);
            HashSet<string> job = Distinct(jobSkills);
            HashSet<string> niceToHave = new(preferred ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);

            // Only job skills count toward a group, and a group needs two to be a choice
            List<List<string>> groups = [.. (alternatives ?? [])
                .Select(g => g.Where(job.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                .Where(g => g.Count >= 2)];
            // A skill in two groups belongs to the first; a group left with one member isn't a choice
            Dictionary<string, int> groupOf = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < groups.Count; i++)
                foreach (string name in groups[i])
                    groupOf.TryAdd(name, i);
            Dictionary<int, int> groupSize = groupOf.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Count());
            foreach (string lone in groupOf.Where(p => groupSize[p.Value] < 2).Select(p => p.Key).ToList())
                groupOf.Remove(lone);

            ScoredSkill Score(string name, SectionType? section, bool isJobSkill, IReadOnlyList<string>? impliedBy = null)
            {
                int? group = isJobSkill && groupOf.TryGetValue(name, out int g) ? g : null;
                int months = shownMonths?.GetValueOrDefault(name) ?? 0;
                if (isJobSkill)
                    months = Math.Max(months, impliedMonths.GetValueOrDefault(name));
                return new(
                    name,
                    tiers.GetValueOrDefault(name),
                    section,
                    requiredYears?.TryGetValue(name, out int years) == true ? years * 12 : null,
                    months,
                    isJobSkill && niceToHave.Contains(name),
                    group,
                    group is { } id ? groupSize[id] : 0,
                    impliedBy);
            }

            // A job skill as the resume shows it: by name, or through a more specific
            // skill when that's stronger evidence. Null when the resume has neither.
            ScoredSkill? Found(string name)
            {
                bool named = resume.TryGetValue(name, out SectionType section);
                if (implied.TryGetValue(name, out var via) && (!named || EvidenceFor(via.Section) > EvidenceFor(section)))
                    return Score(name, via.Section, true, via.Skills);
                return named ? Score(name, section, true) : null;
            }

            List<ScoredSkill> matched = [];
            List<ScoredSkill> missing = [];
            double coveredWeight = 0;
            double totalWeight = 0;

            foreach (string name in job.Where(n => !groupOf.ContainsKey(n)))
            {
                if (Found(name) is { } skill)
                {
                    matched.Add(skill);
                    coveredWeight += skill.CoverageWeight * skill.Evidence * skill.YearsFactor;
                    totalWeight += skill.CoverageWeight;
                }
                else
                {
                    ScoredSkill absent = Score(name, null, true);
                    missing.Add(absent);
                    totalWeight += absent.CoverageWeight;
                }
            }

            foreach (IGrouping<int, string> group in groupOf.GroupBy(pair => pair.Value, pair => pair.Key))
            {
                List<ScoredSkill> members = [.. group.Select(name => Found(name) ?? Score(name, null, true))];
                List<ScoredSkill> have = [.. members.Where(m => m.Section is not null)];

                totalWeight += members.Max(m => m.CoverageWeight);
                coveredWeight += have.Count == 0 ? 0
                    : members.Max(m => m.CoverageWeight) * have.Max(m => m.Evidence * m.YearsFactor);

                // Any one will do: with a match, the other options aren't missing
                if (have.Count > 0)
                    matched.AddRange(have);
                else
                    missing.AddRange(members);
            }

            if (overallYears is not null)
            {
                coveredWeight += OverallYearsWeight * overallYears.Credit;
                totalWeight += OverallYearsWeight;
            }

            // Skills that covered a job skill through the hierarchy aren't extra
            HashSet<string> used = new(matched.SelectMany(s => s.ImpliedBy ?? []), StringComparer.OrdinalIgnoreCase);
            List<ScoredSkill> extra = [.. resume.Where(pair => !job.Contains(pair.Key) && !used.Contains(pair.Key))
                .Select(pair => Score(pair.Key, pair.Value, false))];

            double coverage = totalWeight > 0 ? coveredWeight / totalWeight : 0.0;

            return new SkillReport(Sort(matched), Sort(missing), Sort(extra), overallYears, coverage);
        }

        /// <summary>
        /// Method to trim and dedupe non-empty skills
        /// </summary>
        /// <param name="skills">List of skills</param>
        /// <returns>Trimmed and deduped list of skills</returns>
        private static HashSet<string> Distinct(IEnumerable<string> skills)
        {
            HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
            foreach (string skill in skills)
            {
                string trimmed = skill.Trim();
                if (trimmed.Length > 0) set.Add(trimmed);
            }
            return set;
        }

        /// <summary>
        /// Trim and dedupe resume skills, keeping the section with the strongest evidence
        /// </summary>
        private static Dictionary<string, SectionType> BestSections(
            IEnumerable<(string Skill, SectionType Section)> mentions)
        {
            Dictionary<string, SectionType> best = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (skill, section) in mentions)
            {
                string trimmed = skill.Trim();
                if (trimmed.Length == 0) continue;
                if (!best.TryGetValue(trimmed, out SectionType current)
                        || EvidenceFor(section) > EvidenceFor(current))
                    best[trimmed] = section;
            }
            return best;
        }

        /// <summary>
        /// The general skills the resume's specific ones imply: each with the strongest
        /// section, the skills that imply it (strongest first), and the most months any shows.
        /// </summary>
        private static (Dictionary<string, (SectionType Section, IReadOnlyList<string> Skills)> Via, Dictionary<string, int> Months) Implied(
            Dictionary<string, SectionType> resume,
            IReadOnlyDictionary<string, IReadOnlySet<string>>? parents,
            IReadOnlyDictionary<string, int>? shownMonths)
        {
            Dictionary<string, (SectionType Section, IReadOnlyList<string> Skills)> via = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> months = new(StringComparer.OrdinalIgnoreCase);
            if (parents is null) return (via, months);

            // Strongest evidence first, then alphabetical, so a tie always names the same skill
            foreach (var (skill, section) in resume
                .OrderByDescending(p => EvidenceFor(p.Value))
                .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!parents.TryGetValue(skill, out IReadOnlySet<string>? generals)) continue;
                int shown = shownMonths?.GetValueOrDefault(skill) ?? 0;
                foreach (string general in generals)
                {
                    if (via.TryGetValue(general, out var best))
                        ((List<string>)best.Skills).Add(skill);
                    else
                        via[general] = (section, new List<string> { skill });
                    months[general] = Math.Max(months.GetValueOrDefault(general), shown);
                }
            }
            return (via, months);
        }

        /// <summary>
        /// Helper method to sort skills: required before nice to have, single skills before
        /// groups of alternatives (kept together), then by demand.
        /// </summary>
        private static List<ScoredSkill> Sort(List<ScoredSkill> skills) =>
            [.. skills.OrderBy(s => s.Preferred).ThenBy(s => s.Group ?? -1).ThenByDescending(s => s.Weight)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
