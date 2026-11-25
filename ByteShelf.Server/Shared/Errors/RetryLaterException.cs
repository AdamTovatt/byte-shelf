namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>What was asked for cannot be done yet and may be soon. Answered as 503 with <c>Retry-After</c>.</summary>
    public class RetryLaterException : Exception
    {
        public RetryLaterException(string message, TimeSpan retryAfter)
            : base(message)
        {
            RetryAfter = retryAfter;
        }

        public RetryLaterException(string message, TimeSpan retryAfter, Exception innerException)
            : base(message, innerException)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan RetryAfter { get; }
    }
}
