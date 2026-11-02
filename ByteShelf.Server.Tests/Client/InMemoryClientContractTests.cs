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

        protected override string PrimaryUrl => ((InMemoryByteShelfClient)Client).Address;

        protected override Task<IByteShelfClient> CreateServerToFollowAsync()
        {
            return Task.FromResult<IByteShelfClient>(new InMemoryByteShelfClient());
        }
    }
}
