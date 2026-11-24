using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Removes what was derived from contents once no plain file, live or in the trash, holds them: derived
    /// files show what encryption hides, so they never outlive the plaintext.
    /// </summary>
    public interface IDerivedRemoval
    {
        /// <summary>
        /// Commits <paramref name="session"/>, which swapped <paramref name="released"/>, the contents of files, for
        /// others (encrypted or decrypted in place), with what was derived from them removed first, so that
        /// nothing read from them is left once the swap is answered. Until it has committed nothing is derived from
        /// those contents, which the rest of the server still sees as they were.
        /// </summary>
        /// <param name="released">The contents the files held before.</param>
        /// <param name="session">The transaction.</param>
        /// <param name="beforeCommit">What cannot be undone, done once nothing but the commit is left.</param>
        /// <exception cref="MakerNotStoppedException">Something being made from them could not be stopped or removed; nothing is committed.</exception>
        /// <exception cref="IOException">Something derived from them could not be removed; nothing is committed.</exception>
        /// <exception cref="UnauthorizedAccessException">Something derived from them could not be removed; nothing is committed.</exception>
        Task CommitReleasingAsync(IReadOnlyCollection<string> released, IDbTransactionSession session, Action? beforeCommit = null);

        /// <summary>Removes what was derived from the contents unless a plain file holds them; answers whether none does.</summary>
        Task<bool> RemoveUnheldAsync(string sha256, CancellationToken cancellationToken = default);
    }
}
