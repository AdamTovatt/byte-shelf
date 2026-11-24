namespace ByteShelf.Server.Jobs.Models
{
    /// <summary>The background jobs' names, as <c>domain.job</c>, which their log lines carry.</summary>
    public static class JobNames
    {
        /// <summary>Removes abandoned uploads, purges the expired trash and unreferenced objects.</summary>
        public const string StorageMaintenance = "storage.maintenance";

        /// <summary>Deletes expired refresh and password-reset tokens.</summary>
        public const string ExpiredTokenCleanup = "users.expired_token_cleanup";

        /// <summary>Drops old change-log entries a newer one supersedes, and old removals.</summary>
        public const string ChangeLogCompaction = "change_log.compaction";

        /// <summary>Emails the admins when replication has gone wrong for long, and when it recovers.</summary>
        public const string ReplicationAlerts = "replication.alerts";

        /// <summary>Reads what stored files' contents say about them, and drops what was read, by plugins too, from contents no longer stored.</summary>
        public const string MetadataReading = "metadata.reading";

        /// <summary>Has plugins read stored files' contents.</summary>
        public const string PluginMetadataReading = "metadata.plugin_reading";
    }
}
