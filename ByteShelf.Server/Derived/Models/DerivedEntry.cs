namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What is known of one kind of derived file of some contents: the JSON kept beside it.</summary>
    /// <param name="Plugin">The id of the plugin that made it, or failed to.</param>
    /// <param name="Version">The version that plugin makes the kind at.</param>
    /// <param name="Made">What was made; null when making it failed, which that version does not try again.</param>
    public sealed record DerivedEntry(string Plugin, int Version, MadeInfo? Made);
}
