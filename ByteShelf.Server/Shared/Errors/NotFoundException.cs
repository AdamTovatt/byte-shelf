namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>The thing asked for does not exist, or the caller cannot see it. Answered as 404.</summary>
    public class NotFoundException : Exception
    {
        public NotFoundException(string message)
            : base(message)
        {
        }
    }
}
