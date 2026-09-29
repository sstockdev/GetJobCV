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

        private NerExtractor(Pipeline pipeline) => _pipeline = pipeline;

        /// <summary>
        /// Builds once. Downloads English + WikiNER models on first run
        /// then register the skills gazetteer.
        /// </summary>
        /// <param name="skills">skills gazetteer, matched case-insensitively</param>
        /// <param name="caseSensitiveSkills">skills that collide with ordinary words
        /// (CAN vs "can"), so only match with exact case</param>
        public static async Task<NerExtractor> CreateAsync(
            IEnumerable<string> skills, IEnumerable<string>? caseSensitiveSkills = null)
        {
            English.Register();

            Pipeline pipeline = await Pipeline.ForAsync(Language.English);

            // load WikiNER
            pipeline.Add(await AveragePerceptronEntityRecognizer.FromStoreAsync(
                language: Language.English, version: Version.Latest, tag: "WikiNER"));

            // Gazetteer: tokens matching a skill phrase get entity type "Skill".
            // Entries get the same slash padding as the text, so "ci/cd" still matches.
            Spotter skillSpotter = new(Language.Any, 0, "skills", "Skill");
            skillSpotter.Data.IgnoreCase = true;
            foreach (string skill in skills)
                skillSpotter.AddEntry(PadSlashes(skill));
            pipeline.Add(skillSpotter);

            Spotter exactSpotter = new(Language.Any, 0, "skills-exact", "Skill");
            exactSpotter.Data.IgnoreCase = false;
            foreach (string skill in caseSensitiveSkills ?? [])
                exactSpotter.AddEntry(PadSlashes(skill));
            pipeline.Add(exactSpotter);

            return new NerExtractor(pipeline);
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

            Document doc = new(PadSlashes(rawText), Language.English);
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
                    // Undo the slash padding so "CI / CD" reads as "CI/CD"
                    case "Skill": skills.Add(e.Value.Replace(" / ", "/")); break;
                }
            }

            // remove skills from people / orgs / locations
            people.ExceptWith(skills);
            orgs.ExceptWith(skills);
            locations.ExceptWith(skills);

            return new NerResult([.. people], [.. orgs], [.. locations], [.. skills]);
        }

        /// <summary>
        /// Catalyst's tokenizer doesn't split on '/', so "C/C++" or "C#/.NET" would
        /// be one token and the skill spotter would never see C++ or C#.
        /// URLs are left alone so "https://..." doesn't turn into an "https" skill.
        /// </summary>
        private static string PadSlashes(string text) =>
            SlashOrUrlRegex().Replace(text, m => m.Groups["url"].Success ? m.Value : " / ");

        [GeneratedRegex(@"(?<url>\b(?:https?://|www\.)\S+|\b[\w-]+\.(?:com|org|net|io|dev|edu|gov)/\S*)|(?<=\S)/(?=\S)",
            RegexOptions.IgnoreCase)]
        private static partial Regex SlashOrUrlRegex();

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
