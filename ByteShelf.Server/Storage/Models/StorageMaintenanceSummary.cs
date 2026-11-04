namespace ByteShelf.Server.Storage.Models
{
    /// <summary>What one maintenance pass removed.</summary>
    public record StorageMaintenanceSummary(int AbandonedUploads, int StrayStagingEntries, int PurgedFiles, int RemovedObjects);
}
