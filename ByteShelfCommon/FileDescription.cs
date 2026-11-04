using System.Text;

namespace ByteShelfCommon
{
    /// <summary>The rules for a file's description: one free text per file, which search matches too.</summary>
    public static class FileDescription
    {
        /// <summary>The most characters (Unicode code points) a description may hold.</summary>
        public const int MaxLength = 2000;

        /// <summary>
        /// The description as stored: trimmed and in Unicode normalization form C, or null when nothing
        /// is left, which is no description.
        /// </summary>
        /// <exception cref="ArgumentException">It is longer than <see cref="MaxLength"/>, or not valid Unicode.</exception>
        public static string? Normalize(string? description)
        {
            if (description == null)
            {
                return null;
            }

            string normalized = description.Trim().Normalize(NormalizationForm.FormC);
            if (normalized.Length == 0)
            {
                return null;
            }

            // Counted in code points, as the database counts it, so a description of emoji is not cut
            // at half the length a reader sees.
            int length = normalized.EnumerateRunes().Count();
            if (length > MaxLength)
            {
                throw new ArgumentException($"A description is at most {MaxLength} characters; this one has {length}.", nameof(description));
            }

            return normalized;
        }
    }
}
