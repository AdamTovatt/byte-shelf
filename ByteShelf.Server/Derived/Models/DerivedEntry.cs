using System.Text.Json.Serialization;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What is known of one kind of derived file of some contents: the JSON kept beside it.</summary>
    /// <param name="Plugin">The id of the plugin that made it, or failed to.</param>
    /// <param name="Version">The version that plugin makes the kind at.</param>
    /// <param name="Outcome">How making it ended.</param>
    public sealed record DerivedEntry(string Plugin, int Version, DerivedOutcome Outcome)
    {
        /// <summary>What made it and at which version, in one word, which tells it from another made in its place.</summary>
        [JsonIgnore]
        public string Tag => $"{Plugin}-{Version}";
    }
}
