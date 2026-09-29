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

        /// <summary>
        /// Alias to the name it's reported as ("JS" → "JavaScript")
        /// </summary>
        private readonly IReadOnlyDictionary<string, string> _aliases;

        private NerExtractor(Pipeline pipeline, HashSet<string> skills, HashSet<string> exactSkills,
            IReadOnlyDictionary<string, string> aliases)
        {
            _pipeline = pipeline;
            _skills = skills;
            _exactSkills = exactSkills;
            _aliases = aliases;
        }

        /// <summary>
        /// Builds once. Downloads English + WikiNER models on first run
        /// then register the skills gazetteer.
        /// </summary>
        /// <param name="skills">skills gazetteer, matched case-insensitively</param>
        /// <param name="caseSensitiveSkills">skills that collide with ordinary words
        /// (CAN vs "can"), so only match with exact case</param>
        /// <param name="aliases">alias to canonical name, from <see cref="SkillsGazetteer.LoadAliases"/></param>
        public static async Task<NerExtractor> CreateAsync(
            IEnumerable<string> skills, IEnumerable<string> caseSensitiveSkills,
            IReadOnlyDictionary<string, string>? aliases = null)
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

            return new NerExtractor(pipeline, skillSet, exactSet,
                new Dictionary<string, string>(aliases ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
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

            Document doc = new(DetachTrailingPunctuation(SplitSlashedSkills(rawText)), Language.English);
            _pipeline.ProcessSingle(doc);

            HashSet<string> people = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> orgs = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> locations = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> skills = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);
            List<SkillMention> mentions = [];

            foreach (var e in doc.SelectMany(span => span.GetEntities()))
            {
                // from WikiNER
                switch (e.EntityType.Type)
                {
                    case "Person": people.Add(e.Value); break;
                    case "Organization": orgs.Add(e.Value); break;
                    case "Location": locations.Add(e.Value); break;
                    case "Skill":
                        string name = Canonical(e.Value);
                        skills.Add(name);
                        if (written.Add(e.Value))
                            mentions.Add(new SkillMention(name, e.Value));
                        break;
                }
            }

            // remove skills from people / orgs / locations, as written and as named
            foreach (HashSet<string> set in new[] { people, orgs, locations })
            {
                set.ExceptWith(skills);
                set.ExceptWith(written);
            }

            return new NerResult([.. people], [.. orgs], [.. locations], [.. skills], mentions);
        }

        /// <summary>
        /// The name a spotted skill is reported as: its alias target, or itself.
        /// </summary>
        private string Canonical(string written) =>
            _aliases.TryGetValue(WhitespaceRegex().Replace(written.Trim(), " "), out string? name) ? name : written;

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

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
        /// Catalyst splits "C#." at the end of a sentence into "C" and "#.", and keeps "JS."
        /// whole like an abbreviation, so the spotter finds C or nothing. Space off
        /// punctuation at the end of a word ("C#." → "C# .", "JS." → "JS .", "C++," → "C++ ,").
        /// </summary>
        private static string DetachTrailingPunctuation(string text) =>
            TrailingPunctuationRegex().Replace(text, " $0");

        [GeneratedRegex(@"(?<=[\p{L}\p{N}#+])[.,;:!?)]+(?=\s|$)")]
        private static partial Regex TrailingPunctuationRegex();

        /// <summary>
        /// A whitespace-delimited token with slashes between non-empty parts.
        /// "//" in a URL scheme never matches.
        /// </summary>
        [GeneratedRegex(@"(?<!\S)[^\s/]+(?:/[^\s/]+)+(?!\S)")]
        private static partial Regex SlashedTokenRegex();

        /// <summary>
        /// A skill as found in the text.
        /// </summary>
        /// <param name="Name">The name it's reported under, aliases resolved</param>
        /// <param name="Text">How it was written, e.g. "JS" for JavaScript</param>
        public sealed record SkillMention(string Name, string Text);

        /// <summary>
        /// Entities pulled from a resume. De-duped.
        /// </summary>
        /// <param name="Skills">Skill names, aliases resolved ("JS" is reported as "JavaScript")</param>
        /// <param name="SkillMentions">Each skill as written, for finding it in the text</param>
        public sealed record NerResult(
            IReadOnlyList<string> People,
            IReadOnlyList<string> Organizations,
            IReadOnlyList<string> Locations,
            IReadOnlyList<string> Skills,
            IReadOnlyList<SkillMention>? SkillMentions = null)
        {
            public static readonly NerResult Empty = new([], [], [], []);

            /// <summary>
            /// Combine results from separate chunks of one document (e.g. resume sections).
            /// A name tagged as a skill anywhere is dropped from people / orgs / locations,
            /// same as <see cref="Extract"/> does within one chunk.
            /// </summary>
            public static NerResult Merge(IEnumerable<NerResult> results)
            {
                HashSet<string> people = new(StringComparer.OrdinalIgnoreCase);
                HashSet<string> orgs = new(StringComparer.OrdinalIgnoreCase);
                HashSet<string> locations = new(StringComparer.OrdinalIgnoreCase);
                HashSet<string> skills = new(StringComparer.OrdinalIgnoreCase);
                List<SkillMention> mentions = [];

                foreach (NerResult r in results)
                {
                    people.UnionWith(r.People);
                    orgs.UnionWith(r.Organizations);
                    locations.UnionWith(r.Locations);
                    skills.UnionWith(r.Skills);
                    mentions.AddRange(r.SkillMentions ?? []);
                }

                people.ExceptWith(skills);
                orgs.ExceptWith(skills);
                locations.ExceptWith(skills);

                return new NerResult([.. people], [.. orgs], [.. locations], [.. skills],
                    [.. mentions.DistinctBy(m => m.Text, StringComparer.OrdinalIgnoreCase)]);
            }
        }
    }
}
