namespace ByteShelf.Server.Derived.Models
{
    /// <summary>A <see cref="ByteShelfCommon.DerivedKind.Full"/> file kept of contents, and when it was last used.</summary>
    public sealed record FullFile(string Sha256, DateTime LastUsedUtc);
}
