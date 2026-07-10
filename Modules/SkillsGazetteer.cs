using System;
using System.Collections.Generic;
using System.Text;

namespace GetJobATS.Modules
{
    /// <summary>
    /// Loads skill phrases for the NER gazetteer from a text file.
    /// </summary>
    public static class SkillsGazetteer
    {
        public static IReadOnlyList<string> Load(string path = "Resources/skills.txt")
        {
            if (!File.Exists(path))
                return [];

            return [.. File.ReadLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))];
        }
    }
}
