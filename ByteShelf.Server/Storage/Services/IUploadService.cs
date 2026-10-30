using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// Getting bytes onto a shelf. Every upload reserves its whole size against the quota when it
    /// starts, is written to <c>staging/</c>, hashed by the server, moved into <c>objects/</c>, and gets
    /// its file row in one transaction.
    /// </summary>
    public interface IUploadService
    {
        /// <summary>Stores <paramref name="content"/> at <paramref name="path"/> in one go: a single streamed PUT.</summary>
        Task<ShelfFile> StoreAsync(Shelf shelf, string path, string? contentType, long sizeBytes, bool overwrite, Stream content, CancellationToken cancellationToken);

        Task<UploadSession> StartAsync(Shelf shelf, StartUploadRequest request);

        Task<UploadSession> GetAsync(Shelf shelf, Guid uploadId);

        /// <summary>Appends a chunk, which must start where the bytes received so far end.</summary>
        Task<UploadSession> AppendAsync(Shelf shelf, Guid uploadId, long offset, Stream content, CancellationToken cancellationToken);

        /// <summary>
        /// Stores the file once every declared byte has arrived. When the path was taken in the meantime
        /// the upload is kept, so the caller can abort it.
        /// </summary>
        Task<ShelfFile> CompleteAsync(Shelf shelf, Guid uploadId, CancellationToken cancellationToken);

        Task AbortAsync(Shelf shelf, Guid uploadId);
    }
}
