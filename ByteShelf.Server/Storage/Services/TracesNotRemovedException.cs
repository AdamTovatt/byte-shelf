namespace ByteShelf.Server.Storage.Services
{
    /// <summary>What the server has of contents beside the files that hold them (<see cref="IContentsTraces"/>) could not be removed.</summary>
    public sealed class TracesNotRemovedException : Exception
    {
        public TracesNotRemovedException(string sha256, Exception innerException)
            : base($"What the server has of the contents {sha256} could not be removed.", innerException)
        {
        }
    }
}
