namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>What was asked for is not ready yet and will be. Answered as 503 with <c>Retry-After</c>.</summary>
    public class RetryLaterException : Exception
    {
        public RetryLaterException(string message, TimeSpan retryAfter)
            : base(message)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan RetryAfter { get; }
    }
}
