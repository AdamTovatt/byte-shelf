namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>The shelf's quota cannot hold the bytes. Answered as 507.</summary>
    public class QuotaExceededException : Exception
    {
        public QuotaExceededException(string message)
            : base(message)
        {
        }
    }
}
