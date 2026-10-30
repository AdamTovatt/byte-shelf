namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>The request clashes with what is there: a name already taken, an upload not yet complete. Answered as 409.</summary>
    public class ConflictException : Exception
    {
        public ConflictException(string message)
            : base(message)
        {
        }
    }
}
