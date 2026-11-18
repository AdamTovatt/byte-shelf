using ByteShelf.Server.Shared;
using EasyReasy.Auth;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Who passkeys are made for: the host of <c>APP_BASE_URL</c>, at its origin. Without
    /// <c>APP_BASE_URL</c> passkeys are off, and so they are when it is one no browser runs a ceremony on
    /// (plain http to anything but this machine), which is logged as a warning. A passkey works only on the
    /// host it was made for, so a replica under another hostname has passkeys of its own to none.
    /// </summary>
    public sealed class PasskeyRelyingParty
    {
        /// <summary>The name an authenticator shows beside a passkey.</summary>
        public const string Name = "ByteShelf";

        private PasskeyRelyingParty(WebAuthnRelyingParty? relyingParty)
        {
            RelyingParty = relyingParty;
        }

        /// <summary>The relying party ceremonies are verified for; null while passkeys are off.</summary>
        public WebAuthnRelyingParty? RelyingParty { get; }

        public static PasskeyRelyingParty For(PublicAddress publicAddress, ILogger logger)
        {
            if (publicAddress.Configured is not Uri baseUrl)
            {
                return new PasskeyRelyingParty(null);
            }

            try
            {
                return new PasskeyRelyingParty(new WebAuthnRelyingParty(baseUrl.IdnHost, Name, new[] { baseUrl.GetLeftPart(UriPartial.Authority) }));
            }
            catch (ArgumentException exception)
            {
                logger.LogWarning("Passkeys are off: APP_BASE_URL {AppBaseUrl} is not an address a browser makes passkeys for. {Reason}", baseUrl, exception.Message);
                return new PasskeyRelyingParty(null);
            }
        }
    }
}
