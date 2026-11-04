using ByteShelfCommon;

namespace ByteShelfClient.Http
{
    /// <summary>How <see cref="ByteShelfHttpClient"/> sends files.</summary>
    public sealed class ByteShelfHttpClientOptions
    {
        /// <summary>
        /// The largest file sent in one request; anything larger goes as a resumable upload in chunks.
        /// Keep it under any request size limit of a proxy in front of the server. 32 MiB by default.
        /// </summary>
        public long SingleRequestMaxBytes { get; init; } = 32L * 1024 * 1024;

        /// <summary>
        /// The size of each chunk of a resumable upload, held in memory while it is sent; at most
        /// <see cref="UploadLimits.MaxChunkBytes"/>. 8 MiB by default.
        /// </summary>
        public int ChunkBytes { get; init; } = 8 * 1024 * 1024;

        /// <summary>
        /// The address a download link the server answers relative is made absolute against, ending in
        /// <c>/</c>, for a client that reaches the server at another address than the people the links
        /// are for, such as the server calling itself. The client's base address when left out. A link
        /// the server answers on its configured public URL is kept as it is.
        /// </summary>
        public Uri? LinkBaseAddress { get; init; }
    }
}
