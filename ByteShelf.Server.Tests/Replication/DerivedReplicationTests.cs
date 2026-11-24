using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>A replica makes derived files of its own, and drops them with the plaintext as its primary does.</summary>
    public sealed class DerivedReplicationTests : IDisposable
    {
        private readonly TestDerivedPlugins _plugins = new TestDerivedPlugins();

        [Fact]
        public async Task Sync_AFileFromThePrimary_HasItMadeOnTheReplica()
        {
            await using ReplicationPair pair = await StartAsync();
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "hello");
            await pair.SyncAsync();

            await TestDerivedPlugins.UntilAsync(() => _plugins.Runs.Count == 1);

            using HttpClient client = await pair.SignInOnReplicaAsync();
            Assert.Equal("Preview of hello by test-maker 1", await client.GetStringAsync(Url(file, "preview")));
            Assert.Equal(["hello: Thumbnail, Preview"], _plugins.Runs);
        }

        [Fact]
        public async Task Sync_AFileEncryptedOnThePrimary_RemovesItWithTheSwap()
        {
            await using ReplicationPair pair = await StartAsync();
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "hello");
            await pair.SyncAsync();
            using HttpClient client = await pair.SignInOnReplicaAsync();
            await client.GetStringAsync(Url(file, "thumbnail"));
            Assert.NotEmpty(DerivedOnReplica(pair));

            await pair.SwapOnPrimaryAsync("docs", file, "encrypted");
            await pair.SyncAsync();

            Assert.Empty(DerivedOnReplica(pair));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(file, "thumbnail"))).StatusCode);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Sync_AnAgeFileEncryptedByItsOwnBytes_RemovesItToo(bool byWholeCopy)
        {
            await using ReplicationPair pair = await StartAsync();
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "age-encryption.org/v1\nencrypted");
            await pair.SyncAsync();
            using HttpClient client = await pair.SignInOnReplicaAsync();
            await client.GetStringAsync(Url(file, "thumbnail"));

            ShelfFile swapped = await pair.SwapOnPrimaryAsync("docs", file, "encrypted");
            if (byWholeCopy)
            {
                // Supersedes the swap's entry, so compacting drops that.
                (await pair.Primary.AdminClient.PostAsJsonAsync("/api/shelves/docs/files/move?path=/a.txt", new MoveRequest { ToPath = "/b.txt" })).EnsureSuccessStatusCode();
                await pair.CompactPrimaryAsync();
                await pair.SyncOnceAsync();
                Assert.True((await ReplicationPair.GetStatusAsync(client)).Source!.Copying);
            }

            await pair.SyncAsync();

            Assert.Equal((file.Sha256, true), (swapped.Sha256, swapped.Encrypted));
            Assert.Empty(DerivedOnReplica(pair));
        }

        [Fact]
        public async Task Sync_AFileDecryptedOnThePrimary_HasItMadeOnTheReplica()
        {
            await using ReplicationPair pair = await StartAsync();
            ShelfFile plain = await pair.PutOnPrimaryAsync("docs", "/a.txt", "hello");
            ShelfFile encrypted = await pair.SwapOnPrimaryAsync("docs", plain, "encrypted");
            await pair.SyncAsync();
            Assert.Empty(_plugins.Runs);

            using StringContent content = new StringContent("decrypted");
            (await pair.Primary.AdminClient.PutAsync($"/api/shelves/docs/content/swap?id={encrypted.Id}&sha256={encrypted.Sha256}&encrypted=false", content)).EnsureSuccessStatusCode();
            await pair.SyncAsync();

            await TestDerivedPlugins.UntilAsync(() => _plugins.Runs.Contains("decrypted: Thumbnail, Preview"));
        }

        public void Dispose()
        {
            _plugins.Dispose();
        }

        private async Task<ReplicationPair> StartAsync()
        {
            _plugins.All = [_plugins.Plugin()];
            ReplicationPair pair = await ReplicationPair.StartAsync(configureReplica: _plugins.AddTo);
            await pair.PairAsync();
            await pair.CreateShelfAsync("docs");
            return pair;
        }

        private static string Url(ShelfFile file, string kind)
        {
            return $"/api/shelves/docs/files/derived?id={file.Id}&kind={kind}";
        }

        private static string[] DerivedOnReplica(ReplicationPair pair)
        {
            return Directory.GetFiles(pair.Replica.TestRoot.Root.DerivedPath, "*", SearchOption.AllDirectories);
        }
    }
}
