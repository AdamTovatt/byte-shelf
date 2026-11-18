using System.Text.Json;

namespace ByteShelf.Server.Users.Models
{
    /// <summary>A passkey ceremony begun: what the browser is handed, and the id its answer goes back with.</summary>
    public sealed class PasskeyCeremony
    {
        /// <summary>Hand it back with the browser's answer; it works once, for <c>PasskeyChallenges.Lifetime</c>.</summary>
        public required string CeremonyId { get; init; }

        /// <summary>
        /// The options, as <c>PublicKeyCredential.parseCreationOptionsFromJSON()</c> (adding a passkey) or
        /// <c>parseRequestOptionsFromJSON()</c> (signing in) take them.
        /// </summary>
        public required JsonElement Options { get; init; }
    }
}
