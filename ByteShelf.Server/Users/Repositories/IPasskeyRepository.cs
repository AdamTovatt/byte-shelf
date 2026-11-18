using ByteShelf.Server.Users.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>
    /// The <c>passkey</c> table. Every write to a passkey logs it in the caller's transaction; recording a
    /// sign-in does not, since the sign count and last use are this server's own.
    /// </summary>
    public interface IPasskeyRepository
    {
        /// <summary>The passkey with <paramref name="credentialId"/>, whoever's it is; null when there is none.</summary>
        Task<StoredPasskey?> GetByCredentialIdAsync(string credentialId);

        /// <summary>The user's passkeys, oldest first.</summary>
        Task<IReadOnlyList<StoredPasskey>> GetForUserAsync(Guid userId);

        /// <summary>Whether the user has the passkey <paramref name="passkeyId"/>, which a session signed in with it needs.</summary>
        Task<bool> ExistsAsync(Guid userId, Guid passkeyId);

        Task InsertAsync(Passkey passkey, IDbSession session);

        /// <summary>Renames the user's passkey; false when they have no such passkey.</summary>
        Task<bool> RenameAsync(Guid userId, Guid passkeyId, string name, DateTimeOffset at, IDbSession session);

        /// <summary>Removes the user's passkey; false when they have no such passkey.</summary>
        Task<bool> DeleteAsync(Guid userId, Guid passkeyId, DateTimeOffset at, IDbSession session);

        /// <summary>Removes every passkey of the user, for deleting them.</summary>
        Task DeleteForUserAsync(Guid userId, DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Writes back the sign count a sign-in verified, unless one verified at the same time wrote a higher
        /// one, and when it was; not logged.
        /// </summary>
        Task RecordSignInAsync(Guid passkeyId, uint signCount, DateTimeOffset at);

        /// <summary>
        /// Writes a passkey as a primary's change log holds it, logged as <paramref name="action"/>, keeping
        /// this server's sign count and last use. A different passkey with the same credential id is
        /// removed first: the log brings it back if it still exists.
        /// </summary>
        Task UpsertReplicatedAsync(Passkey passkey, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a passkey by id; false when there is none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session);
    }
}
