using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GetJobCV.Modules
{
    /// <summary>
    /// The kind of content a resume section holds.
    /// </summary>
    public enum SectionType
    {
        /// <summary>Everything before the first heading: name and contact details</summary>
        Contact,
        Summary,
        Education,
        Experience,
        Skills,
        Projects,
        Leadership,
        Certifications,
        Awards,
        Publications,
        Volunteer,
        Interests
    }

    /// <summary>
    /// One section of a resume.
    /// </summary>
    /// <param name="Type">What the section holds</param>
    /// <param name="Heading">The heading as written in the resume; empty for <see cref="SectionType.Contact"/></param>
    /// <param name="Text">The section body, without the heading line</param>
    public sealed record ResumeSection(SectionType Type, string Heading, string Text);

    /// <summary>
    /// Splits raw resume text into sections (Education, Experience, Skills, ...) by
    /// recognizing heading lines. Pimpalkar et al. list "separate resume into parts"
    /// as an open challenge (Section IV.6).
    /// </summary>
    public static partial class SectionSegmenter
    {
        /// <summary>
        /// Full heading phrases, checked first so "Leadership Experience" isn't read as Experience.
        /// </summary>
        private static readonly Dictionary<string, SectionType> Headings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["summary"] = SectionType.Summary,
            ["professional summary"] = SectionType.Summary,
            ["profile"] = SectionType.Summary,
            ["about me"] = SectionType.Summary,
            ["objective"] = SectionType.Summary,
            ["career objective"] = SectionType.Summary,

            ["education"] = SectionType.Education,
            ["academic background"] = SectionType.Education,
            ["coursework"] = SectionType.Education,
            ["relevant coursework"] = SectionType.Education,

            ["experience"] = SectionType.Experience,
            ["work history"] = SectionType.Experience,
            ["employment"] = SectionType.Experience,
            ["employment history"] = SectionType.Experience,
            ["internships"] = SectionType.Experience,
            ["internship"] = SectionType.Experience,

            ["skills"] = SectionType.Skills,
            ["technical skills"] = SectionType.Skills,
            ["key skills"] = SectionType.Skills,
            ["core competencies"] = SectionType.Skills,
            ["technologies"] = SectionType.Skills,

            ["projects"] = SectionType.Projects,
            ["personal projects"] = SectionType.Projects,
            ["academic projects"] = SectionType.Projects,

            ["leadership"] = SectionType.Leadership,
            ["leadership experience"] = SectionType.Leadership,
            ["activities"] = SectionType.Leadership,
            ["extracurricular activities"] = SectionType.Leadership,

            ["certifications"] = SectionType.Certifications,
            ["certificates"] = SectionType.Certifications,
            ["licenses and certifications"] = SectionType.Certifications,

            ["awards"] = SectionType.Awards,
            ["honors"] = SectionType.Awards,
            ["honors and awards"] = SectionType.Awards,
            ["achievements"] = SectionType.Awards,

            ["publications"] = SectionType.Publications,
            ["research"] = SectionType.Publications,

            ["volunteer"] = SectionType.Volunteer,
            ["volunteering"] = SectionType.Volunteer,
            ["volunteer experience"] = SectionType.Volunteer,
            ["community service"] = SectionType.Volunteer,

            ["interests"] = SectionType.Interests,
            ["hobbies"] = SectionType.Interests,
        };

        /// <summary>
        /// Most headings are longer variants ending in one of these words
        /// ("RELEVANT WORK EXPERIENCE", "Programming Skills").
        /// </summary>
        private static readonly Dictionary<string, SectionType> HeadingLastWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["experience"] = SectionType.Experience,
            ["skills"] = SectionType.Skills,
            ["languages"] = SectionType.Skills,
            ["tools"] = SectionType.Skills,
            ["technologies"] = SectionType.Skills,
            ["projects"] = SectionType.Projects,
            ["education"] = SectionType.Education,
            ["certifications"] = SectionType.Certifications,
            ["awards"] = SectionType.Awards,
        };

        /// <summary>
        /// Longest heading we accept, in words. Longer lines are body text.
        /// </summary>
        private const int MaxHeadingWords = 4;

        /// <summary>
        /// Split resume text into sections, in document order. Text before the first
        /// heading becomes a <see cref="SectionType.Contact"/> section. A heading that
        /// appears twice produces two sections.
        /// </summary>
        /// <param name="rawText">Raw text extracted from a PDF, one line per text line</param>
        /// <returns>The sections; empty sections are dropped</returns>
        public static IReadOnlyList<ResumeSection> Segment(string rawText)
        {
            List<ResumeSection> sections = [];
            if (string.IsNullOrWhiteSpace(rawText))
                return sections;

            SectionType type = SectionType.Contact;
            string heading = "";
            StringBuilder body = new();

            foreach (string rawLine in rawText.Split('\n'))
            {
                string line = rawLine.Trim();
                if (TryParseHeading(line, out SectionType next))
                {
                    AddSection(sections, type, heading, body);
                    type = next;
                    heading = line;
                    body.Clear();
                }
                else if (line.Length > 0)
                {
                    body.AppendLine(line);
                }
            }

            AddSection(sections, type, heading, body);
            return sections;
        }

        /// <summary>
        /// Decide whether a line is a section heading.
        /// </summary>
        public static bool TryParseHeading(string line, out SectionType type)
        {
            type = default;

            // "Skills:" and "— EDUCATION —" are headings too
            string name = MultiSpaceRegex().Replace(line.Trim().Trim(':', '-', '–', '—', '|', '•', ' '), " ");
            if (name.Length == 0)
                return false;

            if (Headings.TryGetValue(name, out type))
                return true;

            string[] words = name.Split(' ');
            if (words.Length > MaxHeadingWords || !LooksLikeHeading(words))
                return false;

            return HeadingLastWords.TryGetValue(words[^1], out type);
        }

        /// <summary>
        /// All caps ("WORK EXPERIENCE") or Title Case ("Work Experience"), with small joining
        /// words allowed, so a body line that happens to end in "skills" isn't taken as a heading.
        /// </summary>
        private static bool LooksLikeHeading(string[] words)
        {
            if (words.All(w => !w.Any(char.IsLower)))
                return true;
            return words.All(w => char.IsUpper(w[0]) || w is "and" or "&" or "of");
        }

        private static void AddSection(List<ResumeSection> sections, SectionType type, string heading, StringBuilder body)
        {
            string text = body.ToString().TrimEnd();
            if (text.Length > 0)
                sections.Add(new ResumeSection(type, heading, text));
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex MultiSpaceRegex();
    }
}
