namespace ByteShelfCommon
{
    /// <summary>The server's limits on uploads, so clients can size their requests to fit.</summary>
    public static class UploadLimits
    {
        /// <summary>The largest chunk one request of a resumable upload may carry: 64 MiB.</summary>
        public const long MaxChunkBytes = 64L * 1024 * 1024;
    }
}
