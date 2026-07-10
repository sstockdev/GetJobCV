using Catalyst;
using Catalyst.Models;
using Mosaik.Core;
using Version = Mosaik.Core.Version;

namespace GetJobATS.Modules
{
    /// <summary>
    /// Named Entity Recognition over raw text.
    /// Person / Org / Loc comes from pretrained WikiNER
    /// model. Skills come from a gazetteer Spotter.
    /// Needs un-preprocessed input.
    /// </summary>
    public sealed class NerExtractor
    {
        private readonly Pipeline _pipeline;

        private NerExtractor(Pipeline pipeline) => _pipeline = pipeline;

        /// <summary>
        /// Builds once. Downloads English + WikiNER models on first run
        /// then register the skills gazetteer.
        /// </summary>
        /// <param name="skills">skills gazetteer</param>
        public static async Task<NerExtractor> CreateAsync(IEnumerable<string> skills)
        {
            English.Register();

            Pipeline pipeline = await Pipeline.ForAsync(Language.English);

            // load WikiNER
            pipeline.Add(await AveragePerceptronEntityRecognizer.FromStoreAsync(
                language: Language.English, version: Version.Latest, tag: "WikiNER"));

            // Gazetteer: tokens matching a skill phrase get entity type "Skill"
            Spotter skillSpotter = new(Language.Any, 0, "skills", "Skill");
            skillSpotter.Data.IgnoreCase = true;
            foreach (string skill in skills)
                skillSpotter.AddEntry(skill);
            pipeline.Add(skillSpotter);

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

            Document doc = new(rawText, Language.English);
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

            return new NerResult([.. people], [.. orgs], [.. locations], [.. skills]);
        }

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
