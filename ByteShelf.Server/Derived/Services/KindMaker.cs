using ByteShelf.Server.Derived.Models;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>A plugin as the maker of one kind.</summary>
    internal sealed record KindMaker(DerivedFilePlugin Plugin, DeclaredKind Declaration)
    {
        /// <summary>Whether <paramref name="entry"/> is what it made, or failed to make, at its version.</summary>
        public bool IsCurrent(DerivedEntry? entry)
        {
            return entry != null && entry.Plugin == Plugin.Id && entry.Version == Declaration.Version;
        }
    }
}
