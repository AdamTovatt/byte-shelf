using ByteShelf.Server.Plugins.Processes;
using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Removes what plugins have of contents once no plain file, live or in the trash, holds them: the files derived
    /// from them, and every process that read them, for metadata too. They show what encryption hides, so they
    /// never outlive the plaintext.
    /// </summary>
    public interface IDerivedRemoval
    {
        /// <summary>
        /// Commits <paramref name="session"/>, which swapped <paramref name="released"/>, the contents of files, for
        /// others (encrypted or decrypted in place), with what plugins have of them removed first, so that
        /// nothing read from them is left once the swap is answered. Until it has committed no plugin is given
        /// those contents, which the rest of the server still sees as they were.
        /// </summary>
        /// <param name="released">The contents the files held before.</param>
        /// <param name="session">The transaction.</param>
        /// <param name="beforeCommit">What cannot be undone, done once nothing but the commit is left.</param>
        /// <exception cref="PluginNotStoppedException">A process that read them could not be ended, or what it left not removed; nothing is committed.</exception>
        /// <exception cref="IOException">Something derived from them could not be removed; nothing is committed.</exception>
        /// <exception cref="UnauthorizedAccessException">Something derived from them could not be removed; nothing is committed.</exception>
        Task CommitReleasingAsync(IReadOnlyCollection<string> released, IDbTransactionSession session, Action? beforeCommit = null);

        /// <summary>Removes what plugins have of the contents unless a plain file holds them; answers whether none does.</summary>
        /// <exception cref="PluginNotStoppedException">A process that read them could not be ended, or what it left not removed.</exception>
        Task<bool> RemoveUnheldAsync(string sha256, CancellationToken cancellationToken = default);
    }
}
