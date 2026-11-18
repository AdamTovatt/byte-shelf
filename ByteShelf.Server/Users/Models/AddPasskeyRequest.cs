using System.Text.Json;

namespace ByteShelf.Server.Users.Models
{
    /// <summary>Finishes adding a passkey with what the browser made.</summary>
    public sealed class AddPasskeyRequest
    {
        /// <summary>The ceremony's id, from <see cref="PasskeyCeremony.CeremonyId"/>.</summary>
        public required string CeremonyId { get; init; }

        /// <summary>The new credential, as <c>PublicKeyCredential.toJSON()</c> writes it.</summary>
        public required JsonElement Credential { get; init; }

        /// <summary>The name the user will know it by.</summary>
        public required string Name { get; init; }
    }
}
