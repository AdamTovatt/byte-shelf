namespace ByteShelfCommon
{
    /// <summary>
    /// What a file's contents say about it, read by the server in the background after the file is
    /// stored: a photo's time, place and camera, a video's length, a PDF's title and pages.
    /// Each part is null when the contents do not carry it. An encrypted file has none, since the
    /// server never sees its contents; nor does any file of the in-memory client, which reads nothing.
    /// </summary>
    public sealed class FileMetadata
    {
        /// <summary>None read, nor to be: what an encrypted file, one of a type not read, and the in-memory client's files have.</summary>
        public static readonly FileMetadata None = new FileMetadata();

        /// <summary>
        /// Whether the server reads this file's contents at all: images, videos, sounds and PDFs that are
        /// not encrypted. False for any other file, whose <see cref="ReadAt"/> stays null for good.
        /// </summary>
        public bool Readable { get; init; }

        /// <summary>
        /// When the server read the contents; null when it has not, which for a <see cref="Readable"/> file
        /// means not yet: the server reads it in the background soon after it is stored.
        /// </summary>
        public DateTimeOffset? ReadAt { get; init; }

        /// <summary>
        /// When the photo or video was taken. A camera that recorded no time zone gives its clock's time,
        /// which is given here as if it were UTC.
        /// </summary>
        public DateTimeOffset? TakenAt { get; init; }

        /// <summary>Where it was taken, in degrees north; set exactly when <see cref="Longitude"/> is.</summary>
        public double? Latitude { get; init; }

        /// <summary>Where it was taken, in degrees east.</summary>
        public double? Longitude { get; init; }

        /// <summary>How long the video plays, in seconds, from a QuickTime or MP4 file's header; sound formats such as MP3, WAV or FLAC give none.</summary>
        public double? DurationSeconds { get; init; }

        /// <summary>How many pages the PDF has.</summary>
        public int? PageCount { get; init; }

        /// <summary>The PDF's title. Search matches it as it does the file's name.</summary>
        public string? Title { get; init; }

        /// <summary>The camera or phone, as its make and model. Search matches it as it does the file's name.</summary>
        public string? Camera { get; init; }
    }
}
