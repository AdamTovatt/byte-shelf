namespace ByteShelf.Server.Storage.Models
{
    /// <summary>A directory with the size and count of the live files under it, at any depth.</summary>
    public sealed record DirectoryWithTotals(DirectoryRecord Directory, long SizeBytes, int FileCount);
}
