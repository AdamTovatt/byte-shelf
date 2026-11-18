using System.Globalization;
using System.Text;

namespace ByteShelfCommon
{
    /// <summary>
    /// Which of a shelf's live files a search looks at: every criterion that is set must hold, and none
    /// set takes every file. <see cref="FileSearch"/> pages the files found, and
    /// <see cref="DuplicateSearch"/> the groups of them with the same contents; each takes its criteria
    /// from one of these.
    /// </summary>
    public class FileCriteria
    {
        private const string WildcardSubtype = "/*";

        /// <summary>Criteria to set with an object initializer.</summary>
        public FileCriteria()
        {
        }

        /// <summary>A copy of <paramref name="criteria"/>'s criteria, as they are.</summary>
        public FileCriteria(FileCriteria criteria)
        {
            ArgumentNullException.ThrowIfNull(criteria);
            Under = criteria.Under;
            Text = criteria.Text;
            ContentType = criteria.ContentType;
            MinSizeBytes = criteria.MinSizeBytes;
            MaxSizeBytes = criteria.MaxSizeBytes;
            CreatedFrom = criteria.CreatedFrom;
            CreatedBefore = criteria.CreatedBefore;
            UpdatedFrom = criteria.UpdatedFrom;
            UpdatedBefore = criteria.UpdatedBefore;
            Sha256 = criteria.Sha256;
        }

        /// <summary>The directory to search, everything under it included; the shelf's root when null.</summary>
        public string? Under { get; init; }

        /// <summary>Text the file's name or description contains, ignoring case.</summary>
        public string? Text { get; init; }

        /// <summary>
        /// The media type, such as <c>application/pdf</c>, or every subtype of one, such as <c>image/*</c>.
        /// Parameters such as <c>; charset=utf-8</c> on a stored type are ignored, and so is case.
        /// </summary>
        public string? ContentType { get; init; }

        /// <summary>The fewest bytes a file may have.</summary>
        public long? MinSizeBytes { get; init; }

        /// <summary>The most bytes a file may have.</summary>
        public long? MaxSizeBytes { get; init; }

        /// <summary>Stored at or after this.</summary>
        public DateTimeOffset? CreatedFrom { get; init; }

        /// <summary>Stored before this.</summary>
        public DateTimeOffset? CreatedBefore { get; init; }

        /// <summary>Last changed (moved, renamed, described) at or after this.</summary>
        public DateTimeOffset? UpdatedFrom { get; init; }

        /// <summary>Last changed before this.</summary>
        public DateTimeOffset? UpdatedBefore { get; init; }

        /// <summary>The contents' SHA-256, in hex.</summary>
        public string? Sha256 { get; init; }

        /// <summary>
        /// These criteria checked and put the way they are compared: <see cref="Under"/> always set,
        /// <see cref="Text"/> folded, <see cref="ContentType"/> and <see cref="Sha256"/> lowercase, and
        /// blank text as not set.
        /// </summary>
        /// <exception cref="ArgumentException">A criterion is not allowed, or a range is empty.</exception>
        public FileCriteria NormalizeCriteria()
        {
            if (MinSizeBytes < 0 || MaxSizeBytes < 0)
            {
                throw new ArgumentException("A size cannot be negative.");
            }

            if (MinSizeBytes > MaxSizeBytes)
            {
                throw new ArgumentException("The smallest size is above the largest.");
            }

            if (CreatedFrom >= CreatedBefore || UpdatedFrom >= UpdatedBefore)
            {
                throw new ArgumentException("A time range ends before it starts.");
            }

            return new FileCriteria
            {
                Under = Under == null ? ShelfPath.Root : ShelfPath.Normalize(Under),
                Text = string.IsNullOrWhiteSpace(Text) ? null : Fold(Text.Trim()),
                ContentType = NormalizeContentType(ContentType),
                MinSizeBytes = MinSizeBytes,
                MaxSizeBytes = MaxSizeBytes,
                CreatedFrom = CreatedFrom,
                CreatedBefore = CreatedBefore,
                UpdatedFrom = UpdatedFrom,
                UpdatedBefore = UpdatedBefore,
                Sha256 = NormalizeSha256(Sha256),
            };
        }

