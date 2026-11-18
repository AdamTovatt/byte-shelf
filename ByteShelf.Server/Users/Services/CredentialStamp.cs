using System.Security.Claims;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Users.Repositories;
using EasyReasy.Auth;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Ties a session to what it was signed in with: the password, and the passkey when it was a passkey.
    /// A refresh token stores the stamp of the user's credential as it was when the sign-in was checked,
    /// and a refresh, or a sign-in still finishing, is refused once the credential is another: a password
    /// change or reset ends the sessions that predate it, passkey sessions included. A session signed in
    /// with a passkey also names it, and ends once the passkey is removed. A replica takes the login and
    /// passkey rows whole from its primary's change log, so either change made there ends the replica's
    /// sessions too, with nothing told to the replica.
    /// </summary>
    /// <remarks>
    /// The stamp is the SHA-256 of the stored hash. The hash holds a random salt of its own, so every new
    /// password, even the same one set again, gives a new stamp, and the stamp tells nothing about the
    /// password. A refresh token stores these claims, so EasyReasy.Auth also puts them on the access
    /// tokens a refresh mints, and <see cref="SessionIssuer"/> puts them on a session's first one.
    /// </remarks>
    public static class CredentialStamp
    {
        public const string ClaimType = "credential_stamp";

        /// <summary>The claim naming the passkey a session was signed in with.</summary>
        public const string PasskeyClaimType = "passkey_id";

        /// <summary>
        /// The claims a refresh token stores, serialized, for a session signed in with
        /// <paramref name="credential"/>, and with <paramref name="passkeyId"/> when it was a passkey.
        /// </summary>
        public static string SerializeClaimsFor(string credential, Guid? passkeyId)
        {
            return RefreshTokenClaims.SerializeClaims(ClaimsFor(credential, passkeyId))
                ?? throw new InvalidOperationException("A stamp claim serialized to nothing.");
        }

        /// <summary>The claims a refresh token stores for a session signed in with <paramref name="credential"/>, and <paramref name="passkeyId"/> when given.</summary>
        public static IReadOnlyList<Claim> ClaimsFor(string credential, Guid? passkeyId)
        {
            List<Claim> claims = new List<Claim> { new Claim(ClaimType, Of(credential)) };
            if (passkeyId is Guid passkey)
            {
                claims.Add(new Claim(PasskeyClaimType, passkey.ToString("D")));
            }

            return claims;
        }

        /// <summary>Whether <paramref name="storedClaims"/> were stored for <paramref name="credential"/>; never for claims without a stamp.</summary>
        public static bool Matches(IEnumerable<Claim> storedClaims, string credential)
        {
            string? stamp = storedClaims.FirstOrDefault(claim => claim.Type == ClaimType)?.Value;
            return string.Equals(stamp, Of(credential), StringComparison.Ordinal);
        }

        /// <summary>The passkey <paramref name="claims"/> say the session was signed in with; null for a password session.</summary>
        public static Guid? PasskeyOf(IEnumerable<Claim> claims)
        {
            string? value = claims.FirstOrDefault(claim => claim.Type == PasskeyClaimType)?.Value;
            return value == null ? null : Guid.Parse(value);
        }

        /// <summary>
        /// Whether the session <paramref name="claims"/> belong to was signed in with a passkey
        /// <paramref name="userId"/> no longer has, which ends it: a refresh and every request with its
        /// access token are refused.
        /// </summary>
        public static async Task<bool> PasskeyRemovedAsync(IEnumerable<Claim> claims, Guid userId, IPasskeyRepository passkeys)
        {
            return PasskeyOf(claims) is Guid passkeyId && !await passkeys.ExistsAsync(userId, passkeyId);
        }

        private static string Of(string credential)
        {
            return SecretHash.Sha256Hex(credential);
        }
    }
}
