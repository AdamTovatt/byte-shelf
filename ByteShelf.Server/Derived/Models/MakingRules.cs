using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>How a group of derived files is made (<see cref="DerivedKinds.RulesOf"/>).</summary>
    /// <param name="Job">
    /// The job that makes it in the background, for every file that has it, of contents as they are stored and of those
    /// stored already; null for a group made only when asked for.
    /// </param>
    /// <param name="MadeWhenAsked">
    /// Whether a missing one is made as someone asks for it. One that is not is left to the background: making it takes
    /// too long to be waited for.
    /// </param>
    /// <param name="Lane">The lane its runs take, which says how long one may.</param>
    public sealed record MakingRules(string? Job, bool MadeWhenAsked, PluginLane Lane);
}
