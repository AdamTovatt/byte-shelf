using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Makes a server's database a replica's, without a primary to pair with, for what a replica refuses or says.</summary>
    public static class TestReplicas
    {
        /// <param name="services">The server's services, or a scope of them.</param>
        /// <param name="needsCopy">Whether it is still waiting for its primary's first copy.</param>
        public static async Task MarkAsReplicaAsync(IServiceProvider services, bool needsCopy = false)
        {
            using IServiceScope scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IReplicationSourceRepository>().TryInsertAsync(new ReplicationSource
            {
                PrimaryUrl = "http://primary.invalid/",
                PrimaryInstanceId = Guid.NewGuid().ToString("D"),
                Credential = "replica-credential",
                PairedAt = DateTimeOffset.UtcNow,
                AppliedSequence = 0,
                NeedsCopy = needsCopy,
            });
        }
    }
}
