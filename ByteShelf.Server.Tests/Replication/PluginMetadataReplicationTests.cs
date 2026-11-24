using System.Net.Http.Json;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>
    /// What plugins keep about files and their contents on a replica: the documents arrive whatever the
    /// order their files come in, and nothing read from plaintext outlives it there. Neither server has the
    /// plugin installed, which changes nothing.
    /// </summary>
    public sealed class PluginMetadataReplicationTests : IAsyncLifetime
    {
        private const string Rating = "{\"rating\":4}";

        private static readonly byte[] SecretPdf = TestMedia.Pdf(pages: 2, "Q3 secret");

        private readonly List<ReplicationPair> _pairs = new List<ReplicationPair>();

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            foreach (ReplicationPair pair in _pairs)
            {
                await pair.DisposeAsync();
            }
        }

        [Fact]
        public async Task Sync_DocumentsWrittenOnThePrimary_ReachTheReplicaAndAreRemovedThere()
        {
            ReplicationPair pair = await StartAsync();
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            await pair.PairAsync();
            await pair.SyncAsync();

            await pair.Primary.Services.SetFileDocumentAsync(file.Id, "{}");
            await pair.Primary.Services.SetFileDocumentAsync(file.Id, Rating);
            Assert.True(await pair.Primary.Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":10}"));
            await pair.SyncAsync();
            Assert.Equal((Rating, "{\"width\":10}"), await DocumentsOnReplicaAsync(pair, file));

            await pair.Primary.Services.RemoveFileDocumentAsync(file.Id);
            await pair.SyncAsync();
            Assert.Equal((null, "{\"width\":10}"), await DocumentsOnReplicaAsync(pair, file));
        }

        [Fact]
        public async Task Pair_DocumentsWrittenBeforePairing_ComeWithTheWholeCopy()
        {
            ReplicationPair pair = await StartAsync();
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            await pair.Primary.Services.SetFileDocumentAsync(file.Id, Rating);
            await pair.Primary.Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":10}");

            await pair.PairAsync();
            await pair.SyncAsync();

            Assert.Equal((Rating, "{\"width\":10}"), await DocumentsOnReplicaAsync(pair, file));
        }

        [Fact]
        public async Task Pair_FileMovedAfterItsDocument_KeepsTheDocumentThatComesBeforeItInTheCopy()
        {
            // A page each, so the document is applied a page before its file.
            ReplicationPair pair = await StartAsync(pageSize: 1);
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            FilePluginMetadata document = await pair.Primary.Services.SetFileDocumentAsync(file.Id, Rating);
            await MoveOnPrimaryAsync(pair, "/a.txt", "/b.txt");
            await CompactAsync(pair.Primary);
            List<Guid> copied = (await pair.Primary.Services.GetLatestStatesAsync()).Select(entry => entry.EntityId).ToList();
            Assert.True(copied.IndexOf(document.Id) < copied.IndexOf(file.Id), "The copy does not give the document before its file, so this proves nothing.");

            await pair.PairAsync();
            await pair.SyncAsync();

            using HttpClient replica = await pair.SignInOnReplicaAsync();
            Assert.Equal(file.Id, (await replica.GetFromJsonAsync<ShelfFile>("/api/shelves/docs/files?path=/b.txt"))!.Id);
            Assert.Equal(Rating, await pair.Replica.Services.GetFileDocumentAsync(file.Id));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        public async Task Sync_FileSwappedBeforeItsContentsWereFetched_BringsItsDocumentWithTheSwap(int pageSize)
        {
            ReplicationPair pair = await StartAsync(pageSize);
            await pair.PairAsync();
            await pair.SyncAsync();

            // The file's entries up to the swap are passed over, as the primary no longer holds those contents.
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            await pair.Primary.Services.SetFileDocumentAsync(file.Id, Rating);
            await pair.SwapOnPrimaryAsync("docs", file, "encrypted");
            await pair.SyncAsync();

            Assert.Equal(Rating, await pair.Replica.Services.GetFileDocumentAsync(file.Id));
        }

        [Fact]
        public async Task Pair_FileSwappedWhileItsContentsAreCopied_BringsItsDocumentAfterTheCopy()
        {
            ShelfFile? file = null;
            ReplicationPair? pair = null;
            bool swapped = false;
            pair = await StartAsync(wrapPrimary: primary => new BeforeObjectHandler(primary, async () =>
            {
                if (!swapped)
                {
                    swapped = true;
                    await pair!.SwapOnPrimaryAsync("docs", file!, "encrypted");
                }
            }));
            file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            await pair.Primary.Services.SetFileDocumentAsync(file.Id, Rating);

            // The copy's last page is read when the swap comes, so the copy has the file as it was, whose
            // contents are gone: neither it nor its document is written until the log is followed.
            await pair.PairAsync();
            await pair.SyncOnceAsync();
            Assert.True(swapped);
            Assert.Null(await pair.Replica.Services.GetFileDocumentAsync(file.Id));

            await pair.SyncAsync();
            Assert.Equal(Rating, await pair.Replica.Services.GetFileDocumentAsync(file.Id));
        }

        [Fact]
        public async Task Pair_DocumentOfAPurgedFile_IsPassedOver()
        {
            ReplicationPair pair = await StartAsync();
            ShelfFile purged = await pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            ShelfFile kept = await pair.PutOnPrimaryAsync("docs", "/b.txt", "b");
            await pair.Primary.Services.SetFileDocumentAsync(purged.Id, Rating);
            await pair.Primary.Services.SetFileDocumentAsync(kept.Id, Rating);
            await pair.PurgeOnPrimaryAsync("docs", "/a.txt");

            await pair.PairAsync();
            await pair.SyncAsync();

            Assert.Null(await pair.Replica.Services.GetFileDocumentAsync(purged.Id));
            Assert.Equal(Rating, await pair.Replica.Services.GetFileDocumentAsync(kept.Id));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Sync_FilePurgedOnThePrimary_KeepsItsDocumentWhileTheReplicaKeepsTheFile(bool compacted)
        {
            ReplicationPair pair = await StartAsync();
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            FilePluginMetadata document = await pair.Primary.Services.SetFileDocumentAsync(file.Id, Rating);
            await pair.PairAsync();
            await pair.SyncAsync();

            await pair.PurgeOnPrimaryAsync("docs", "/a.txt");
            if (compacted)
            {
                await CompactAsync(pair.Primary);
                Assert.DoesNotContain(await pair.Primary.Services.GetLatestStatesAsync(), entry => entry.EntityId == document.Id);
            }

            await RequireCopyAsync(pair.Replica);
            await pair.SyncAsync();

            using IServiceScope scope = pair.Replica.Services.CreateScope();
            Assert.NotNull((await scope.ServiceProvider.GetRequiredService<IFileRepository>().GetByIdAsync(file.Id))!.DeletedAt);
            Assert.Equal(Rating, await pair.Replica.Services.GetFileDocumentAsync(file.Id));
        }

        [Fact]
        public async Task Sync_DocumentRemovedWhileTheReplicaWasBehind_IsRemovedByTheCopy()
        {
            ReplicationPair pair = await StartAsync();
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            await pair.Primary.Services.SetFileDocumentAsync(file.Id, Rating);
            await pair.PairAsync();
            await pair.SyncAsync();

            await pair.Primary.Services.RemoveFileDocumentAsync(file.Id);
            await CompactAsync(pair.Primary);
            await RequireCopyAsync(pair.Replica);
            await pair.SyncAsync();

            Assert.Null(await pair.Replica.Services.GetFileDocumentAsync(file.Id));
        }

        [Fact]
        public async Task Sync_FileEncryptedOnThePrimary_DropsWhatWasReadOnBothServersAndInBothLogs()
        {
            ReplicationPair pair = await StartAsync();
            ShelfFile file = await PutReadPdfAsync(pair);
            await pair.PairAsync();
            await pair.SyncAsync();
            await AssertReadingsAsync(pair.Replica, file, readings: 2);

            await pair.SwapOnPrimaryAsync("docs", file, "encrypted");
            await pair.SyncAsync();

            await AssertNothingOutlivesAsync(pair.Primary, file);
            await AssertNothingOutlivesAsync(pair.Replica, file);
        }

        [Fact]
        public async Task Sync_ReplicaStoppedAnywhereAroundASwap_HoldsNoReadingOfContentsItsFileDoesNotHold()
        {
            ReplicationPair pair = await StartAsync(pageSize: 1);
            ShelfFile file = await PutReadPdfAsync(pair);
            await pair.PairAsync();
            await pair.SyncAsync();
            await pair.SwapOnPrimaryAsync("docs", file, "encrypted");

            // One entry a step: the two removals of what was read, then the swap.
            await pair.SyncOnceAsync();
            await AssertReadingsAsync(pair.Replica, file, readings: 1);
            await pair.SyncOnceAsync();
            Assert.Equal(file.Sha256, (await FileOnReplicaAsync(pair, file))!.Sha256);
            await AssertNothingOutlivesAsync(pair.Replica, file);
            using HttpClient replica = await pair.SignInOnReplicaAsync();
            Assert.Null((await replica.GetFromJsonAsync<FileMetadata>($"/api/shelves/docs/files/metadata?id={file.Id}"))!.Title);

            await pair.SyncOnceAsync();
            Assert.True((await FileOnReplicaAsync(pair, file))!.Encrypted);
            await AssertNothingOutlivesAsync(pair.Replica, file);
        }

        [Fact]
        public async Task Sync_FileSwappedBeforeItsContentsWereFetched_LeavesNoReadingOnceTheSwapIsApplied()
        {
            ShelfFile? file = null;
            ShelfFile? other = null;
            ReplicationPair? pair = null;
            bool swapped = false;
            pair = await StartAsync(pageSize: 3, wrapPrimary: primary => new BeforeObjectHandler(primary, async () =>
            {
                if (file != null && !swapped)
                {
                    // The swap comes last on its page, so any entry after it is on the next.
                    swapped = true;
                    await pair!.Primary.Services.SetFileDocumentAsync(other!.Id, "{}");
                    await pair.Primary.Services.SetFileDocumentAsync(other.Id, Rating);
                    await pair.SwapOnPrimaryAsync("docs", file, "encrypted");
                }
            }));
            other = await pair.PutOnPrimaryAsync("docs", "/other.txt", "other");
            await pair.PairAsync();
            await pair.SyncAsync();
            file = await PutReadPdfAsync(pair);

            // A page of the file and its readings, which the swap blanks on the primary once it is sent.
            await pair.SyncOnceAsync();
            Assert.True(swapped);
            Assert.Null(await FileOnReplicaAsync(pair, file));
            await AssertReadingsAsync(pair.Replica, file, readings: 2);

            for (int step = 0; step < 10 && await FileOnReplicaAsync(pair, file) == null; step++)
            {
                await pair.SyncOnceAsync();
            }

            Assert.True((await FileOnReplicaAsync(pair, file))!.Encrypted);
            await AssertNothingOutlivesAsync(pair.Replica, file);
        }

        [Fact]
        public async Task Sync_FileSwappedWhileAPlainCopyComesLater_KeepsWhatWasReadForTheCopy()
        {
            ReplicationPair pair = await StartAsync(pageSize: 1);
            ShelfFile file = await PutReadPdfAsync(pair);
            ShelfFile copy = await pair.Primary.PutAsAdminAsync("docs", "/copy.pdf", SecretPdf, "application/pdf");
            Assert.Equal(file.Sha256, copy.Sha256);
            await pair.SwapOnPrimaryAsync("docs", file, "encrypted");

            await pair.PairAsync();
            await pair.SyncAsync();

            await AssertReadingsAsync(pair.Replica, copy, readings: 2);
        }

        [Fact]
        public async Task Sync_ContentsReadAgainByAnotherVersion_ReplacesTheReadingOnTheReplica()
        {
            ReplicationPair pair = await StartAsync();
            ShelfFile file = await pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            await pair.Primary.Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":10}");
            await pair.PairAsync();
            await pair.SyncAsync();

            Assert.True(await pair.Primary.Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":20}", version: 2));
            await pair.SyncAsync();

            Assert.Equal("{\"width\":20}", await pair.Replica.Services.GetContentDocumentAsync(file.Sha256));
        }

        [Fact]
        public async Task Sync_ReadingDroppedWithItsPurgedFile_IsDroppedOnTheReplicaWithoutACopy()
        {
            ReplicationPair pair = await StartAsync();
            ShelfFile file = await PutReadPdfAsync(pair);
            await pair.PairAsync();
            await pair.SyncAsync();

            await pair.PurgeOnPrimaryAsync("docs", "/a.pdf");
            await pair.Primary.Services.ReadMetadataAsync();
            await pair.SyncAsync();

            await AssertNothingOutlivesAsync(pair.Replica, file);
        }

        [Fact]
        public async Task Sync_ReplicaBehindASwap_DropsWhatWasReadWithTheCopy()
        {
            ReplicationPair pair = await StartAsync();
            ShelfFile file = await PutReadPdfAsync(pair);
            await pair.PairAsync();
            await pair.SyncAsync();

            await pair.SwapOnPrimaryAsync("docs", file, "encrypted");
            await CompactAsync(pair.Primary);
            await RequireCopyAsync(pair.Replica);
            await pair.SyncAsync();

            await AssertNothingOutlivesAsync(pair.Replica, file);
        }

        private async Task<ReplicationPair> StartAsync(int? pageSize = null, Func<HttpMessageHandler, HttpMessageHandler>? wrapPrimary = null)
        {
            ReplicationPair pair = await ReplicationPair.StartAsync(wrapPrimary, pageSize: pageSize);
            _pairs.Add(pair);
            await pair.CreateShelfAsync("docs");
            return pair;
        }

        /// <summary>A PDF titled "Q3 secret" on the primary, read by the server's own reader and by a plugin's.</summary>
        private static async Task<ShelfFile> PutReadPdfAsync(ReplicationPair pair)
        {
            ShelfFile file = await pair.Primary.PutAsAdminAsync("docs", "/a.pdf", SecretPdf, "application/pdf");
            await pair.Primary.Services.ReadMetadataAsync();
            Assert.True(await pair.Primary.Services.RecordContentDocumentAsync(file.Sha256, "{\"text\":\"Q3 secret\"}"));
            await AssertReadingsAsync(pair.Primary, file, readings: 2);
            return file;
        }

        private static async Task<FileRecord?> FileOnReplicaAsync(ReplicationPair pair, ShelfFile file)
        {
            using IServiceScope scope = pair.Replica.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IFileRepository>().GetByIdAsync(file.Id);
        }

        private static async Task<(string? File, string? Content)> DocumentsOnReplicaAsync(ReplicationPair pair, ShelfFile file)
        {
            return (await pair.Replica.Services.GetFileDocumentAsync(file.Id), await pair.Replica.Services.GetContentDocumentAsync(file.Sha256));
        }

        private static async Task AssertNothingOutlivesAsync(ServerTestHost host, ShelfFile file)
        {
            await AssertReadingsAsync(host, file, readings: 0);

            using IServiceScope scope = host.Services.CreateScope();
            await using IDbSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionAsync();
            Assert.Equal(0, await TestQueries.CountAsync(session, "change_log WHERE instr(payload, 'Q3 secret') > 0"));
            Assert.Equal(0, await TestQueries.CountAsync(session, "replication_copy WHERE instr(payload, 'Q3 secret') > 0"));
        }

        private static async Task AssertReadingsAsync(ServerTestHost host, ShelfFile file, int readings)
        {
            using IServiceScope scope = host.Services.CreateScope();
            await using IDbSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionAsync();
            long own = await TestQueries.CountAsync(session, $"object_metadata WHERE sha256 = '{file.Sha256}'");
            long plugins = await TestQueries.CountAsync(session, $"content_plugin_metadata WHERE sha256 = '{file.Sha256}'");
            Assert.Equal(readings, own + plugins);
        }

        private static async Task MoveOnPrimaryAsync(ReplicationPair pair, string path, string toPath)
        {
            (await pair.Primary.AdminClient.PostAsJsonAsync($"/api/shelves/docs/files/move?path={path}", new MoveRequest { ToPath = toPath })).EnsureSuccessStatusCode();
        }

        /// <summary>Compacts a server's log as if every entry were older than the retention; the hosts run on the real clock.</summary>
        private static async Task CompactAsync(ServerTestHost host)
        {
            using IServiceScope scope = host.Services.CreateScope();
            await using IDbTransactionSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IChangeLogRepository>().CompactAsync(DateTimeOffset.UtcNow.AddYears(1), session);
            await session.CommitAsync();
        }

        private static async Task RequireCopyAsync(ServerTestHost replica)
        {
            using IServiceScope scope = replica.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IReplicationSourceRepository>().RequireCopyAsync();
        }

        /// <summary>Does something on the primary right before it is asked for a file's bytes.</summary>
        private sealed class BeforeObjectHandler : DelegatingHandler
        {
            private readonly Func<Task> _before;

            public BeforeObjectHandler(HttpMessageHandler primary, Func<Task> before)
                : base(primary)
            {
                _before = before;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/object", StringComparison.Ordinal))
                {
                    await _before();
                }

                return await base.SendAsync(request, cancellationToken);
            }
        }
    }
}
