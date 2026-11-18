namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>What a file's contents say about it, as <see cref="Services.MetadataReader"/> found it; every part null when not found.</summary>
    public sealed record ContentMetadata
    {
        /// <summary>Nothing found.</summary>
        public static readonly ContentMetadata None = new ContentMetadata();

        /// <summary>
        /// When a photo or video was taken. A camera that recorded no time zone gives its clock's time,
        /// which is kept as if it were UTC.
        /// </summary>
        public DateTimeOffset? TakenAt { get; init; }

        /// <summary>Where it was taken, in degrees; set together with <see cref="Longitude"/>.</summary>
        public double? Latitude { get; init; }

        public double? Longitude { get; init; }

        /// <summary>How long a video plays, from a QuickTime or MP4 header.</summary>
        public double? DurationSeconds { get; init; }

        /// <summary>A PDF's pages.</summary>
        public int? PageCount { get; init; }

        /// <summary>A PDF's title.</summary>
        public string? Title { get; init; }

        /// <summary>The camera or phone, as its make and model.</summary>
        public string? Camera { get; init; }
    }
}
