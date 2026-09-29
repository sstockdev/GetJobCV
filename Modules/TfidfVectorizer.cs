using System;
using System.Collections.Generic;
using System.Text;

namespace GetJobCV.Modules
{
    /// <summary>
    /// TF-IDF vectorizer. Wraps <see cref="CountVectorizer"/> for the shared vocab and term counts.
    /// It then weighs each count by a smoothed inverse document frequency and L2 normlizes the result row.
    /// We then produce <c>double[]</c> vectors aligned o the learned vocab.
    /// </summary>
    public sealed class TfidfVectorizer
    {
        /// <summary>
        /// The underlying bag of words model that owns the vocab and counts.
        /// </summary>
        private readonly CountVectorizer _counts;

        /// <summary>
        /// The inverse document weight per term, in vocab column order.
        /// </summary>
        private readonly double[] _idf;

        /// <summary>
        /// Replace raw term counts with <c>1 + ln(tf)</c>.
        /// </summary>
        private readonly bool _sublinearTf;

        private TfidfVectorizer(CountVectorizer counts, double[] idf, bool sublinearTf)
        {
            _counts = counts;
            _idf = idf;
            _sublinearTf = sublinearTf;
        }

        /// <summary>
        /// The number of terms in the learned vocab.
        /// </summary>
        public int VocabularySize => _counts.VocabularySize;

        /// <summary>
        /// The vocab terms in column order.
        /// </summary>
        public IReadOnlyList<string> Vocabulary => _counts.Vocabulary;

        /// <summary>
        /// This learns a vocab and a smoothed IDF weight per term from a corpus.
        /// </summary>
        /// <param name="documents">The preprocessed token lists (one per doc)</param>
        /// <param name="useIdf">Weigh terms by IDF. With a tiny corpus (e.g. just a resume and a
        /// job description) IDF down-weights the shared terms, so turn it off there.</param>
        /// <param name="sublinearTf">Use <c>1 + ln(tf)</c> instead of raw counts.</param>
        /// <returns>A fitted vectorizer.</returns>
        public static TfidfVectorizer Fit(IEnumerable<string[]> documents,
            bool useIdf = true, bool sublinearTf = false)
        {
            // Grab from CountVectorizer
            var (counts, countMatrix) = CountVectorizer.FitTransform(documents);

            int vocabSize = counts.VocabularySize;

            // Document frequency: how many docs contain each term at least once.
            int[] documentFrequency = new int[vocabSize];
            foreach (int[] vector in countMatrix)
                for (int i = 0; i < vocabSize; i++)
                    if (vector[i] > 0) documentFrequency[i]++;

            // from scikit-learn: ln((1 + N) / (1 + df)) + 1
            int n = countMatrix.Length;
            double[] idf = new double[vocabSize];
            for (int i = 0; i < vocabSize; i++)
                idf[i] = useIdf ? Math.Log((1.0 + n) / (1.0 + documentFrequency[i])) + 1.0 : 1.0;

            return new TfidfVectorizer(counts, idf, sublinearTf);
        }

        /// <summary>
        /// This maps a document to an L2 normalized TF-IDF vector aligned to the learned vocab.
        /// </summary>
        /// <param name="tokens">Preprocessed tokens for a single doc</param>
        /// <returns>TF-IDF vector of length <see cref="VocabularySize"/>; it's zero if no terms match</returns>
        public double[] Transform(string[] tokens)
        {
            int[] rawCounts = _counts.Transform(tokens);

            double[] weights = new double[rawCounts.Length];
            double sumOfSquares = 0.0;

            for (int i = 0; i < rawCounts.Length; i++)
            {
                double tf = _sublinearTf && rawCounts[i] > 0 ? 1.0 + Math.Log(rawCounts[i]) : rawCounts[i];
                double weight = tf * _idf[i];
                weights[i] = weight;
                sumOfSquares += weight * weight;
            }

            // L2 normalize so cosin similarity reduces to a dot product and
            // document length doesn't bias the score.
            double norm = Math.Sqrt(sumOfSquares);
            if (norm > 0.0)
                for (int i = 0; i < weights.Length; i++)
                    weights[i] /= norm;

            return weights;
        }

        /// <summary>
        /// This learns the vocab and IDF from a corpus, then returns one TF-IDF vector
        /// per doc in the same order.
        /// </summary>
        /// <param name="documents">Prepreocessed token lists (one per doc).</param>
        /// <param name="useIdf">See <see cref="Fit"/>.</param>
        /// <param name="sublinearTf">See <see cref="Fit"/>.</param>
        /// <returns>The fitted vectorizer and one TF-IDF vector per input document.</returns>
        public static (TfidfVectorizer Vectorizer, double[][] Vectors)
            FitTransform(IEnumerable<string[]> documents, bool useIdf = true, bool sublinearTf = false)
        {
            string[][] corpus = [.. documents];
            TfidfVectorizer vectorizer = Fit(corpus, useIdf, sublinearTf);
            double[][] vectors = [.. corpus.Select(vectorizer.Transform)];
            return (vectorizer, vectors);
        }
    }
}
