using EasyReasy.Auth;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// The session a request was made from, as its own signed access token names it: its refresh token
    /// family, and the passkey it was signed in with, if any. A password change keeps this session by
    /// moving it onto the new password with its passkey, so it still ends with the passkey.
    /// </summary>
    public sealed record SignedInSession(string FamilyId, Guid? PasskeyId)
    {
        /// <summary>The session of <paramref name="httpContext"/>'s access token; null for a token naming none.</summary>
        public static SignedInSession? Of(HttpContext httpContext)
        {
            string? familyId = httpContext.GetRefreshFamilyId();
            return string.IsNullOrEmpty(familyId) ? null : new SignedInSession(familyId, CredentialStamp.PasskeyOf(httpContext.User.Claims));
        }
    }
}
