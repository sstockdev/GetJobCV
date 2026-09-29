using System;
using System.Collections.Generic;
using System.Text;

namespace GetJobCV.Modules
{
    /// <summary>
    /// Loads skill phrases for the NER gazetteer from the bundled O*NET file
    /// You can run the skills resource generator with: dotnet run --project tools\OnetGazetteer
    /// </summary>
    public static class SkillsGazetteer
    {
        public const string DefaultPath = "Resources/onet_skills.tsv";
        public const string OverlayPath = "Resources/skills.txt";

        public sealed record SkillEntry(string Name, string Category, int Weight);

        /// <summary>
        /// O*NET base and curated overlay, deduped case-insensitively names only.
        /// </summary>
        public static IReadOnlyList<string> Load(string path = DefaultPath, string overlayPath = OverlayPath)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<string> names = [];
            foreach (SkillEntry e in LoadCategorized(path))
                if (seen.Add(e.Name)) names.Add(e.Name);
            foreach (string n in LoadOverlay(overlayPath))
                if (!n.StartsWith(CaseSensitivePrefix) && seen.Add(n)) names.Add(n);
            foreach (string form in WordForms(overlayPath).Keys)
                if (seen.Add(form)) names.Add(form);
            foreach (ShortName s in ShortNames(path, overlayPath).Where(s => !s.ExactCase))
                if (seen.Add(s.Form)) names.Add(s.Form);
            return names;
        }

        /// <summary>
        /// A way of writing an O*NET "... software" entry that people actually use, and the
        /// name it's reported as. <paramref name="ExactCase"/> forms are acronyms, spotted
        /// case-sensitively like the overlay's '=' lines.
        /// </summary>
        private sealed record ShortName(string Form, string Name, bool ExactCase);

        private const string SoftwareSuffix = " software";

