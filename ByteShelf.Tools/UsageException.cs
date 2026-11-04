namespace ByteShelf.Tools
{
    /// <summary>A command or tool call given wrongly, or one that cannot run as things are, such as before signing in.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Only the CLI and the server use this library, and neither lets it out of its own code.")]
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
