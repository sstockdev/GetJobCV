namespace GetJobCV.Tests.Evaluation
{
    /// <summary>
    /// How well a candidate fits a job, as a recruiter would label it.
    /// </summary>
    public enum Fit { No = 0, Potential = 1, Good = 2 }

    /// <summary>
    /// Precision, recall, and F1 from counts summed over documents (micro-averaged).
    /// </summary>
    public readonly record struct SetScore(int TruePositives, int FalsePositives, int FalseNegatives)
    {
        public double Precision => Ratio(TruePositives, TruePositives + FalsePositives);
        public double Recall => Ratio(TruePositives, TruePositives + FalseNegatives);
        public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);

        public static SetScore operator +(SetScore a, SetScore b) => new(
            a.TruePositives + b.TruePositives, a.FalsePositives + b.FalsePositives, a.FalseNegatives + b.FalseNegatives);

        /// <summary>
        /// Compares one document's predicted set with its gold set.
        /// </summary>
        public static SetScore Compare(IReadOnlySet<string> gold, IReadOnlySet<string> predicted)
        {
            int tp = predicted.Count(gold.Contains);
            return new(tp, predicted.Count - tp, gold.Count - tp);
        }

        // An empty denominator means nothing was asked of it, so it gets full marks
        private static double Ratio(int n, int d) => d == 0 ? 1 : (double)n / d;

        public override string ToString() => $"P {Precision:P0}  R {Recall:P0}  F1 {F1:P0}  (tp {TruePositives}, fp {FalsePositives}, fn {FalseNegatives})";
    }

    /// <param name="Ndcg">Mean over groups, skipping groups where every item is No fit</param>
    public readonly record struct Ranking(double Concordance, double Auc, double Ndcg);

    /// <summary>
    /// Ranking metrics over scored, labeled pairs.
    /// </summary>
    public static class EvalMetrics
    {
        /// <summary>
        /// Within-group concordance, Good vs No AUC, and mean NDCG per group.
        /// </summary>
        public static Ranking Rank<TGroup>(IEnumerable<(TGroup Group, Fit Fit, double Score)> items)
        {
            var list = items.ToList();
            return new Ranking(
                Concordance(list),
                Auc(list.Where(i => i.Fit == Fit.Good).Select(i => i.Score), list.Where(i => i.Fit == Fit.No).Select(i => i.Score)),
                list.GroupBy(i => i.Group)
                    .Select(g => Ndcg(g.Select(i => (i.Fit, i.Score))))
                    .Where(v => !double.IsNaN(v)).Average());
        }

        /// <summary>
        /// Of every two items in the same group with different fits, the share where the
        /// better fit scored higher. A tie counts half. 0.5 is chance, 1 is perfect.
        /// </summary>
        public static double Concordance<TGroup>(IEnumerable<(TGroup Group, Fit Fit, double Score)> items)
        {
            double correct = 0;
            int compared = 0;
            foreach (var group in items.GroupBy(i => i.Group))
            {
                var list = group.ToList();
                for (int a = 0; a < list.Count; a++)
                    for (int b = a + 1; b < list.Count; b++)
                    {
                        if (list[a].Fit == list[b].Fit)
                            continue;
                        var (better, worse) = list[a].Fit > list[b].Fit ? (list[a], list[b]) : (list[b], list[a]);
                        compared++;
                        correct += better.Score > worse.Score ? 1 : better.Score == worse.Score ? 0.5 : 0;
                    }
            }
            return compared == 0 ? double.NaN : correct / compared;
        }

        /// <summary>
        /// Area under the ROC curve: the chance a random positive scores above a random
        /// negative. Same as <see cref="Concordance"/> with every item in one group.
        /// </summary>
        public static double Auc(IEnumerable<double> positives, IEnumerable<double> negatives) =>
            Concordance(positives.Select(s => (0, Fit.Good, s)).Concat(negatives.Select(s => (0, Fit.No, s))));

        /// <summary>
        /// Normalized discounted cumulative gain of one group ranked by score, with the fit
        /// as the gain. 1 means the best fits are ranked first.
        /// </summary>
        public static double Ndcg(IEnumerable<(Fit Fit, double Score)> items)
        {
            var list = items.ToList();
            static double Dcg(IEnumerable<Fit> ranked) =>
                ranked.Select((f, i) => (Math.Pow(2, (int)f) - 1) / Math.Log2(i + 2)).Sum();

            double ideal = Dcg(list.Select(i => i.Fit).OrderByDescending(f => f));
            // Ties rank the worse fit first, so a model can't score well by giving everyone the same score
            double actual = Dcg(list.OrderByDescending(i => i.Score).ThenBy(i => i.Fit).Select(i => i.Fit));
            return ideal == 0 ? double.NaN : actual / ideal;
        }
    }
}