        /// <summary>
        /// Short names for O*NET entries named as a kind of software, which job descriptions
        /// and resumes don't write out. "Programmable logic controller PLC software" is
        /// written "PLC", "PLCs" or "programmable logic controller" and reported as PLC; a
        /// product name with a capital inside a word ("HubSpot software", "ESRI software") is
        /// written without "software". A kind of software named in ordinary words gets no
        /// short name: "Order management" and "Data warehouse" are more often what someone
        /// worked on than a skill. Neither does an entry the overlay already aliases.
        /// </summary>
        private static List<ShortName> ShortNames(string path, string overlayPath)
        {
            HashSet<string> overlaid = [.. OverlayAliasKeys(overlayPath)];
            HashSet<string> known = new(LoadCategorized(path).Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            known.UnionWith(LoadOverlay(overlayPath).Select(n => n.TrimStart(CaseSensitivePrefix).Trim()));

            List<ShortName> shortNames = [];
            foreach (SkillEntry e in LoadCategorized(path))
            {
                if (e.Category != "Technology" || overlaid.Contains(e.Name)
                        || !e.Name.EndsWith(SoftwareSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] words = e.Name[..^SoftwareSuffix.Length].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;

                if (words.Length > 1 && Expansion(words[..^1], words[^1]) is { } expansion)
                {
                    string acronym = words[^1];
                    shortNames.Add(new(e.Name, acronym, false));
                    shortNames.Add(new(acronym, acronym, true));
                    shortNames.Add(new(acronym + "s", acronym, true));
                    string[] expanded = expansion.Split(' ');
                    foreach (string form in Plural(expanded[^1]) is { } plural
                            ? [expansion, string.Join(' ', [.. expanded[..^1], plural])]
                            : new[] { expansion })
                        if (!known.Contains(form))
                            shortNames.Add(new(form, acronym, false));
                }
                else if (words.Any(w => w.Skip(1).Any(char.IsUpper)))
                {
                    string name = string.Join(' ', words);
                    shortNames.Add(new(e.Name, name, false));
                    shortNames.Add(new(name, name, false));
                }
            }

            // A form that's a short name of its own is left alone ("CIMS" isn't CIM's plural)
            HashSet<string> names = new(shortNames.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            return [.. shortNames.Where(s => s.Form.Equals(s.Name, StringComparison.OrdinalIgnoreCase) || !names.Contains(s.Form))];
        }

        private static readonly HashSet<string> AcronymSkippedWords = new(StringComparer.OrdinalIgnoreCase) { "and", "of", "the", "for", "to", "with", "&" };

        /// <summary>
        /// The words at the end of <paramref name="words"/> that <paramref name="acronym"/>
        /// abbreviates ("Programmable logic controller" for PLC, "computer aided design"
        /// for CAD in "Three-dimensional 3D computer aided design"), or null when it isn't an
        /// all-caps acronym of them. Hyphenated parts count as words ("Computer-aided
        /// engineering" for CAE); "and", "of" and the like don't.
        /// </summary>
        private static string? Expansion(string[] words, string acronym)
        {
            if (acronym.Length < 2 || !acronym.All(char.IsAsciiLetterUpper)) return null;

            int letter = acronym.Length;
            for (int start = words.Length - 1; start >= 0; start--)
            {
                string word = words[start].Trim(',', ';');
                if (AcronymSkippedWords.Contains(word)) continue;

                string[] parts = word.Split('-', StringSplitOptions.RemoveEmptyEntries);
                for (int p = parts.Length - 1; p >= 0; p--)
                {
                    if (letter == 0 || char.ToUpperInvariant(parts[p][0]) != acronym[letter - 1]) return null;
                    letter--;
                }
                if (letter == 0)
                    return string.Join(' ', words[start..].Select(w => w.Trim(',', ';')));
            }
            return null;
        }

        private static IEnumerable<string> OverlayAliasKeys(string overlayPath) =>
            File.Exists(overlayPath)
                ? ReadDataLines(overlayPath)
                    .Where(l => !IsHierarchyLine(l) && l.Contains('\t'))
                    .Select(l => l[..l.IndexOf('\t')].Trim().TrimStart(CaseSensitivePrefix).Trim())
                : [];

        /// <summary>
        /// Overlay lines starting with this are matched case-sensitively, for skills
        /// that collide with ordinary words or letters (CAN vs "can", C vs "c").
        /// </summary>
        public const char CaseSensitivePrefix = '=';

        /// <summary>
        /// Overlay skills that must match case-sensitively, prefix stripped.
        /// </summary>
        public static IReadOnlyList<string> LoadCaseSensitive(string overlayPath = OverlayPath, string path = DefaultPath) =>
            [.. LoadOverlay(overlayPath)
                .Where(n => n.StartsWith(CaseSensitivePrefix))
                .Select(n => n[1..].Trim())
                .Concat(ShortNames(path, overlayPath).Where(s => s.ExactCase).Select(s => s.Form))
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.Ordinal)];

        /// <summary>
        /// Converts O*NET skill to demand tier. A canonical name takes the highest tier of
        /// its aliases, so "AWS" gets the tier of "Amazon Web Services AWS software".
        /// </summary>
        public static IReadOnlyDictionary<string, int> LoadWeights(string path = DefaultPath, string overlayPath = OverlayPath)
        {
            Dictionary<string, int> tiers = new(StringComparer.OrdinalIgnoreCase);
            foreach (SkillEntry e in LoadCategorized(path))
                if (!tiers.TryGetValue(e.Name, out int existing) || e.Weight > existing)
                    tiers[e.Name] = e.Weight;

            foreach ((string alias, string canonical) in LoadAliases(overlayPath, path))
                if (tiers.TryGetValue(alias, out int tier) && tier > tiers.GetValueOrDefault(canonical))
                    tiers[canonical] = tier;
            return tiers;
        }

        /// <summary>
        /// Overlay "alias&lt;TAB&gt;Canonical" lines: alias (without any '=' prefix) to the
        /// name it's reported as, plus the <see cref="WordForms"/> of multi-word skills and
        /// the <see cref="ShortNames"/> of O*NET "... software" entries.
        /// Case-insensitive keys. Aliases don't chain.
        /// </summary>
        public static IReadOnlyDictionary<string, string> LoadAliases(string overlayPath = OverlayPath, string path = DefaultPath)
        {
            Dictionary<string, string> aliases = WordForms(overlayPath);
            Dictionary<string, string> overlay = new(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(overlayPath))
                foreach (string line in ReadDataLines(overlayPath).Where(l => !IsHierarchyLine(l)))
                {
                    int tab = line.IndexOf('\t');
                    if (tab < 0) continue;
                    string alias = line[..tab].Trim().TrimStart(CaseSensitivePrefix).Trim();
                    string canonical = line[(tab + 1)..].Trim();
                    if (alias.Length > 0 && canonical.Length > 0)
                        aliases[alias] = overlay[alias] = canonical;
                }

            // The overlay's own aliases win, and a short name it aliases reports as its target
            foreach (ShortName s in ShortNames(path, overlayPath))
            {
                string name = overlay.GetValueOrDefault(s.Name, s.Name);
                if (!s.Form.Equals(name, StringComparison.OrdinalIgnoreCase) && !overlay.ContainsKey(s.Form))
                    aliases.TryAdd(s.Form, name);
            }
            return aliases;
        }

        /// <summary>
        /// Separates the two sides of an overlay "Specific &gt; General" hierarchy line.
        /// </summary>
        public const char HierarchySeparator = '>';

        private static bool IsHierarchyLine(string line) => line.Contains(HierarchySeparator);

        /// <summary>
        /// Overlay "Specific &gt; General" lines: each skill to every more general skill it
        /// implies, chains followed ("Next.js &gt; React" and "React &gt; JavaScript" give
        /// Next.js both). Case-insensitive keys; a skill never implies itself.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlySet<string>> LoadParents(string overlayPath = OverlayPath)
        {
            Dictionary<string, IReadOnlySet<string>> closed = new(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(overlayPath)) return closed;

            Dictionary<string, HashSet<string>> direct = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in ReadDataLines(overlayPath).Where(IsHierarchyLine))
            {
                string[] sides = line.Split(HierarchySeparator, 2, StringSplitOptions.TrimEntries);
                if (sides[0].Length == 0 || sides[1].Length == 0) continue;
                if (!direct.TryGetValue(sides[0], out HashSet<string>? parents))
                    direct[sides[0]] = parents = new(StringComparer.OrdinalIgnoreCase);
                parents.Add(sides[1]);
            }

            foreach (string skill in direct.Keys)
            {
                HashSet<string> all = new(StringComparer.OrdinalIgnoreCase);
                Stack<string> todo = new(direct[skill]);
                while (todo.TryPop(out string? next))
                    if (!next.Equals(skill, StringComparison.OrdinalIgnoreCase) && all.Add(next)
                            && direct.TryGetValue(next, out HashSet<string>? more))
                        foreach (string m in more) todo.Push(m);
                closed[skill] = all;
            }
            return closed;
        }

        /// <summary>
        /// Plural and hyphenated forms of the overlay's multi-word skills, to the skill
        /// ("code reviews", "code-review" for "code review"). Only for skills made of plain
        /// words: tech names ("next.js") and one-word skills that are also ordinary words
        /// ("spring", "express") get none. A form that's a skill of its own is left alone.
        /// </summary>
        private static Dictionary<string, string> WordForms(string overlayPath)
        {
            Dictionary<string, string> forms = new(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(overlayPath)) return forms;

            List<string> skills = [.. ReadDataLines(overlayPath)
                .Where(l => !IsHierarchyLine(l) && !l.Contains('\t') && !l.StartsWith(CaseSensitivePrefix))];
            HashSet<string> taken = new(skills, StringComparer.OrdinalIgnoreCase);

            foreach (string skill in skills)
            {
                string[] words = skill.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length < 2 || !words.All(w => w.All(char.IsLetter))) continue;

                List<string> variants = [string.Join('-', words)];
                if (Plural(words[^1]) is { } plural)
                {
                    string[] head = words[..^1];
                    variants.Add(string.Join(' ', [.. head, plural]));
                    variants.Add(string.Join('-', [.. head, plural]));
                }
                foreach (string variant in variants.Where(v => !taken.Contains(v)))
                    forms.TryAdd(variant, skill);
            }
            return forms;
        }

