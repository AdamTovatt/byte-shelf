using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>A derived file, already open. Whoever sends it disposes it.</summary>
    public sealed class DerivedFile : IAsyncDisposable
    {
        public required DerivedKind Kind { get; init; }

        /// <summary>The SHA-256 of the contents it was made from.</summary>
        public required string Sha256 { get; init; }

        /// <summary>What made it and at which version (<see cref="DerivedEntry.Tag"/>).</summary>
        public required string Tag { get; init; }

        public required string MediaType { get; init; }

        public required FileStream Contents { get; init; }

        public ValueTask DisposeAsync()
        {
            return Contents.DisposeAsync();
        }
    }
}
