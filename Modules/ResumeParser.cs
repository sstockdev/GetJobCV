using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GetJobCV.Modules
{
    /// <summary>
    /// A date or date range from a resume, e.g. "Jan 2024 - Present".
    /// </summary>
    /// <param name="Text">The dates as written</param>
    /// <param name="Start">First month, or null if it couldn't be read</param>
    /// <param name="End">Last month; null when <paramref name="IsCurrent"/> or for a single date</param>
    /// <param name="IsCurrent">True for "Present", "Current", or "Now"</param>
    public sealed record DateRange(string Text, DateOnly? Start, DateOnly? End, bool IsCurrent);

    /// <summary>
    /// Name and contact details from the top of the resume.
    /// </summary>
    public sealed record ContactInfo(string? Name, string? Email, string? Phone, string? GitHub, string? LinkedIn);

    /// <summary>
    /// One school entry from the Education section.
    /// </summary>
    public sealed record EducationEntry(string? Degree, string? School, string? Location, DateRange? Dates, string? Gpa);

    /// <summary>
    /// One role from an Experience, Leadership, or Volunteer section.
    /// </summary>
    public sealed record ExperienceEntry(
        string? Title, string? Organization, string? Location, DateRange Dates, SectionType Section);

    /// <summary>
    /// The resume as structured data instead of free text.
    /// </summary>
    public sealed record ResumeRecord(
        ContactInfo Contact,
        IReadOnlyList<EducationEntry> Education,
        IReadOnlyList<ExperienceEntry> Experience);

    /// <summary>
    /// Turns segmented resume text into a <see cref="ResumeRecord"/>. Pimpalkar et al.
    /// (Section III.A): "A resume parser transforms unstructured data into structured data".
    /// Relies on common layouts: an entry starts at a non-bullet line ending in a date,
    /// and "Organization | Location" lines.
    /// </summary>
    public static partial class ResumeParser
    {
        /// <summary>
        /// Sections whose entries are roles with a title, organization, and dates.
        /// </summary>
        private static readonly HashSet<SectionType> RoleSections =
            [SectionType.Experience, SectionType.Leadership, SectionType.Volunteer];

        /// <summary>
        /// Words that mark a line as a job title rather than an organization.
        /// </summary>
        private static readonly HashSet<string> TitleWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "engineer", "developer", "intern", "assistant", "analyst", "manager", "director", "lead",
            "president", "vice", "coordinator", "consultant", "specialist", "technician", "scientist",
            "architect", "officer", "administrator", "associate", "designer", "researcher", "tutor",
            "teacher", "instructor", "volunteer", "member", "chair", "founder", "owner", "head",
            "supervisor", "representative", "programmer", "fellow", "trainee", "apprentice"
        };

        private static readonly string[] Bullets = ["•", "-", "*", "–", "▪", "◦", "●", "○", "■"];

        /// <summary>
        /// Build the structured record.
        /// </summary>
        /// <param name="sections">Output of <see cref="SectionSegmenter.Segment"/></param>
        /// <param name="socials">Profile links already found by <see cref="SocialExtractor"/></param>
        public static ResumeRecord Parse(IReadOnlyList<ResumeSection> sections, Socials socials)
        {
            ContactInfo contact = ParseContact(
                sections.FirstOrDefault(s => s.Type == SectionType.Contact)?.Text ?? "", socials);

            List<EducationEntry> education = [.. sections
                .Where(s => s.Type == SectionType.Education)
                .SelectMany(s => ParseEducation(s.Text))];

            List<ExperienceEntry> experience = [.. sections
                .Where(s => RoleSections.Contains(s.Type))
                .SelectMany(s => SplitEntries(s.Text)
                    .Select(entry => ParseRole(entry, s.Type)))
                .OfType<ExperienceEntry>()];

            return new ResumeRecord(contact, education, experience);
        }

        private static ContactInfo ParseContact(string text, Socials socials)
        {
            string[] lines = SplitLines(text);

            // The name is almost always the first line: short, no digits, no '@'
            string? name = lines.FirstOrDefault() is { } first
                && first.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 1 and <= 4
                && !first.Any(char.IsDigit) && !first.Contains('@')
                    ? first
                    : null;

            string? email = EmailRegex().Match(text) is { Success: true } e ? e.Value : null;
            string? phone = PhoneRegex().Match(text) is { Success: true } p ? p.Value.Trim() : null;

            return new ContactInfo(name, email, phone, socials.GitHub, socials.LinkedIn);
        }

        /// <summary>
        /// Degree and school lines come in either order, and the dates can sit on either
        /// one, so a new entry starts only when a degree or school repeats.
        /// </summary>
        private static IEnumerable<EducationEntry> ParseEducation(string text)
        {
            string? degree = null, school = null, location = null, gpa = null;
            DateRange? dates = null;

            foreach (string line in SplitLines(text))
            {
                gpa ??= GpaRegex().Match(line) is { Success: true } g ? g.Groups["gpa"].Value : null;
                if (IsBullet(line))
                    continue;

                string rest = TrySplitDate(line, out string before, out DateRange? d) ? before : line;
                bool isDegree = DegreeRegex().IsMatch(rest);
                bool isSchool = !isDegree && SchoolRegex().IsMatch(rest);

                if ((isDegree && degree is not null) || (isSchool && school is not null))
                {
                    yield return new EducationEntry(degree, school, location, dates, gpa);
                    degree = school = location = gpa = null;
                    dates = null;
                }

                if (isDegree)
                    degree = rest;
                else if (isSchool)
                    (school, location) = SplitLocation(rest);
                dates ??= d;
            }

            if (degree is not null || school is not null)
                yield return new EducationEntry(degree, school, location, dates, gpa);
        }

        /// <summary>
        /// A role's first line holds the dates; its other header line (before the bullets)
        /// is usually the organization. Title words decide which is which, so both
        /// "Title / Org" and "Org / Title" layouts work.
        /// </summary>
        private static ExperienceEntry? ParseRole(string[] entry, SectionType section)
        {
            if (!TrySplitDate(entry[0], out string dateLine, out DateRange? dates))
                return null;

            string? other = entry.Skip(1).TakeWhile(l => !IsBullet(l)).FirstOrDefault();

            string? title = dateLine.Length > 0 ? dateLine : null;
            string? orgLine = other;
            if (other is not null && LooksLikeTitle(other) && !LooksLikeTitle(dateLine))
                (title, orgLine) = (other, title);

            (string? org, string? location) = orgLine is null ? (null, null) : SplitLocation(orgLine);
            return new ExperienceEntry(title, org, location, dates!, section);
        }

        /// <summary>
        /// Split a section into entries. A new entry starts at a non-bullet line that
        /// ends in a date. Lines starting lowercase are wrapped bullet text, not entries.
        /// Text before the first dated line is its own entry.
        /// </summary>
        private static IEnumerable<string[]> SplitEntries(string text)
        {
            List<string> current = [];
            foreach (string line in SplitLines(text))
            {
                if (current.Count > 0 && !IsBullet(line) && !char.IsLower(line[0])
                        && TrySplitDate(line, out _, out _))
                {
                    yield return [.. current];
                    current.Clear();
                }
                current.Add(line);
            }
            if (current.Count > 0)
                yield return [.. current];
        }

        /// <summary>
        /// "DigiKey | Thief River Falls, MN" → ("DigiKey", "Thief River Falls, MN").
        /// Without a '|' separator the whole line is the name.
        /// </summary>
        private static (string? Name, string? Location) SplitLocation(string line)
        {
            string[] parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            string name = parts[0];
            string? location = parts.Length > 1 ? parts[1].TrimEnd(',', ' ') : null;
            return (name.Length > 0 ? name : null, string.IsNullOrEmpty(location) ? null : location);
        }

        /// <summary>
        /// Split trailing dates off a line: "Software Engineer Jun 2026 - Aug 2026"
        /// → ("Software Engineer", Jun 2026 – Aug 2026).
        /// </summary>
        public static bool TrySplitDate(string line, out string before, out DateRange? dates)
        {
            Match m = TrailingDateRegex().Match(line);
            if (!m.Success)
            {
                (before, dates) = (line, null);
                return false;
            }

            before = line[..m.Index].TrimEnd(' ', ',', '|', '-', '–', '—');
            bool current = m.Groups["current"].Success;
            dates = new DateRange(
                m.Groups["range"].Value.Trim(),
                ParseDate(m.Groups["start"].Value),
                m.Groups["end"].Success ? ParseDate(m.Groups["end"].Value) : null,
                current);
            return true;
        }

        private static DateOnly? ParseDate(string text)
        {
            Match m = SingleDateRegex().Match(text);
            if (!m.Success || !int.TryParse(m.Groups["year"].Value, out int year))
                return null;

            int month = 1;
            if (m.Groups["mon"].Success)
                month = Array.IndexOf(Months, m.Groups["mon"].Value[..3].ToLowerInvariant()) + 1;
            else if (m.Groups["num"].Success)
                month = int.Parse(m.Groups["num"].Value);

            return month is >= 1 and <= 12 ? new DateOnly(year, month, 1) : null;
        }

        private static readonly string[] Months =
            ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

        private static bool LooksLikeTitle(string line) =>
            line.Split([' ', ',', '/', '-'], StringSplitOptions.RemoveEmptyEntries).Any(TitleWords.Contains);

        private static bool IsBullet(string line) => Bullets.Any(line.StartsWith);

        private static string[] SplitLines(string text) =>
            text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // Generate regex at compile time

        private const string DatePattern =
            @"(?:(?<mon>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+(?<year>\d{4})|(?<num>\d{1,2})/(?<year>\d{4})|(?<year>(?:19|20)\d{2}))";

        [GeneratedRegex(@"^" + DatePattern + @"$", RegexOptions.IgnoreCase)]
        private static partial Regex SingleDateRegex();

        /// <summary>
        /// A date or date range at the end of a line. The range's start and end
        /// are captured as text and parsed with <see cref="SingleDateRegex"/>.
        /// </summary>
        [GeneratedRegex(
            @"(?<![\w/])(?<range>(?<start>(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{4}|\d{1,2}/\d{4}|(?:19|20)\d{2})" +
            @"(?:\s*(?:-|–|—|to)\s*(?:(?<current>Present|Current|Now|Today)|(?<end>(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{4}|\d{1,2}/\d{4}|(?:19|20)\d{2})))?)\s*$",
            RegexOptions.IgnoreCase)]
        private static partial Regex TrailingDateRegex();

        [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")]
        private static partial Regex EmailRegex();

        [GeneratedRegex(@"(?:\+\d{1,3}[\s.-]?)?\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}")]
        private static partial Regex PhoneRegex();

        [GeneratedRegex(@"\b(?:B\.?S\.?|B\.?A\.?|B\.?Sc\.?|M\.?S\.?|M\.?A\.?|M\.?Sc\.?|MBA|Ph\.?D\.?|A\.?A\.?S?\.?|Bachelor|Master|Doctor|Associate|Diploma|Certificate)\b", RegexOptions.IgnoreCase)]
        private static partial Regex DegreeRegex();

        [GeneratedRegex(@"\b(?:University|College|Institute|School|Academy|Polytechnic)\b", RegexOptions.IgnoreCase)]
        private static partial Regex SchoolRegex();

        [GeneratedRegex(@"GPA\s*:?\s*(?<gpa>\d\.\d{1,2}(?:\s*/\s*\d\.\d{1,2})?)", RegexOptions.IgnoreCase)]
        private static partial Regex GpaRegex();
    }
}
