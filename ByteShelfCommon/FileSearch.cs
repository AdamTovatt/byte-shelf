using System.Globalization;
using System.Text;

namespace ByteShelfCommon
{
    /// <summary>
    /// What to look for among a shelf's live files. Every criterion that is set must hold; none set
    /// finds every file. Results come by path, a page at a time: pass a page's
    /// <see cref="FileSearchResults.NextCursor"/> as <see cref="After"/> for the next one.
    /// </summary>
    public sealed class FileSearch
    {
        /// <summary>How many files a page holds when <see cref="Limit"/> is not set.</summary>
        public const int DefaultLimit = 100;

        /// <summary>The most files one page may hold.</summary>
        public const int MaxLimit = 1000;

        private const string WildcardSubtype = "/*";

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

        /// <summary>How many files a page holds: 1 to <see cref="MaxLimit"/>, <see cref="DefaultLimit"/> when null.</summary>
        public int? Limit { get; init; }

        /// <summary>The previous page's <see cref="FileSearchResults.NextCursor"/>; null for the first page.</summary>
        public string? After { get; init; }

        /// <summary>
        /// Text as search compares it: in normalization form C and lowercase by the invariant culture's
        /// rules. Both what is searched for and what is searched pass through it.
        /// </summary>
        public static string Fold(string text)
        {
            return text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>The one media type asked for, such as <c>application/pdf</c>; null for none or a family. Call it on a normalized search.</summary>
        /// <remarks>Methods rather than properties, so binding the search from a query string neither offers nor reads them.</remarks>
        public string? ExactMediaType()
        {
            return ContentType != null && !IsFamily(ContentType) ? ContentType : null;
        }

        /// <summary>What every type of the family asked for starts with, such as <c>image/</c>; null for none or one type. Call it on a normalized search.</summary>
        public string? MediaTypePrefix()
        {
            return ContentType != null && IsFamily(ContentType) ? ContentType[..^1] : null;
        }

        /// <summary>
        /// This search with every criterion checked and put the way it is compared: <see cref="Under"/>
        /// and <see cref="Limit"/> always set, <see cref="Text"/> folded, <see cref="ContentType"/> and
        /// <see cref="Sha256"/> lowercase, and blank text as not set.
        /// </summary>
        /// <exception cref="ArgumentException">A criterion is not allowed, or a range is empty.</exception>
        public FileSearch Normalize()
        {
            int limit = Limit ?? DefaultLimit;
            if (limit < 1 || limit > MaxLimit)
            {
                throw new ArgumentException($"A page holds 1 to {MaxLimit} files, not {limit}.", nameof(Limit));
            }

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

            return new FileSearch
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
                Limit = limit,
                After = string.IsNullOrEmpty(After) ? null : After,
            };
        }

        /// <summary>Whether a stored content type is the one <see cref="ContentType"/> asks for. Call on a normalized search.</summary>
        public bool MatchesContentType(string storedContentType)
        {
            string mediaType = ContentTypes.MediaTypeOf(storedContentType);
            string? exact = ExactMediaType();
            string? prefix = MediaTypePrefix();
            return (exact == null || mediaType == exact)
                && (prefix == null || mediaType.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static bool IsFamily(string contentType)
        {
            return contentType.EndsWith(WildcardSubtype, StringComparison.Ordinal);
        }

        /// <summary>The query string carrying every criterion that is set, escaped, without a leading <c>?</c>.</summary>
        public string ToQuery()
        {
            List<string> parts = new List<string>();
            Add(parts, "under", Under);
            Add(parts, "text", Text);
            Add(parts, "contentType", ContentType);
            Add(parts, "minSizeBytes", MinSizeBytes?.ToString(CultureInfo.InvariantCulture));
            Add(parts, "maxSizeBytes", MaxSizeBytes?.ToString(CultureInfo.InvariantCulture));
            Add(parts, "createdFrom", CreatedFrom?.ToString("O", CultureInfo.InvariantCulture));
            Add(parts, "createdBefore", CreatedBefore?.ToString("O", CultureInfo.InvariantCulture));
            Add(parts, "updatedFrom", UpdatedFrom?.ToString("O", CultureInfo.InvariantCulture));
            Add(parts, "updatedBefore", UpdatedBefore?.ToString("O", CultureInfo.InvariantCulture));
            Add(parts, "sha256", Sha256);
            Add(parts, "limit", Limit?.ToString(CultureInfo.InvariantCulture));
            Add(parts, "after", After);
            return string.Join("&", parts);
        }

        private static void Add(List<string> parts, string name, string? value)
        {
            if (value != null)
            {
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
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