        /// <summary>
        /// A noun's plural, or null when it already ends in s ("systems", "bus").
        /// </summary>
        private static string? Plural(string word)
        {
            if (word.EndsWith('s')) return null;
            if (word.EndsWith('x') || word.EndsWith('z') || word.EndsWith("ch") || word.EndsWith("sh"))
                return word + "es";
            if (word.Length > 1 && word[^1] == 'y' && !"aeiou".Contains(word[^2]))
                return word[..^1] + "ies";
            return word + "s";
        }

        private static IEnumerable<string> ReadDataLines(string path) =>
            File.ReadLines(path, Encoding.UTF8)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'));

        /// <summary>
        /// O*Net base entries with their categories and weights
        /// </summary>
        public static IReadOnlyList<SkillEntry> LoadCategorized(string path = DefaultPath)
        {
            if (!File.Exists(path)) return [];
            return [.. ReadDataLines(path).Select(ParseLine)];
        }

        /// <summary>
        /// Overlay names to spot. An alias line contributes only its alias: the canonical
        /// name is spotted only if it's listed on its own ("golang&lt;TAB&gt;Go" must not
        /// make every "go" a skill; "=Go" does that, case-sensitively).
        /// </summary>
        private static IEnumerable<string> LoadOverlay(string path)
        {
            if (!File.Exists(path)) return [];
            return [.. ReadDataLines(path)
                .Where(l => !IsHierarchyLine(l))
                .Select(l => l.Split('\t')[0].Trim())
                .Where(l => l.Length > 0)];
        }

        private static SkillEntry ParseLine(string line)
        {
            string[] p = line.Split('\t');
            string name = p[0].Trim();
            string cat = p.Length > 1 ? p[1].Trim() : "Skill";
            int weight = p.Length > 2 && int.TryParse(p[2], out int w) ? w : 0;
            return new SkillEntry(name, cat, weight);
        }
    }
}
