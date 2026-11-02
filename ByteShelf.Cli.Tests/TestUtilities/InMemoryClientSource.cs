using ByteShelf.Cli.Connection;
using ByteShelfClient;
using ByteShelfClient.InMemory;

namespace ByteShelf.Cli.Tests.TestUtilities
{
    /// <summary>Commands over an in-memory client, signed in as its admin; signing in and out is the server tests' part.</summary>
    internal sealed class InMemoryClientSource : IClientSource
    {
        /// <param name="timeProvider">The client's clock; the real one unless given.</param>
        public InMemoryClientSource(TimeProvider? timeProvider = null)
        {
            Client = new InMemoryByteShelfClient(timeProvider: timeProvider);
        }

        public InMemoryByteShelfClient Client { get; }

        public Task<IByteShelfClient> OpenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IByteShelfClient>(Client);
        }

        public Task SignInAsync(string server, string email, string password, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UseKeyAsync(string server, string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public string? Describe()
        {
            return "memory as admin";
        }
    }
}
