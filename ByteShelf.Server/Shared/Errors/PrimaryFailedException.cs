namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>
    /// A replica's primary failed it: it could not be reached, or what it answered could not be used (a
    /// page that is not a feed, bytes that do not match their hash). Answered as 502.
    /// </summary>
    public class PrimaryFailedException : Exception
    {
        public PrimaryFailedException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
