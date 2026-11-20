using System.Reflection;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>The server's release, the same on both clients: the version without the commit, and a steady deploy id.</summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task GetServerVersionAsync_AnswersTheReleaseWithoutTheCommitAndASteadyDeployId()
        {
            ServerVersion first = await Client.GetServerVersionAsync();
            ServerVersion second = await Client.GetServerVersionAsync();

            // Assembly versions carry the csproj's Version and never the commit, so they say what the
            // release must be without asking the code under test.
            Assert.Equal(ReleaseAssembly.GetName().Version!.ToString(3), first.Version);
            Assert.Matches("^[0-9a-f]{32}$", first.VersionId);
            Assert.Equal(first.VersionId, second.VersionId);
        }

        /// <summary>The assembly whose release <see cref="Client"/> answers: the server's, or the in-memory client's own package.</summary>
        protected abstract Assembly ReleaseAssembly { get; }
    }
}
