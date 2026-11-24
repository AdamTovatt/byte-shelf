using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>
    /// The media types whose files' contents the server reads itself: what its own reader lists to read, and
    /// what <c>FileMetadata.Readable</c> tells the clients. <see cref="Services.MetadataReader"/> knows the
    /// formats by their bytes and reads no other kinds.
    /// </summary>
    public static class ReadMediaTypes
    {
        public static readonly MediaTypeSet All = MediaTypeSet.Of("image/*", "video/*", "audio/*", "application/pdf");
    }
}
