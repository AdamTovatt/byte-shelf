namespace ByteShelf.Server.Backup.Models
{
    /// <summary>What a backup holds, for the line the backup command prints.</summary>
    public sealed class BackupSummary
    {
        public required int ObjectCount { get; init; }

        public required long ObjectBytes { get; init; }

        /// <summary>Files dropped from the snapshot because the server purged them before their objects were copied.</summary>
        public required int FilesPurgedMeanwhile { get; init; }
    }
}
