namespace ByteShelf.Server.Jobs.Models
{
    /// <summary>
    /// Well-known <c>error_message</c> values for <c>job_run</c> rows that did not fail in the work's
    /// own sense: the host was shutting down, or a previous process died before completing the run.
    /// </summary>
    public static class JobRunSentinels
    {
        /// <summary>Stamped by the recorder when graceful shutdown cancels the work.</summary>
        public const string ShutdownErrorMessage = "Cancelled at shutdown.";

        /// <summary>Stamped by the startup sweeper on rows orphaned by a non-cooperative termination of a previous process.</summary>
        public const string UncleanShutdownErrorMessage = "Process terminated unexpectedly.";
    }
}
