using ByteShelf.Server.Shared.Errors;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// What the server has of contents beside the files that hold them (<see cref="IContentsTraces"/>) could not be
    /// removed, so what would have released them was refused. A caller may ask again: what held them, a plugin's
    /// process that has yet to end, usually lets go.
    /// </summary>
    public sealed class TracesNotRemovedException : RetryLaterException
    {
        /// <summary>How long a caller is asked to wait before asking again.</summary>
        public static readonly TimeSpan WaitBeforeAskingAgain = TimeSpan.FromSeconds(30);

        public TracesNotRemovedException(string sha256, Exception innerException)
            : base($"What the server has of the contents {sha256} could not be removed yet, so nothing was changed; ask again later.", WaitBeforeAskingAgain, innerException)
        {
            Sha256 = sha256;
        }

        /// <summary>The contents whose traces are left.</summary>
        public string Sha256 { get; }
    }
}
