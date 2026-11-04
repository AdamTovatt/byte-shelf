using ByteShelfClient;

namespace ByteShelf.Tools
{
    /// <summary>Where the tools get the client they act through: the CLI's login, or the server's caller's key.</summary>
    internal interface IShelfClientSource
    {
        /// <summary>The client to act through for this call.</summary>
        /// <exception cref="UsageException">Nothing says which server or credentials to use.</exception>
        Task<IByteShelfClient> OpenAsync(CancellationToken cancellationToken);
    }
}
