using System;
using System.Collections.Generic;
using System.Text;

namespace GetJobCV.Modules
{
    /// <summary>
    /// Years of professional experience, overall and per skill.
    /// </summary>
    /// <param name="TotalMonths">Months covered by at least one role</param>
    /// <param name="SkillMonths">Skill to months covered by roles that mention it, most first</param>
    public sealed record ExperienceSummary(int TotalMonths, IReadOnlyList<(string Skill, int Months)> SkillMonths)
    {
        public double TotalYears => TotalMonths / 12.0;
    }

    /// <summary>
    /// Works out years of experience from role date ranges. Chavan et al. (Section 6)
    /// found that "Skills and Experience showed significantly improved shortlisting results".
    /// Overlapping roles (two jobs at once) are only counted once.
    /// </summary>
    public static class ExperienceCalculator
    {
        /// <summary>
        /// Summarize experience from Experience section roles. Leadership and Volunteer
        /// roles aren't counted as professional experience.
        /// </summary>
        /// <param name="roles">Each role with the skills found in its text</param>
        /// <param name="asOf">Today; "Present" ends here and later dates are cut off here</param>
        public static ExperienceSummary Summarize(
            IEnumerable<(ExperienceEntry Role, IEnumerable<string> Skills)> roles, DateOnly asOf)
        {
            var professional = roles
                .Where(r => r.Role.Section == SectionType.Experience)
                .Select(r => (Span: ToMonths(r.Role.Dates, asOf), r.Skills))
                .Where(r => r.Span is not null)
                .Select(r => (Span: r.Span!.Value, r.Skills))
                .ToList();

            int total = UnionMonths(professional.Select(r => r.Span));

            Dictionary<string, List<(int, int)>> spansBySkill = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (span, skills) in professional)
                foreach (string skill in skills)
                {
                    if (!spansBySkill.TryGetValue(skill, out var spans))
                        spansBySkill[skill] = spans = [];
                    spans.Add(span);
                }

            List<(string, int)> skillMonths = [.. spansBySkill
                .Select(pair => (Skill: pair.Key, Months: UnionMonths(pair.Value)))
                .OrderByDescending(s => s.Months)
                .ThenBy(s => s.Skill, StringComparer.OrdinalIgnoreCase)];

            return new ExperienceSummary(total, skillMonths);
        }

        /// <summary>
        /// A role's months as an inclusive [first, last] range of month numbers
        /// (year * 12 + month). Jun 2026 - Aug 2026 is 3 months.
        /// </summary>
        /// <returns>null when the start is unknown or after <paramref name="asOf"/></returns>
        private static (int First, int Last)? ToMonths(DateRange dates, DateOnly asOf)
        {
            if (dates.Start is not { } start)
                return null;

            int now = MonthNumber(asOf);
            int first = MonthNumber(start);
            if (first > now)
                return null;

            // A single date with no end ("May 2023") counts as one month
            int last = dates.IsCurrent ? now
                : dates.End is { } end ? MonthNumber(end)
                : first;

            return (first, Math.Clamp(last, first, now));
        }

        private static int MonthNumber(DateOnly date) => date.Year * 12 + date.Month - 1;

        /// <summary>
        /// Months covered by any of the ranges, counting overlaps once.
        /// </summary>
        private static int UnionMonths(IEnumerable<(int First, int Last)> spans)
        {
            int total = 0;
            int coveredThrough = int.MinValue;
            foreach (var (first, last) in spans.OrderBy(s => s.First))
            {
                int from = Math.Max(first, coveredThrough + 1);
                if (last >= from)
                {
                    total += last - from + 1;
                    coveredThrough = last;
                }
            }
            return total;
        }
    }
}
