namespace ByteShelf.Server.Jobs.Models
{
    /// <summary>
    /// Canonical names for the background jobs whose runs are recorded in <c>job_run</c>, as
    /// <c>domain.job</c>.
    /// </summary>
    public static class JobNames
    {
        /// <summary>Removes abandoned uploads, purges the expired trash and unreferenced objects.</summary>
        public const string StorageMaintenance = "storage.maintenance";

        /// <summary>Deletes expired refresh and password-reset tokens.</summary>
        public const string ExpiredTokenCleanup = "users.expired_token_cleanup";
    }
}
