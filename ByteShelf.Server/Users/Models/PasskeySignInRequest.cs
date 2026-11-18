using System.Text.Json;

namespace ByteShelf.Server.Users.Models
{
    /// <summary>Finishes signing in with a passkey.</summary>
    public sealed class PasskeySignInRequest
    {
        /// <summary>The ceremony's id, from <see cref="PasskeyCeremony.CeremonyId"/>.</summary>
        public required string CeremonyId { get; init; }

        /// <summary>The assertion, as <c>PublicKeyCredential.toJSON()</c> writes it.</summary>
        public required JsonElement Credential { get; init; }
    }
}
