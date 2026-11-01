using System.Security.Claims;
using ByteShelf.Server.Shared.Auth;
using EasyReasy.Auth;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Ties a session to the password it was signed in with. A refresh token stores the stamp of the
    /// user's credential as it was when the password was checked, and a refresh, or a sign-in still
    /// finishing, is refused once the credential is another: a password change or reset ends the
    /// sessions that predate it. A replica takes the login row whole from its primary's change log, so
    /// a password changed there ends the replica's sessions too, with nothing told to the replica.
    /// </summary>
    /// <remarks>
    /// The stamp is the SHA-256 of the stored hash. The hash holds a random salt of its own, so every new
    /// password, even the same one set again, gives a new stamp, and the stamp tells nothing about the
    /// password. It is the one claim a refresh token stores, so EasyReasy.Auth also puts it on the
    /// access tokens a refresh mints.
    /// </remarks>
    public static class CredentialStamp
    {
        public const string ClaimType = "credential_stamp";

        /// <summary>The claims a refresh token stores, serialized, for a session signed in with <paramref name="credential"/>.</summary>
        public static string SerializeClaimsFor(string credential)
        {
            return RefreshTokenClaims.SerializeClaims(ClaimsFor(credential))
                ?? throw new InvalidOperationException("A stamp claim serialized to nothing.");
        }

        /// <summary>The claims a refresh token stores for a session signed in with <paramref name="credential"/>.</summary>
        public static IReadOnlyList<Claim> ClaimsFor(string credential)
        {
            return new[] { new Claim(ClaimType, Of(credential)) };
        }

        /// <summary>Whether <paramref name="storedClaims"/> were stored for <paramref name="credential"/>; never for claims without a stamp.</summary>
        public static bool Matches(IEnumerable<Claim> storedClaims, string credential)
        {
            string? stamp = storedClaims.FirstOrDefault(claim => claim.Type == ClaimType)?.Value;
            return string.Equals(stamp, Of(credential), StringComparison.Ordinal);
        }

        private static string Of(string credential)
        {
            return SecretHash.Sha256Hex(credential);
        }
    }
}
