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
                if (seen.Add(n)) names.Add(n);
            return names;
        }

        /// <summary>
        /// O*Net base entries with their categories and weights
        /// </summary>
        public static IReadOnlyList<SkillEntry> LoadCategorized(string path = DefaultPath)
        {
            if (!File.Exists(path)) return [];
            return [.. File.ReadLines(path, Encoding.UTF8)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(ParseLine)];
        }

        private static IEnumerable<string> LoadOverlay(string path)
        {
            if (!File.Exists(path)) return [];
            return [.. File.ReadLines(path, Encoding.UTF8)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .SelectMany(SplitOverlay)];
        }

        private static IEnumerable<string> SplitOverlay(string line)
        {
            int tab = line.IndexOf('\t');
            if (tab < 0) { yield return line; yield break; }
            string alias = line[..tab].Trim();
            string canon = line[(tab + 1)..].Trim();
            if (alias.Length > 0) yield return alias;
            if (canon.Length > 0) yield return canon;
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
