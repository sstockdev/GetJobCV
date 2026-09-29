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
            return names;
        }

        /// <summary>
        /// Overlay lines starting with this are matched case-sensitively, for skills
        /// that collide with ordinary words or letters (CAN vs "can", C vs "c").
        /// </summary>
        public const char CaseSensitivePrefix = '=';

        /// <summary>
        /// Overlay skills that must match case-sensitively, prefix stripped.
        /// </summary>
        public static IReadOnlyList<string> LoadCaseSensitive(string overlayPath = OverlayPath) =>
            [.. LoadOverlay(overlayPath)
                .Where(n => n.StartsWith(CaseSensitivePrefix))
                .Select(n => n[1..].Trim())
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

            foreach ((string alias, string canonical) in LoadAliases(overlayPath))
                if (tiers.TryGetValue(alias, out int tier) && tier > tiers.GetValueOrDefault(canonical))
                    tiers[canonical] = tier;
            return tiers;
        }

        /// <summary>
        /// Overlay "alias&lt;TAB&gt;Canonical" lines: alias (without any '=' prefix) to the
        /// name it's reported as. Case-insensitive keys. Aliases don't chain.
        /// </summary>
        public static IReadOnlyDictionary<string, string> LoadAliases(string overlayPath = OverlayPath)
        {
            Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(overlayPath)) return aliases;
            foreach (string line in ReadDataLines(overlayPath))
            {
                int tab = line.IndexOf('\t');
                if (tab < 0) continue;
                string alias = line[..tab].Trim().TrimStart(CaseSensitivePrefix).Trim();
                string canonical = line[(tab + 1)..].Trim();
                if (alias.Length > 0 && canonical.Length > 0)
                    aliases[alias] = canonical;
            }
            return aliases;
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
