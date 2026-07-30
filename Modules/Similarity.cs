using System;
using System.Collections.Generic;
using System.Text;

namespace GetJobCV.Modules
{
    /// <summary>
    /// Vector similarity helper class
    /// </summary>
    public static class Similarity
    {
        /// <summary>
        /// Method to compute the cosine similarity (cosine of the angle)
        /// between two vectors of equal length
        /// </summary>
        /// <param name="a">First vector</param>
        /// <param name="b">Second vector with the same length as <paramref name="a"/></param>
        /// <returns>Cosine similarity in <c>[-1, 1]</c></returns>
        /// <exception cref="ArgumentException">The vectors are different lengths!</exception>
        public static double Cosine(double[] a, double[] b)
        {
            if (a.Length != b.Length)
                throw new ArgumentException("Vectors are not the same length!", nameof(b));

            double dot = 0.0;
            double sumSquaresA = 0.0;
            double sumSquaresB = 0.0;
            
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                sumSquaresA += a[i] * a[i];
                sumSquaresB += b[i] * b[i];
            }

            if (sumSquaresA == 0.0 || sumSquaresB == 0.0)
                return 0.0;

            return dot / (Math.Sqrt(sumSquaresA) * Math.Sqrt(sumSquaresB));
        }
    }
}
