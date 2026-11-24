namespace ByteShelf.Server.Derived.Services
{
    /// <summary>A maker could not run, which says nothing of the contents.</summary>
    internal sealed class MakerUnavailableException : Exception
    {
        public MakerUnavailableException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
