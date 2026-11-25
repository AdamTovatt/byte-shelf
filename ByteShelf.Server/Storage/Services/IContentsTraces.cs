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

        /// <summary>
        /// Brings what the database says of the contents' traces back in line with what is left of them, after a session
        /// that removed some (<see cref="RemoveForCommitAsync"/>) did not commit: its writes were undone, the removal not.
        /// Hold no lock of the contents.
        /// </summary>
        Task ReconcileAsync(string sha256);

        /// <summary>Removes what there is of the contents unless a plain file holds them; answers whether none does.</summary>
        /// <exception cref="TracesNotRemovedException">Some could not be removed.</exception>
        Task<bool> RemoveUnheldAsync(string sha256, CancellationToken cancellationToken = default);
    }
}
