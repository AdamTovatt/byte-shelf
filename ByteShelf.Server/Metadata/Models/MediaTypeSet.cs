using System.Text.RegularExpressions;
using ByteShelfCommon;

namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>The media types a reader takes: single ones (<c>application/pdf</c>) and families (<c>image/*</c>).</summary>
    public sealed partial class MediaTypeSet
    {
        private readonly IReadOnlyList<MediaTypePattern> _patterns;

        private MediaTypeSet(IReadOnlyList<MediaTypePattern> patterns)
        {
            _patterns = patterns;
        }

        /// <summary>The set of <paramref name="patterns"/>, each a media type or a family of them, lowercase.</summary>
        /// <exception cref="ArgumentException">One is neither.</exception>
        public static MediaTypeSet Of(params IEnumerable<string> patterns)
        {
            List<MediaTypePattern> checkedPatterns = new List<MediaTypePattern>();
            foreach (string pattern in patterns)
            {
                if (!Pattern().IsMatch(pattern))
                {
                    throw new ArgumentException($"'{pattern}' is not a lowercase media type, such as image/png, or a family of them, such as image/*.", nameof(patterns));
                }

                checkedPatterns.Add(MediaTypePattern.Of(pattern));
            }

            return new MediaTypeSet(checkedPatterns);
        }

        /// <summary>Whether <paramref name="mediaType"/> (without parameters, lowercase) is one of them.</summary>
        public bool Includes(string mediaType)
        {
            return _patterns.Any(pattern => pattern.Includes(mediaType));
        }

        /// <summary>
        /// The condition that <paramref name="column"/>, a media type, is one of them. The types are written
        /// into it rather than bound: <see cref="Of"/> takes no character a string literal could end with.
        /// </summary>
        internal string Sql(string column)
        {
            IEnumerable<string> conditions = _patterns.Select(pattern => pattern.Prefix == null
                ? $"{column} = '{pattern.Type}'"
                : $"substr({column}, 1, {pattern.Prefix.Length}) = '{pattern.Prefix}'");

            return $"({string.Join(" OR ", conditions.DefaultIfEmpty("0"))})";
        }

        // The characters RFC 6838 allows in a media type's names.
        [GeneratedRegex(@"\A[a-z0-9!#$&^_.+-]+/(\*|[a-z0-9!#$&^_.+-]+)\z")]
        private static partial Regex Pattern();
    }
}
