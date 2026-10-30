namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>The caller can see the shelf but may not do this to it. Answered as 403.</summary>
    public class PermissionDeniedException : Exception
    {
        public PermissionDeniedException(string message)
            : base(message)
        {
        }
    }
}
