using EasyReasy.Database;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>Moves a session onto a new credential, for the password change made from it (<see cref="Services.CredentialStamp"/>).</summary>
    public interface ISessionStampStore
    {
        /// <summary>
        /// Replaces the stored claims of the live refresh tokens in <paramref name="familyId"/>, a session of
        /// <paramref name="subject"/>, in the caller's transaction.
        /// </summary>
        Task RestampFamilyAsync(string subject, string familyId, string serializedClaims, IDbSession session);
    }
}
