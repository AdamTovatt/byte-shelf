using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What is to be kept of one kind.</summary>
    /// <param name="Kind">The kind.</param>
    /// <param name="Entry">What is known of it.</param>
    /// <param name="SourcePath">The derived file, which is moved; null when <paramref name="Entry"/> says making it failed.</param>
    public sealed record KeptFile(DerivedKind Kind, DerivedEntry Entry, string? SourcePath);
}
