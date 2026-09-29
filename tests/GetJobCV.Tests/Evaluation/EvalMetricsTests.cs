using GetJobCV.Tests.Evaluation;

namespace GetJobCV.Tests
{
    public class EvalMetricsTests
    {
        [Fact]
        public void Scores_a_predicted_set_against_the_gold_set()
        {
            SetScore s = SetScore.Compare(new HashSet<string> { "a", "b", "c", "d" }, new HashSet<string> { "a", "b", "x" });
            Assert.Equal(new SetScore(2, 1, 2), s);
            Assert.Equal(2.0 / 3, s.Precision, 6);
            Assert.Equal(0.5, s.Recall, 6);
            Assert.Equal(4.0 / 7, s.F1, 6);
        }

        [Fact]
        public void Concordance_only_compares_within_a_group()
        {
            // Across groups the order is wrong, but within each it's right
            (string, Fit, double)[] items =
            [
                ("j1", Fit.Good, 0.3), ("j1", Fit.No, 0.1),
                ("j2", Fit.Good, 0.9), ("j2", Fit.Potential, 0.5), ("j2", Fit.No, 0.4),
            ];
            Assert.Equal(1.0, EvalMetrics.Concordance(items));
        }

        [Fact]
        public void Concordance_counts_ties_as_half()
        {
            Assert.Equal(0.5, EvalMetrics.Concordance([("j", Fit.Good, 0.5), ("j", Fit.No, 0.5)]));
        }

        [Fact]
        public void Auc_is_the_share_of_positive_negative_pairs_ordered_right()
        {
            Assert.Equal(0.75, EvalMetrics.Auc([0.9, 0.4], [0.5, 0.1]));
        }

        [Fact]
        public void Ndcg_is_one_for_a_perfect_ranking_and_less_otherwise()
        {
            Assert.Equal(1.0, EvalMetrics.Ndcg([(Fit.Good, 0.9), (Fit.Potential, 0.5), (Fit.No, 0.1)]), 6);
            Assert.True(EvalMetrics.Ndcg([(Fit.Good, 0.1), (Fit.Potential, 0.5), (Fit.No, 0.9)]) < 1);
        }

        [Fact]
        public void Ndcg_does_not_reward_giving_everyone_the_same_score()
        {
            Assert.True(EvalMetrics.Ndcg([(Fit.Good, 0.5), (Fit.No, 0.5)]) < 1);
        }
    }
}
