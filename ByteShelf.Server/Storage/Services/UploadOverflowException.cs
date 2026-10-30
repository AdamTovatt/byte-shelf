namespace ByteShelf.Server.Storage.Services
{
    /// <summary>An upload sent more bytes than it declared, and so more than it reserved.</summary>
    public class UploadOverflowException : ArgumentException
    {
        public UploadOverflowException(long declaredBytes)
            : base($"The upload sent more than the {declaredBytes} bytes it declared.")
        {
        }
    }
}
