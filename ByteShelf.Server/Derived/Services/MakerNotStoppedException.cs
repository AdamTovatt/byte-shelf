namespace ByteShelf.Server.Derived.Services
{
    /// <summary>A maker's process is still there though it was killed, or what it wrote is.</summary>
    internal sealed class MakerNotStoppedException : Exception
    {
        public MakerNotStoppedException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
