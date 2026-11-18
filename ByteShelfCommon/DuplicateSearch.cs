using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ByteShelfCommon
{
    /// <summary>
    /// The duplicates among the files that meet the <see cref="FileCriteria"/>: the plain, non-empty
    /// files that share their contents with another of them, in groups, most repeated bytes first, a
    /// page of groups at a time. Only the files the criteria take count, so with
    /// <see cref="FileCriteria.Under"/> a group holds only files under it. Encrypted files are left
    /// out, as two encryptions of the same file never share a SHA-256, and so are empty ones, whose
    /// copies tidy nothing away. Pass a page's <see cref="DuplicateSearchResults.NextCursor"/> as
    /// <see cref="After"/> for the next one.
    /// </summary>
    public sealed class DuplicateSearch : FileCriteria
    {
        /// <summary>How many groups a page holds when <see cref="Limit"/> is not set.</summary>
        public const int DefaultLimit = 100;

        /// <summary>The most groups one page may hold.</summary>
        public const int MaxLimit = 1000;

        /// <summary>A search to set with an object initializer.</summary>
        public DuplicateSearch()
        {
        }

        /// <summary>A search by <paramref name="criteria"/>, as they are; set the paging with an object initializer.</summary>
        public DuplicateSearch(FileCriteria criteria)
            : base(criteria)
        {
        }

        /// <summary>How many groups a page holds: 1 to <see cref="MaxLimit"/>, <see cref="DefaultLimit"/> when null.</summary>
        public int? Limit { get; init; }

        /// <summary>The previous page's <see cref="DuplicateSearchResults.NextCursor"/>; null for the first page.</summary>
        public DuplicateCursor? After { get; init; }

        /// <summary>
        /// This search with every criterion checked and put the way it is compared, as
        /// <see cref="FileCriteria"/> does, and <see cref="Limit"/> always set.
        /// </summary>
        /// <exception cref="ArgumentException">A criterion or the limit is not allowed, or a range is empty.</exception>
        public DuplicateSearch Normalize()
        {
            int limit = Limit ?? DefaultLimit;
            if (limit < 1 || limit > MaxLimit)
            {
                throw new ArgumentException($"A page holds 1 to {MaxLimit} groups of duplicates, not {limit}.", nameof(Limit));
            }

            return new DuplicateSearch(NormalizeCriteria()) { Limit = limit, After = After };
        }

        /// <summary>The query string carrying every criterion that is set, escaped, without a leading <c>?</c>.</summary>
        public string ToQuery()
        {
            List<string> parts = QueryParts();
            AddQueryPart(parts, "limit", Limit?.ToString(CultureInfo.InvariantCulture));
            AddQueryPart(parts, "after", After?.ToString());
            return string.Join("&", parts);
        }
    }

    /// <summary>
    /// Where a page of <see cref="DuplicateSearchResults"/> ended: its last group's
    /// <see cref="DuplicateGroup.RepeatedBytes"/> and SHA-256, written as <c>bytes:sha256</c>.
    /// </summary>
    [JsonConverter(typeof(DuplicateCursorJsonConverter))]
    public readonly record struct DuplicateCursor(long RepeatedBytes, string Sha256)
    {
        private const char Separator = ':';

        /// <summary>The cursor a page ending with <paramref name="group"/> passes on.</summary>
        public static DuplicateCursor Of(DuplicateGroup group)
        {
            ArgumentNullException.ThrowIfNull(group);
            return new DuplicateCursor(group.RepeatedBytes, group.Sha256);
        }

        /// <summary>The cursor <paramref name="text"/> writes, as <see cref="ToString"/> writes it.</summary>
        /// <exception cref="ArgumentException">It is not a cursor a page of duplicates ended with.</exception>
        public static DuplicateCursor Parse(string text)
        {
            return TryParse(text, out DuplicateCursor cursor)
                ? cursor
                : throw new ArgumentException($"'{text}' is not a cursor a page of duplicates ended with.", nameof(text));
        }

        /// <summary>Reads the cursor <paramref name="text"/> writes; false when it is not one. Binding a query string reads it here.</summary>
        public static bool TryParse(string? text, out DuplicateCursor cursor)
        {
            cursor = default;
            int separator = text?.IndexOf(Separator, StringComparison.Ordinal) ?? -1;
            if (separator < 0
                || !long.TryParse(text.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out long repeatedBytes)
                || !IsSha256(text![(separator + 1)..]))
            {
                return false;
            }

            cursor = new DuplicateCursor(repeatedBytes, text[(separator + 1)..]);
            return true;
        }

        /// <summary>Whether <paramref name="group"/> comes after this cursor, on a later page.</summary>
        public bool Precedes(DuplicateGroup group)
        {
            ArgumentNullException.ThrowIfNull(group);
            return group.RepeatedBytes < RepeatedBytes
                || (group.RepeatedBytes == RepeatedBytes && string.CompareOrdinal(group.Sha256, Sha256) > 0);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Create(CultureInfo.InvariantCulture, $"{RepeatedBytes}{Separator}{Sha256}");
        }

        private static bool IsSha256(string text)
        {
            return text.Length == 64 && text.All(character => char.IsAsciiDigit(character) || (character >= 'a' && character <= 'f'));
        }
    }

    /// <summary>A <see cref="DuplicateCursor"/> in JSON: the string <see cref="DuplicateCursor.ToString"/> writes.</summary>
    public sealed class DuplicateCursorJsonConverter : JsonConverter<DuplicateCursor>
    {
        /// <inheritdoc />
        public override DuplicateCursor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? text = reader.GetString();
            return DuplicateCursor.TryParse(text, out DuplicateCursor cursor)
                ? cursor
                : throw new JsonException($"'{text}' is not a cursor a page of duplicates ended with.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, DuplicateCursor value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.ToString());
        }
    }
}
