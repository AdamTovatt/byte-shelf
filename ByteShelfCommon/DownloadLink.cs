namespace ByteShelfCommon
{
    /// <summary>A link to one file's contents that works without a token until it runs out, for a browser to open or save.</summary>
    public sealed class DownloadLink
    {
        /// <summary>The link, relative to the server's address; add <c>?download=true</c> to have the browser save it.</summary>
        public required string Url { get; init; }

        /// <summary>When the link stops working.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
