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
        /// A skill with it's O*NET demand tier and where the resume shows it
        /// </summary>
        /// <param name="Name">Skill</param>
        /// <param name="Tier">O*NET demand tier: 2 = hot, 1 = in demand, 0 = other</param>
        /// <param name="Section">The strongest resume section the skill appears in;
        /// null for a job description skill the resume doesn't have</param>
        public sealed record ScoredSkill(string Name, int Tier, SectionType? Section = null)
        {
            /// <summary>
            /// The weight used for coverage: <c>Tier + 1</c>
            /// </summary>
            public int Weight => Tier + 1;

            /// <summary>
            /// How strongly the resume backs this skill, from <see cref="Section"/>
            /// </summary>
            public double Evidence => Section is { } s ? EvidenceFor(s) : 0.0;
        }

        /// <summary>
        /// This is the result of comparing.
        /// </summary>
        /// <param name="Matched">Job description skills the resume has, important first</param>
        /// <param name="Missing">Job descrption skills the resume is missing, most important first</param>
        /// <param name="Extra">Resume skills the job descrption does not ask for, most important first</param>
        /// <param name="WeightCoverage">The share of total job description skill weight the resume covers,
        /// scaled by how strongly each matched skill is evidenced, in <c>[0, 1]</c></param>
        public sealed record SkillReport(
            IReadOnlyList<ScoredSkill> Matched,
            IReadOnlyList<ScoredSkill> Missing,
            IReadOnlyList<ScoredSkill> Extra,
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
        public static SkillReport Match(
            IEnumerable<(string Skill, SectionType Section)> resumeMentions,
            IEnumerable<string> jobSkills,
            IReadOnlyDictionary<string, int> tiers)
        {
            Dictionary<string, SectionType> resume = BestSections(resumeMentions);
            HashSet<string> job = Distinct(jobSkills);

            List<ScoredSkill> matched = [];
            List<ScoredSkill> missing = [];
            double coveredWeight = 0;
            int totalWeight = 0;

            foreach (string name in job)
            {
                if (resume.TryGetValue(name, out SectionType section))
                {
                    ScoredSkill skill = Score(name, tiers, section);
                    matched.Add(skill);
                    coveredWeight += skill.Weight * skill.Evidence;
                    totalWeight += skill.Weight;
                }
                else
                {
                    ScoredSkill skill = Score(name, tiers);
                    missing.Add(skill);
                    totalWeight += skill.Weight;
                }
            }

            List<ScoredSkill> extra = [.. resume.Where(pair => !job.Contains(pair.Key))
                .Select(pair => Score(pair.Key, tiers, pair.Value))];

            double coverage = totalWeight > 0 ? coveredWeight / totalWeight : 0.0;

            return new SkillReport(Sort(matched), Sort(missing), Sort(extra), coverage);
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
        /// Helper object that returns score with tier
        /// </summary>
        private static ScoredSkill Score(string name, IReadOnlyDictionary<string, int> tiers,
            SectionType? section = null)
        {
            int tier = tiers.TryGetValue(name, out int t) ? t : 0;
            return new ScoredSkill(name, tier, section);
        }

        /// <summary>
        /// Helper method to sort skills.
        /// </summary>
        private static List<ScoredSkill> Sort(List<ScoredSkill> skills) =>
            [.. skills.OrderByDescending(s => s.Weight).ThenBy(s => s.Name,
                StringComparer.OrdinalIgnoreCase)];
    }
}
