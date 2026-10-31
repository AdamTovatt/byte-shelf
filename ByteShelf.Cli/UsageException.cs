namespace ByteShelf.Cli
{
    /// <summary>A command given wrongly, or one that cannot run as things are, such as before signing in.</summary>
    internal sealed class UsageException : Exception
    {
        public UsageException(string message)
            : base(message)
        {
        }

        public UsageException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public UsageException()
        {
        }
    }
}
