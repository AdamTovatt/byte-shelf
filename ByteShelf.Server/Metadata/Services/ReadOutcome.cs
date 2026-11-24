namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>How a reader's read of one object ended.</summary>
    /// <param name="Record">Records what it found, or nothing for a read that failed.</param>
    /// <param name="Failed">Whether it threw or took too long.</param>
    internal sealed record ReadOutcome(RecordReading Record, bool Failed);
}
