using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GetJobCV.Modules
{
    /// <summary>
    /// Finds groups of job description skills where any one will do, so a resume that has
    /// one isn't marked down for the rest:
    /// <list type="bullet">
    /// <item>An inline list of short topics: "• Machine Learning • Quantum Computing • Game
    /// Development", across consecutive lines</item>
    /// <item>A lead-in: "one of the following", "any of", "such as", "e.g.", ... for the rest of
    /// the sentence, or for the bullets under it when the lead-in line ends with a colon</item>
    /// <item>Skills joined by "or": "Python, Java, or Go"</item>
    /// </list>
    /// A plain list ("SQL, Docker, and Git") stays required skill by skill, and a skill that's
    /// also required on its own elsewhere leaves its group.
    /// </summary>
    public static partial class AlternativeGroups
    {
        public static IReadOnlyList<IReadOnlySet<string>> Find(string jobDescription, NerExtractor ner) =>
            Find(jobDescription, clause => ner.Extract(clause).SkillMentions ?? []);

        /// <param name="findSkills">Skills in a clause, written as named; a stub in tests</param>
        public static IReadOnlyList<IReadOnlySet<string>> Find(
            string jobDescription, Func<string, IEnumerable<string>> findSkills) =>
            Find(jobDescription, clause => findSkills(clause).Select(s => new NerExtractor.SkillMention(s, s)));

        /// <param name="findMentions">Skills in a clause, with how each is written</param>
        public static IReadOnlyList<IReadOnlySet<string>> Find(
            string jobDescription, Func<string, IEnumerable<NerExtractor.SkillMention>> findMentions)
        {
            List<HashSet<string>> groups = [];
            HashSet<string> alone = new(StringComparer.OrdinalIgnoreCase);

            HashSet<string>? open = null;          // a group still collecting lines
            bool openIsTopicList = false;          // inline topic lists merge across lines

            void Close()
            {
                if (open is { Count: >= 2 })
                    groups.Add(open);
                open = null;
            }

            foreach (string raw in jobDescription.Split('\n'))
            {
                // "e.g." and "i.e." would end a sentence for the clause splitter
                string line = AbbreviationRegex().Replace(raw.Trim(), m => m.Value.Replace(".", ""));
                if (line.Length == 0)
                {
                    Close();
                    continue;
                }

                // "• Machine Learning • Database Systems • Quantum Computing"
                if (IsTopicList(line))
                {
                    if (open is null || !openIsTopicList)
                    {
                        Close();
                        open = new(StringComparer.OrdinalIgnoreCase);
                        openIsTopicList = true;
                    }
                    open.UnionWith(Names(line));
                    continue;
                }

                // Bullets under "Experience with one of the following:"
                if (open is not null && !openIsTopicList && Bullets.Any(line.StartsWith))
                {
                    open.UnionWith(Names(line));
                    continue;
                }
                Close();

                if (line.EndsWith(':') && LeadInRegex().IsMatch(line))
                {
                    open = new(StringComparer.OrdinalIgnoreCase);
                    openIsTopicList = false;
                    alone.UnionWith(Names(line[..LeadInRegex().Match(line).Index]));
                    continue;
                }

                foreach (string clause in RequirementExtractor.SplitClauses(line))
                    ReadClause(clause);
            }
            Close();

            // Required on its own somewhere: not an alternative
            List<IReadOnlySet<string>> result = [];
            foreach (HashSet<string> group in groups)
            {
                group.ExceptWith(alone);
                if (group.Count >= 2)
                    result.Add(group);
            }
            return result;

            IEnumerable<string> Names(string text) => findMentions(text).Select(m => m.Name);

            void ReadClause(string clause)
            {
                // Mentions in reading order, each found by how it's written
                List<(int At, int End, string Name)> mentions = [.. findMentions(clause)
                    .Select(m => (At: RequirementExtractor.WholeWordIndex(clause, m.Text), m.Text.Length, m.Name))
                    .Where(m => m.At >= 0)
                    .Select(m => (m.At, m.At + m.Length, m.Name))
                    .OrderBy(m => m.At)];
                HashSet<int> grouped = [];

                // "…such as AWS, Azure, or GCP": everything after the lead-in
                if (LeadInRegex().Match(clause) is { Success: true } leadIn)
                {
                    List<int> after = [.. Enumerable.Range(0, mentions.Count).Where(i => mentions[i].At > leadIn.Index)];
                    if (after.Count >= 2)
                    {
                        groups.Add(new(after.Select(i => mentions[i].Name), StringComparer.OrdinalIgnoreCase));
                        grouped.UnionWith(after);
                    }
                }

                // "Python, Java, or Go": a run of skills joined by commas, slashes, and "or"
                int start = 0;
                for (int i = 1; i <= mentions.Count; i++)
                {
                    bool joined = i < mentions.Count && OrJoinRegex().IsMatch(clause[mentions[i - 1].End..mentions[i].At]);
                    if (joined)
                        continue;
                    List<int> run = [.. Enumerable.Range(start, i - start).Where(k => !grouped.Contains(k))];
                    bool hasOr = Enumerable.Range(start, i - start - 1)
                        .Any(k => OrWordRegex().IsMatch(clause[mentions[k].End..mentions[k + 1].At]));
                    if (run.Count >= 2 && hasOr)
                    {
                        groups.Add(new(run.Select(k => mentions[k].Name), StringComparer.OrdinalIgnoreCase));
                        grouped.UnionWith(run);
                    }
                    start = i;
                }

                for (int i = 0; i < mentions.Count; i++)
                    if (!grouped.Contains(i))
                        alone.Add(mentions[i].Name);
            }
        }

        /// <summary>
        /// A line that is only a list of short items between bullet glyphs: three or more
        /// items of at most six words, none ending a sentence, and no "required" or "must".
        /// Bars and middle dots don't count: "Python | Django | Postgres" is usually a stack
        /// where every item is needed.
        /// </summary>
        private static bool IsTopicList(string line)
        {
            if (RequiredWordRegex().IsMatch(line))
                return false;
            string body = line.TrimStart(Bullets.SelectMany(b => b).ToArray()).Trim();
            string[] items = [.. TopicSeparatorRegex().Split(body).Select(s => s.Trim()).Where(s => s.Length > 0)];
            return items.Length >= 3 && items.All(item =>
                item.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6 && !item.EndsWith('.'));
        }

        private static readonly string[] Bullets = ["•", "-", "*", "–", "▪", "◦", "●", "○", "■"];

        // Generate regex at compile time

        [GeneratedRegex(@"\s*[•▪●◦]\s*")]
        private static partial Regex TopicSeparatorRegex();

        [GeneratedRegex(@"\b(?:required|must|mandatory)\b", RegexOptions.IgnoreCase)]
        private static partial Regex RequiredWordRegex();

        [GeneratedRegex(@"\be\.g\.|\bi\.e\.", RegexOptions.IgnoreCase)]
        private static partial Regex AbbreviationRegex();

        /// <summary>
        /// Words that introduce a choice or examples.
        /// </summary>
        [GeneratedRegex(
            @"\b(?:one|any|at\s+least\s+one|one\s+or\s+more)\s+of\s+(?:the\s+)?(?:following|these|below)\b|" +
            @"\bany\s+of\b|\bsuch\s+as\b|\beg\b|\bfor\s+(?:example|instance)\b|" +
            @"\b(?:areas|domains|fields|topics)\s+(?:like|including)\b",
            RegexOptions.IgnoreCase)]
        private static partial Regex LeadInRegex();

        /// <summary>
        /// What may sit between two skills in an "or" list: ", ", "/", "or", ", or", "and/or".
        /// </summary>
        [GeneratedRegex(@"^\s*(?:,|/|,?\s*(?:and/)?or)\s*$", RegexOptions.IgnoreCase)]
        private static partial Regex OrJoinRegex();

        [GeneratedRegex(@"\bor\b", RegexOptions.IgnoreCase)]
        private static partial Regex OrWordRegex();
    }
}
