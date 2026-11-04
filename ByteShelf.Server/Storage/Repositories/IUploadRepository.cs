using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Repositories
{
    public interface IUploadRepository
    {
        Task<UploadRecord?> GetByIdAsync(Guid id, IDbSession? session = null);

        Task InsertAsync(UploadRecord upload, IDbSession session);

        /// <summary>
        /// Records that the upload now holds <paramref name="receivedBytes"/>, only if it still held
        /// <paramref name="expectedReceivedBytes"/>. False when it did not, or no longer exists.
        /// </summary>
        Task<bool> UpdateReceivedAsync(Guid id, long expectedReceivedBytes, long receivedBytes, DateTimeOffset activityAt, IDbSession? session = null);

        Task<bool> DeleteAsync(Guid id, IDbSession? session = null);

        /// <summary>
        /// Uploads with no activity since <paramref name="idleBefore"/>, and single-PUT uploads started
        /// before <paramref name="singlePutsStartedBefore"/> when it is given.
        /// </summary>
        Task<IReadOnlyList<UploadRecord>> GetAbandonedAsync(DateTimeOffset idleBefore, DateTimeOffset? singlePutsStartedBefore, IDbSession? session = null);

        /// <summary>The ids of every upload, for matching against what is in <c>staging/</c>.</summary>
        Task<IReadOnlySet<Guid>> GetAllIdsAsync(IDbSession? session = null);
    }
}
