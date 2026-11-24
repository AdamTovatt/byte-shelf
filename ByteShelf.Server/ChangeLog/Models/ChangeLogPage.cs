namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>A page of entries, oldest first.</summary>
    /// <param name="Entries">The entries.</param>
    /// <param name="HasMore">Whether entries follow the last one.</param>
    public sealed record ChangeLogPage(IReadOnlyList<ChangeLogEntry> Entries, bool HasMore);
}
