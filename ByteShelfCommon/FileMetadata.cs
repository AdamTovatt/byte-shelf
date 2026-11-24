namespace ByteShelfCommon
{
    /// <summary>
    /// What a file's contents say about it, read by the server in the background after the file is
    /// stored: a photo's time, place and camera, a video's length, a PDF's title and pages.
    /// Each part is null when the contents do not carry it. An encrypted file has none, since the
    /// server never sees its contents; nor does any file of the in-memory client, which reads nothing.
    /// Beside it, <see cref="Plugins"/> has what plugins keep, which any of those may still have.
    /// </summary>
    public sealed class FileMetadata
    {
        private static readonly IReadOnlyDictionary<string, PluginFileDetails> NoPlugins = new Dictionary<string, PluginFileDetails>();

        private readonly IReadOnlyDictionary<string, PluginFileDetails>? _plugins;

        /// <summary>None read, nor to be, and nothing kept by a plugin.</summary>
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

        /// <summary>
        /// What each installed plugin keeps about the file, by the plugin's id, for those that keep or have kept
        /// anything: a document about the file's contents, read in the background, and one about the file itself.
        /// </summary>
        /// <remarks>Empty, too, from a server that keeps nothing for plugins, whose answer does not have it.</remarks>
        public IReadOnlyDictionary<string, PluginFileDetails> Plugins
        {
            get => _plugins ?? NoPlugins;
            init => _plugins = value;
        }
    }
}
