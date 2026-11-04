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
        /// <summary>
        /// Stores <paramref name="content"/> as <paramref name="request"/> describes in one go: a single
        /// streamed PUT, to the API or through an upload link.
        /// </summary>
        /// <param name="startedBy">Who stores it, recorded on the upload as <see cref="UploadRecord.StartedBy"/>.</param>
        Task<ShelfFile> StoreAsync(Shelf shelf, StartUploadRequest request, string startedBy, Stream content, CancellationToken cancellationToken);

        /// <summary>
        /// Stores through an upload link, as <see cref="StoreAsync"/> does, refused when it finishes too if
        /// the place is then under an encrypted directory or holds an encrypted file
        /// (<see cref="ShelfEncryption.RequireLinkTargetAsync"/>): it may have been encrypted meanwhile.
        /// </summary>
        /// <exception cref="Shared.Errors.ConflictException">The place took encryption while the body arrived.</exception>
        Task<ShelfFile> StoreThroughLinkAsync(Shelf shelf, StartUploadRequest request, string startedBy, Stream content, CancellationToken cancellationToken);

        /// <param name="startedBy">Who starts it, recorded as <see cref="UploadRecord.StartedBy"/>.</param>
        Task<UploadSession> StartAsync(Shelf shelf, StartUploadRequest request, string startedBy);

        /// <summary>
        /// Swaps a file's contents in place for <paramref name="content"/> in one go, as
        /// <paramref name="request"/> describes: encrypting or decrypting it, keeping its id. Its old
        /// contents are removed at once, never trashed, unless another file holds them.
        /// </summary>
        /// <exception cref="Shared.Errors.NotFoundException">The shelf has no such live file.</exception>
        /// <exception cref="Shared.Errors.ConflictException">The swap is not allowed as things stand (<see cref="ShelfEncryption.RequireSwapAsync"/>).</exception>
        Task<ShelfFile> SwapAsync(Shelf shelf, StartContentSwapRequest request, string startedBy, Stream content, CancellationToken cancellationToken);

        /// <summary>Starts a resumable upload that swaps a file's contents when it completes, as <see cref="SwapAsync"/> does.</summary>
        Task<UploadSession> StartSwapAsync(Shelf shelf, StartContentSwapRequest request, string startedBy);

        /// <exception cref="Shared.Errors.NotFoundException">The shelf has no such upload.</exception>
        Task<UploadRecord> GetAsync(Shelf shelf, Guid uploadId);

        /// <summary>An upload as the API answers it.</summary>
        UploadSession ToSession(UploadRecord upload);

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