        /// <summary>
        /// Text as search compares it: in normalization form C and lowercase by the invariant culture's
        /// rules. Both what is searched for and what is searched pass through it.
        /// </summary>
        public static string Fold(string text)
        {
            return text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>The one media type asked for, such as <c>application/pdf</c>; null for none or a family. Call it on normalized criteria.</summary>
        /// <remarks>Methods rather than properties, so binding a search from a query string neither offers nor reads them.</remarks>
        public string? ExactMediaType()
        {
            return ContentType != null && !IsFamily(ContentType) ? ContentType : null;
        }

        /// <summary>What every type of the family asked for starts with, such as <c>image/</c>; null for none or one type. Call it on normalized criteria.</summary>
        public string? MediaTypePrefix()
        {
            return ContentType != null && IsFamily(ContentType) ? ContentType[..^1] : null;
        }

        /// <summary>Whether a stored content type is the one <see cref="ContentType"/> asks for. Call it on normalized criteria.</summary>
        public bool MatchesContentType(string storedContentType)
        {
            string mediaType = ContentTypes.MediaTypeOf(storedContentType);
            string? exact = ExactMediaType();
            string? prefix = MediaTypePrefix();
            return (exact == null || mediaType == exact)
                && (prefix == null || mediaType.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>The query string's parts for every criterion that is set, escaped.</summary>
        protected List<string> QueryParts()
        {
            List<string> parts = new List<string>();
            AddQueryPart(parts, "under", Under);
            AddQueryPart(parts, "text", Text);
            AddQueryPart(parts, "contentType", ContentType);
            AddQueryPart(parts, "minSizeBytes", MinSizeBytes?.ToString(CultureInfo.InvariantCulture));
            AddQueryPart(parts, "maxSizeBytes", MaxSizeBytes?.ToString(CultureInfo.InvariantCulture));
            AddQueryPart(parts, "createdFrom", CreatedFrom?.ToString("O", CultureInfo.InvariantCulture));
            AddQueryPart(parts, "createdBefore", CreatedBefore?.ToString("O", CultureInfo.InvariantCulture));
            AddQueryPart(parts, "updatedFrom", UpdatedFrom?.ToString("O", CultureInfo.InvariantCulture));
            AddQueryPart(parts, "updatedBefore", UpdatedBefore?.ToString("O", CultureInfo.InvariantCulture));
            AddQueryPart(parts, "sha256", Sha256);
            return parts;
        }

        /// <summary>Adds <paramref name="name"/>=<paramref name="value"/>, escaped, when the value is set.</summary>
        protected static void AddQueryPart(List<string> parts, string name, string? value)
        {
            ArgumentNullException.ThrowIfNull(parts);
            if (value != null)
            {
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }

        private static bool IsFamily(string contentType)
        {
            return contentType.EndsWith(WildcardSubtype, StringComparison.Ordinal);
        }

        private static string? NormalizeContentType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return null;
            }

            string trimmed = contentType.Trim().ToLowerInvariant();
            if (IsFamily(trimmed))
            {
                string type = trimmed[..^WildcardSubtype.Length];
                if (type.Length == 0 || type.Contains('/', StringComparison.Ordinal) || type.Contains(';', StringComparison.Ordinal))
                {
                    throw new ArgumentException($"'{contentType}' is not a media type, nor a type with /*.", nameof(contentType));
                }

                return trimmed;
            }

            return ContentTypes.MediaTypeOf(ContentTypes.Normalize(trimmed));
        }

        private static string? NormalizeSha256(string? sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256))
            {
                return null;
            }

            string trimmed = sha256.Trim().ToLowerInvariant();
            if (trimmed.Length != 64 || !trimmed.All(char.IsAsciiHexDigit))
            {
                throw new ArgumentException($"'{sha256}' is not a SHA-256: 64 hex digits.", nameof(sha256));
            }

            return trimmed;
        }
    }
}
