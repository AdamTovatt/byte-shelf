namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>
    /// The media types whose files' contents are read for metadata: what the reading job lists to read,
    /// and what <c>FileMetadata.Readable</c> tells the clients. <see cref="Services.MetadataReader"/>
    /// knows the formats by their bytes and reads no other kinds.
    /// </summary>
    public static class ReadMediaTypes
    {
        /// <summary>The families of media types read, each with its slash.</summary>
        public static readonly IReadOnlyList<string> Families = ["image/", "video/", "audio/"];

        /// <summary>The single media types read, besides <see cref="Families"/>.</summary>
        public static readonly IReadOnlyList<string> Types = ["application/pdf"];

        /// <summary>Whether files of <paramref name="mediaType"/> (without parameters, lowercase) are read.</summary>
        public static bool Include(string mediaType)
        {
            return Families.Any(family => mediaType.StartsWith(family, StringComparison.Ordinal))
                || Types.Contains(mediaType);
        }
    }
}
