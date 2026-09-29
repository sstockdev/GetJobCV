using static GetJobCV.Modules.NerExtractor;

namespace GetJobCV.Modules
{
    /// <summary>
    /// Runs the whole pipeline on resume text and a job description. Knows nothing
    /// about files or the UI, so the app and the evaluation tests run the same code.
    /// </summary>
    public static class Analyzer
    {
        /// <summary>
        /// Everything produced by one resume vs job description run.
        /// </summary>
        public sealed record AnalysisResult(
            Socials Socials,
            IReadOnlyList<ResumeSection> Sections,
            ResumeRecord Record,
            ExperienceSummary Experience,
            JobRequirements Requirements,
            NerResult Ner,
            double Score,
            SkillMatcher.SkillReport Skills);

        /// <summary>
        /// Scores the resume against the job description.
        /// </summary>
        /// <param name="hyperlinks">Link targets from the resume file, for socials the text doesn't spell out</param>
        /// <param name="today">The end date of a role still held ("Present")</param>
        /// <param name="status">Told the name of each step as it starts</param>
        /// <param name="cancel">Checked between steps; a step already running finishes first</param>
        public static AnalysisResult Analyze(
            string resumeText,
            IReadOnlyList<string> hyperlinks,
            string jobDescription,
            NerExtractor ner,
            IReadOnlyDictionary<string, int> skillTiers,
            IReadOnlyDictionary<string, IReadOnlySet<string>> skillParents,
            DateOnly today,
            IProgress<string>? status = null,
            CancellationToken cancel = default)
        {
            // Every step starts by reporting, so that's where a cancel takes effect
            void Step(string name)
            {
                cancel.ThrowIfCancellationRequested();
                status?.Report(name);
            }

            // Get socials
            Step("Extracting Socials");
            Socials socials = SocialExtractor.Extract(resumeText, hyperlinks);

            // Split into Education, Experience, Skills, ...
            Step("Finding Sections");
            IReadOnlyList<ResumeSection> sections = SectionSegmenter.Segment(resumeText);

            // Pull out contact details, schools, and roles
            Step("Parsing Resume");
            ResumeRecord record = ResumeParser.Parse(sections, socials);

            // Run NER per section so each skill is known with where it appears
            Step("Running Named Entity Recognition");
            // This is the slow step, so it also checks for a cancel between NER calls
            NerResult Extract(string text)
            {
                cancel.ThrowIfCancellationRequested();
                return ner.Extract(text);
            }

            var sectionNer = sections.Select(s => (s.Type, Ner: Extract(s.Text))).ToList();
            NerResult resumeNer = NerResult.Merge(sectionNer.Select(s => s.Ner));
            NerResult jdNer = Extract(jobDescription);

            // Per-role skills give each skill the months of the jobs that use it
            ExperienceSummary experience = ExperienceCalculator.Summarize(
                record.Experience
                    .Where(role => role.Section == SectionType.Experience)
                    .Select(role => (role, (IEnumerable<string>)Extract(role.Text).Skills)),
                today);

            // "3+ years of Python" and "5+ years of experience" in the job description
            JobRequirements requirements = RequirementExtractor.Extract(jobDescription, ner);

            // Run preprocessing on text
            Step("Preprocessing Text");
            string[] resumeTokens = PreProcessor.Preprocess(resumeText);
            string[] jdTokens = PreProcessor.Preprocess(jobDescription);

            // Vectorize over one shared vocabulary. IDF is off: fitted on only these
            // two docs it would down-weight exactly the shared terms we're scoring on.
            Step("Scoring");
            var (_, vectors) = TfidfVectorizer.FitTransform(
                [resumeTokens, jdTokens], useIdf: false, sublinearTf: true);

            double score = Similarity.Cosine(vectors[0], vectors[1]);

            List<(string Skill, SectionType Section)> resumeSkills =
                [.. sectionNer.SelectMany(s => s.Ner.Skills.Select(skill => (skill, s.Type)))];

            // A GitHub profile link is evidence of GitHub even though NER can't see into URLs
            if (socials.GitHub is not null)
                resumeSkills.Add(("GitHub", SectionType.Contact));

            Dictionary<string, int> shownMonths = experience.SkillMonths
                .ToDictionary(s => s.Skill, s => s.Months, StringComparer.OrdinalIgnoreCase);

            SkillMatcher.YearsCheck? overallYears = requirements.OverallYears is { } years
                ? new SkillMatcher.YearsCheck(years * 12, experience.TotalMonths)
                : null;

            // "Python is a plus", "Preferred qualifications": these count for less
            IReadOnlySet<string> preferred = QualificationSplitter.PreferredSkills(jobDescription, ner);

            // "One of the following", "such as", "Python or Java", topic lists: any one will do
            IReadOnlyList<IReadOnlySet<string>> alternatives = AlternativeGroups.Find(jobDescription, ner);

            SkillMatcher.SkillReport skillReport = SkillMatcher.Match(
                resumeSkills, jdNer.Skills, skillTiers, requirements.SkillYears, shownMonths, overallYears, preferred, alternatives,
                skillParents);

            return new AnalysisResult(
                socials, sections, record, experience, requirements, resumeNer, score, skillReport);
        }
    }
}
