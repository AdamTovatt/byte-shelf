using ByteShelfClient;
using ByteShelfClient.InMemory;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>The client contract against the in-memory client, which must behave as the server does.</summary>
    public class InMemoryClientContractTests : ClientContractTests
    {
        protected override Task<IByteShelfClient> CreateAdminClientAsync()
        {
            return Task.FromResult<IByteShelfClient>(new InMemoryByteShelfClient(password: AdminPassword));
        }
    }
}
