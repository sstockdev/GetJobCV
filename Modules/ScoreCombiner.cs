using System;
using System.Collections.Generic;
using System.Text;

namespace GetJobCV.Modules
{
    /// <summary>
    /// Combines the TF-IDF cosine match with the weighted skill coverage int a score.
    /// </summary>
    public static class ScoreCombiner
    {
        // Both signals rank the gold set (tests/GetJobCV.Tests/Evaluation) perfectly; on the
        // public fit benchmark cosine ranks a little better, so it gets an equal share

        /// <summary>
        /// The weight for the TF-IDF cosine signal
        /// </summary>
        public const double DefaultCosineWeight = 0.5;

        /// <summary>
        /// The weight for the skill coverage signal
        /// </summary>
        public const double DefaultCoverageWeight = 0.5;

        // Cosine without IDF rarely passes 0.6 even for a strong fit, so these sit lower than
        // an even split. Each is midway between the gold set's fits: good fits score 61% and
        // up, potential fits 35-45%, no fits 18% and under
        private const double StrongThreshold = 0.55;
        private const double OkayThreshold = 0.25;
        private const double BadThreshold = 0.10;

        /// <summary>
        /// The combined result
        /// </summary>
        public sealed record CombinedScore(double Overall, string Verdict);
        
        /// <summary>
        /// Method to combine the two scores.
        /// </summary>
        public static CombinedScore Combine(
            double cosine,
            double weightCoverage,
            double cosineWeight = DefaultCosineWeight,
            double coverageWeight = DefaultCoverageWeight)
        {
            double c = Math.Clamp(cosine, 0.0, 1.0);
            double w = Math.Clamp(weightCoverage, 0.0, 1.0);

            double total = cosineWeight + coverageWeight;
            double overall = total > 0.0
                ? (c * cosineWeight + w * coverageWeight) / total
                : (c + w) / 2.0;

            return new CombinedScore(overall, Verdict(overall));
        }

        public static string Verdict(double overall) => overall switch
        {
            >= StrongThreshold => "Good match!",
            >= OkayThreshold => "Could use improvement.",
            >= BadThreshold => "Needs improvement!",
            _ => "Awful"
        };
    }
}
