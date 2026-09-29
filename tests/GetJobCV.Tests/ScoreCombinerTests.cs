using GetJobCV.Modules;
using static GetJobCV.Modules.SkillMatcher;

namespace GetJobCV.Tests
{
    public class ScoreCombinerTests
    {
        private static SkillReport Report(int jobSkills, double coverage) =>
            new([], [], [], new YearsCheck(48, 48), coverage) { JobSkillCount = jobSkills };

        [Fact]
        public void Holds_back_the_verdict_when_the_job_has_too_few_recognized_skills()
        {
            // One skill and a years requirement the resume meets: coverage is high, but it's
            // nearly all the years
            ScoreCombiner.CombinedScore combined = ScoreCombiner.Combine(0.1, Report(1, 0.8));

            Assert.Equal(ScoreCombiner.TooFewSkillsVerdict, combined.Verdict);
            Assert.Equal(0.45, combined.Overall, 3);
        }

        [Fact]
        public void Judges_once_the_job_has_enough_recognized_skills()
        {
            ScoreCombiner.CombinedScore combined = ScoreCombiner.Combine(0.4, Report(ScoreCombiner.MinJobSkills, 0.7));
            Assert.Equal(ScoreCombiner.Verdict(0.55), combined.Verdict);
        }
    }
}
