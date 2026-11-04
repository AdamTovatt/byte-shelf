namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>The request needs a <c>Content-Length</c> it did not send. Answered as 411.</summary>
    public class LengthRequiredException : Exception
    {
        public LengthRequiredException(string message)
            : base(message)
        {
        }
    }
}
