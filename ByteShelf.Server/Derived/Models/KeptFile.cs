using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>
    /// What is to be kept of one kind: a file made, with where it is, or only what came of making it. Made only by its
    /// factories, so a file is there exactly when <see cref="Entry"/> says it was made.
    /// </summary>
    public sealed record KeptFile
    {
        private KeptFile(DerivedKind kind, DerivedEntry entry, string? sourcePath)
        {
            Kind = kind;
            Entry = entry;
            SourcePath = sourcePath;
        }

        public DerivedKind Kind { get; }

        /// <summary>What is known of it.</summary>
        public DerivedEntry Entry { get; }

        /// <summary>The derived file, which is moved into place; null unless <see cref="Entry"/> says it was made.</summary>
        public string? SourcePath { get; }

        /// <summary>A file the plugin made, at <paramref name="sourcePath"/>.</summary>
        public static KeptFile Made(DerivedKind kind, string plugin, int version, string mediaType, string sourcePath)
        {
            return new KeptFile(kind, new DerivedEntry(plugin, version, new DerivedOutcome.Made(mediaType)), sourcePath);
        }

        /// <summary>That the plugin could not make one of the contents.</summary>
        public static KeptFile Failed(DerivedKind kind, string plugin, int version)
        {
            return new KeptFile(kind, new DerivedEntry(plugin, version, new DerivedOutcome.Failed()), null);
        }

        /// <summary>This one made, not kept for want of room for its <paramref name="bytes"/>.</summary>
        public KeptFile WithNoRoom(long bytes)
        {
            return new KeptFile(Kind, Entry with { Outcome = new DerivedOutcome.NoRoom(bytes) }, null);
        }
    }
}
