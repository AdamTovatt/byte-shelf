namespace ByteShelfCommon
{
    /// <summary>One media type (<c>application/pdf</c>) or a family of them (<c>image/*</c>), and which media types it covers.</summary>
    public sealed record MediaTypePattern
    {
        private const string WildcardSubtype = "/*";

        private MediaTypePattern(string? type, string? prefix)
        {
            Type = type;
            Prefix = prefix;
        }

        /// <summary>The one media type; null for a family.</summary>
        public string? Type { get; }

        /// <summary>What every type of the family starts with, such as <c>image/</c>; null for one type.</summary>
        public string? Prefix { get; }

        /// <summary>The pattern <paramref name="pattern"/> writes, lowercase. It checks no names: what takes a pattern from outside says which it takes.</summary>
        public static MediaTypePattern Of(string pattern)
        {
            ArgumentNullException.ThrowIfNull(pattern);
            return pattern.EndsWith(WildcardSubtype, StringComparison.Ordinal)
                ? new MediaTypePattern(null, pattern[..^1])
                : new MediaTypePattern(pattern, null);
        }

        /// <summary>The pattern as <see cref="Of"/> takes it.</summary>
        public override string ToString()
        {
            return Type ?? $"{Prefix}*";
        }

        /// <summary>Whether it covers <paramref name="mediaType"/>, without parameters and lowercase.</summary>
        public bool Includes(string mediaType)
        {
            ArgumentNullException.ThrowIfNull(mediaType);
            return Prefix == null ? mediaType == Type : mediaType.StartsWith(Prefix, StringComparison.Ordinal);
        }
    }
}
