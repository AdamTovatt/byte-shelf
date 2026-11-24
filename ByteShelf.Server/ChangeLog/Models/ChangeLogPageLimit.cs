namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>How much one page of entries holds; always at least one entry, whatever its size.</summary>
    /// <param name="Entries">The most entries.</param>
    /// <param name="PayloadBytes">The most bytes of UTF-8 their payloads hold together.</param>
    public sealed record ChangeLogPageLimit(int Entries, long PayloadBytes);
}
