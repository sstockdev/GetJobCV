using Catalyst;
using Catalyst.Models;
using System.Text.RegularExpressions;
using Mosaik.Core;
using Version = Mosaik.Core.Version;

namespace GetJobCV.Modules
{
    /// <summary>
    /// Named Entity Recognition over raw text.
    /// Person / Org / Loc comes from pretrained WikiNER
    /// model. Skills come from a gazetteer Spotter.
    /// Needs un-preprocessed input.
    /// </summary>
    public sealed partial class NerExtractor
    {
        private readonly Pipeline _pipeline;

        /// <summary>
        /// Every gazetteer entry, used to decide which slashed tokens hold skills.
        /// </summary>
        private readonly HashSet<string> _skills;
        private readonly HashSet<string> _exactSkills;

        private NerExtractor(Pipeline pipeline, HashSet<string> skills, HashSet<string> exactSkills)
        {
            _pipeline = pipeline;
            _skills = skills;
            _exactSkills = exactSkills;
        }

        /// <summary>
        /// Builds once. Downloads English + WikiNER models on first run
        /// then register the skills gazetteer.
        /// </summary>
        /// <param name="skills">skills gazetteer, matched case-insensitively</param>
        /// <param name="caseSensitiveSkills">skills that collide with ordinary words
        /// (CAN vs "can"), so only match with exact case</param>
        public static async Task<NerExtractor> CreateAsync(
            IEnumerable<string> skills, IEnumerable<string> caseSensitiveSkills)
        {
            English.Register();

            Pipeline pipeline = await Pipeline.ForAsync(Language.English);

            // load WikiNER
            pipeline.Add(await AveragePerceptronEntityRecognizer.FromStoreAsync(
                language: Language.English, version: Version.Latest, tag: "WikiNER"));

            HashSet<string> skillSet = new(skills, StringComparer.OrdinalIgnoreCase);
            HashSet<string> exactSet = new(caseSensitiveSkills, StringComparer.Ordinal);

            // Gazetteer: tokens matching a skill phrase get entity type "Skill"
            pipeline.Add(CreateSpotter("skills", ignoreCase: true, skillSet));
            pipeline.Add(CreateSpotter("skills-exact", ignoreCase: false, exactSet));

            return new NerExtractor(pipeline, skillSet, exactSet);
        }

        private static Spotter CreateSpotter(string tag, bool ignoreCase, IEnumerable<string> entries)
        {
            Spotter spotter = new(Language.Any, 0, tag, "Skill");
            spotter.Data.IgnoreCase = ignoreCase;
            foreach (string entry in entries)
                spotter.AddEntry(entry);
            return spotter;
        }

        /// <summary>
        /// Extract entities from raw un-preprocessed text.
        /// Each returned list is de-duped and is case-insensitive.
        /// </summary>
        /// <param name="rawText">Raw text extracted from a PDF</param>
        /// <returns>Entity list</returns>
        public NerResult Extract(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return NerResult.Empty;

            Document doc = new(SplitSlashedSkills(rawText), Language.English);
            _pipeline.ProcessSingle(doc);

            HashSet<string> people = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> orgs = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> locations = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> skills = new(StringComparer.OrdinalIgnoreCase);

            foreach (var e in doc.SelectMany(span => span.GetEntities()))
            {
                // from WikiNER
                switch (e.EntityType.Type)
                {
                    case "Person": people.Add(e.Value); break;
                    case "Organization": orgs.Add(e.Value); break;
                    case "Location": locations.Add(e.Value); break;
                    case "Skill": skills.Add(e.Value); break;
                }
            }

            // remove skills from people / orgs / locations
            people.ExceptWith(skills);
            orgs.ExceptWith(skills);
            locations.ExceptWith(skills);

            return new NerResult([.. people], [.. orgs], [.. locations], [.. skills]);
        }

        /// <summary>
        /// Catalyst's tokenizer doesn't split on '/', so "C/C++" or "C#/.NET" would be
        /// one token and the spotter would never see C++ or C#. Split a slashed token
        /// only when a part is a known skill and the whole token isn't one ("CI/CD").
        /// URLs and paths ("github.com/user") have no skill parts, so they stay intact.
        /// </summary>
        private string SplitSlashedSkills(string text) =>
            SlashedTokenRegex().Replace(text, m =>
            {
                string core = m.Value.TrimStart('(').TrimEnd(',', ';', ':', ')', '.');
                if (IsSkill(core) || !core.Split('/').Any(IsSkill))
                    return m.Value;
                return m.Value.Replace("/", " / ");
            });

        private bool IsSkill(string text) => _skills.Contains(text) || _exactSkills.Contains(text);

        /// <summary>
        /// A whitespace-delimited token with slashes between non-empty parts.
        /// "//" in a URL scheme never matches.
        /// </summary>
        [GeneratedRegex(@"(?<!\S)[^\s/]+(?:/[^\s/]+)+(?!\S)")]
        private static partial Regex SlashedTokenRegex();

        /// <summary>
        /// Entities pulled from a resume. De-duped.
        /// </summary>
        public sealed record NerResult(
            IReadOnlyList<string> People,
            IReadOnlyList<string> Organizations,
            IReadOnlyList<string> Locations,
            IReadOnlyList<string> Skills)
        {
            public static readonly NerResult Empty = new([], [], [], []);
        }
    }
}
