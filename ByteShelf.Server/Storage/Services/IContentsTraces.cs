using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// What the server has of contents beside the files that hold them, which shows what encryption hides: what
    /// was made from them and what may still hold some of them. It never outlives the plaintext.
    /// </summary>
    public interface IContentsTraces
    {
        /// <summary>
        /// Removes what there is of the contents unless a plain file, live or in the trash, holds them as
        /// <paramref name="session"/> sees it, and has none come to be until the answer is disposed: the rest of
        /// the server still sees them as they were until the session has committed.
        /// </summary>
        /// <exception cref="TracesNotRemovedException">Some could not be removed.</exception>
        Task<IDisposable> RemoveForCommitAsync(string sha256, IDbSession session);

        /// <summary>Removes what there is of the contents unless a plain file holds them; answers whether none does.</summary>
        /// <exception cref="TracesNotRemovedException">Some could not be removed.</exception>
        Task<bool> RemoveUnheldAsync(string sha256, CancellationToken cancellationToken = default);
    }
}
