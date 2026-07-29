using System;
using System.Collections.Generic;
using System.Text;

namespace GetJobCV.Modules
{
    /// <summary>
    /// Count vectorizer (bag of words). Expects already preprocessed tokens from <see cref="PreProcessor"/>.
    /// </summary>
    public sealed class CountVectorizer
    {
        /// <summary>
        /// Vocabulary terms in column order.
        /// </summary>
        private readonly string[] _terms;

        /// <summary>
        /// Maps each term to its column index in a count vector.
        /// </summary>
        private readonly Dictionary<string, int> _vocabulary;

        private CountVectorizer(string[] terms, Dictionary<string, int> vocabulary)
        {
            _terms = terms;
            _vocabulary = vocabulary;
        }

        /// <summary>
        /// The number of terms in the learned vocab. It should equal the length
        /// of every vector that is produced by <see cref="Transform"/>
        /// </summary>
        public int VocabularySize => _terms.Length;

        /// <summary>
        /// The vocab terms in column order.
        /// </summary>
        public IReadOnlyList<string> Vocabulary => _terms;

        /// <summary>
        /// Learns a vocabulary from a corpus. Every distinct token across
        /// all the documents becomes a column.
        /// </summary>
        /// <param name="documents">The preprocessed token lists (one per doc)</param>
        /// <returns>A fitted vectorizer</returns>
        public static CountVectorizer Fit(IEnumerable<string[]> documents)
        {
            SortedSet<string> distinct = new(StringComparer.Ordinal);
            foreach (string[] document in documents)
                foreach (string token in document)
                    distinct.Add(token);

            string[] terms = [.. distinct];
            Dictionary<string, int> vocabulary = new(terms.Length, StringComparer.Ordinal);
            for (int i = 0; i < terms.Length; i++)
                vocabulary[terms[i]] = i;

            return new CountVectorizer(terms, vocabulary);
        }

        /// <summary>
        /// Maps a document to a term frequency vector that is aligned
        /// to the learned vocab.
        /// </summary>
        /// <param name="tokens">Preprocessed tokens for a single document</param>
        /// <returns>Count vector of length <see cref="VocabularySize"/></returns>
        public int[] Transform(string[] tokens)
        {
            int[] counts = new int[_terms.Length];
            foreach (string token in tokens)
                if (_vocabulary.TryGetValue(token, out int index))
                    counts[index]++;
            return counts;
        }

        /// <summary>
        /// Learns a vocabulary from the corpus and returns tthe count vectors for
        /// each document in the same order.
        /// </summary>
        /// <param name="documents">Preprocessed tokens lists (one per doc)</param>
        /// <returns>The fittted vectorizer and one count vector per input document</returns>
        public static (CountVectorizer Vectorizer, int[][] Vectors)
            FitTransform(IEnumerable<string[]> documents)
        {
            string[][] corpus = [.. documents];
            CountVectorizer vectorizer = Fit(corpus);
            int[][] vectors = [.. corpus.Select(vectorizer.Transform)];
            return (vectorizer, vectors);
        }
    }
}
