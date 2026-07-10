using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GetJobATS.Modules
{
    public record Socials(string? GitHub, string? LinkedIn);
    
    /// <summary>
    /// Extracts GitHub and LinkedIn profile URLs from raw text and from
    /// hyperlinks
    /// </summary>
    public partial class SocialExtractor
    {
        /// <summary>
        /// Regex to find GitHub URL
        /// </summary>
        private static readonly Regex GitHubRegex = GitHubUrlRegex();

        /// <summary>
        /// Regex to find LinkedIn URL
        /// </summary>
        private static readonly Regex LinkedInRegex = LinkedInUrlRegex();

        /// <summary>
        /// Extract GitHub and LinkedIn profiles
        /// </summary>
        /// <param name="raw">Raw text extracted from PDF</param>
        /// <param name="hyperlinks">
        /// URIs pulled from PDF links. Checked first.
        /// </param>
        public static Socials Extract(string raw, IEnumerable<string>? hyperlinks = null)
        {
            List<string> haystacks = [];
            if (hyperlinks is not null)
                haystacks.AddRange(hyperlinks);
            if (!string.IsNullOrWhiteSpace(raw))
                haystacks.Add(raw);

            return new Socials(
                FirstMatch(GitHubRegex, haystacks),
                FirstMatch(LinkedInRegex, haystacks));
        }

        /// <summary>
        /// Find first match in haystack and normalize it
        /// </summary>
        /// <param name="re">Regex Pattern</param>
        /// <param name="haystacks">Haystack of values</param>
        /// <returns>Noramlized match</returns>
        private static string? FirstMatch(Regex re, List<string> haystacks)
        {
            foreach (string h in haystacks)
            {
                Match m = re.Match(h);
                if (m.Success)
                    return Normalize(m);
            }
            return null;
        }

        /// <summary>
        /// Helper method to add scheme if missing from URL
        /// and trim trailing punctuation
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        private static string Normalize(Match m)
        {
            string url = m.Value.Trim().TrimEnd('.', ',', ')', ';', '/');
            return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
        }

        [GeneratedRegex(@"(?:https?://)?(?:www\.)?github\.com/[A-Za-z0-9_-]+/?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex GitHubUrlRegex();

        [GeneratedRegex(@"(?:https?://)?(?:www\.)?linkedin\.com/in/[A-Za-z0-9_%-]+/?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex LinkedInUrlRegex();
    }
}
