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
        /// A skill with it's O*NET demand tier
        /// </summary>
        /// <param name="Name">Skill</param>
        /// <param name="Tier">O*NET demand tier: 2 = hot, 1 = in demand, 0 = other</param>
        public sealed record ScoredSkill(string Name, int Tier)
        {
            /// <summary>
            /// The weight used for coverage: <c>Tier + 1</c>
            /// </summary>
            public int Weight => Tier + 1;
        }

        /// <summary>
        /// This is the result of comparing.
        /// </summary>
        /// <param name="Matched">Job description skills the resume has, important first</param>
        /// <param name="Missing">Job descrption skills the resume is missing, most important first</param>
        /// <param name="Extra">Resume skills the job descrption does not ask for, most important first</param>
        /// <param name="WeightCoverage">The shared total job description skill weight the resume covers,
        /// in <c>[0, 1]</c></param>
        public sealed record SkillReport(
            IReadOnlyList<ScoredSkill> Matched,
            IReadOnlyList<ScoredSkill> Missing,
            IReadOnlyList<ScoredSkill> Extra,
            double WeightCoverage);

        public static SkillReport Match(
            IEnumerable<string> resumeSkills,
            IEnumerable<string> jobSkills,
            IReadOnlyDictionary<string, int> tiers)
        {
            HashSet<string> resume = Distinct(resumeSkills);
            HashSet<string> job = Distinct(jobSkills);

            List<ScoredSkill> matched = [];
            List<ScoredSkill> missing = [];
            int coveredWeight = 0;
            int totalWeight = 0;

            foreach (string name in job)
            {
                ScoredSkill skill = Score(name, tiers);
                totalWeight += skill.Weight;
                if (resume.Contains(name))
                {
                    matched.Add(skill);
                    coveredWeight += skill.Weight;
                }
                else
                {
                    missing.Add(skill);
                }
            }

            List<ScoredSkill> extra = [.. resume.Where(name => !job.Contains(name))
                .Select(name => Score(name, tiers))];

            double coverage = totalWeight > 0 ? (double)coveredWeight / totalWeight : 0.0;

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
        /// Helper object that returns score with tier
        /// </summary>
        private static ScoredSkill Score(string name, IReadOnlyDictionary<string, int> tiers)
        {
            int tier = tiers.TryGetValue(name, out int t) ? t : 0;
            return new ScoredSkill(name, tier);
        }

        /// <summary>
        /// Helper method to sort skills.
        /// </summary>
        private static List<ScoredSkill> Sort(List<ScoredSkill> skills) =>
            [.. skills.OrderByDescending(s => s.Weight).ThenBy(s => s.Name,
                StringComparer.OrdinalIgnoreCase)];
    }
}
