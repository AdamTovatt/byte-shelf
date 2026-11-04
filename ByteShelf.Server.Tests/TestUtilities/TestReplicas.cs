using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Makes a server's database a replica's, without a primary to pair with, for what a replica refuses or says.</summary>
    public static class TestReplicas
    {
        /// <summary>The URL of the primary it follows.</summary>
        public const string PrimaryUrl = "http://primary.invalid/";

        /// <param name="services">The server's services, or a scope of them.</param>
        /// <param name="needsCopy">Whether it is still waiting for its primary's first copy.</param>
        /// <param name="pairedAt">When it was paired; <see cref="TestInstants.Now"/> when not given.</param>
        public static async Task MarkAsReplicaAsync(IServiceProvider services, bool needsCopy = false, DateTimeOffset? pairedAt = null)
        {
            using IServiceScope scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IReplicationSourceRepository>().TryInsertAsync(new ReplicationSource
            {
                PrimaryUrl = PrimaryUrl,
                PrimaryInstanceId = Guid.NewGuid().ToString("D"),
                Credential = "replica-credential",
                PairedAt = pairedAt ?? TestInstants.Now,
                AppliedSequence = 0,
                NeedsCopy = needsCopy,
            });
        }
    }
}
