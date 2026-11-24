using System.Text;
using System.Text.Json.Nodes;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>What plugins' readers find in files' contents: read by the same job as the server's own, and never outliving the plaintext.</summary>
    public class PluginReadingTests : ServiceTestBase
    {
        // Written out rather than taken from the code, so a change of either is noticed.
        private static readonly TimeSpan PrepareLimit = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan StopLimit = TimeSpan.FromSeconds(5);

        private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(30);

        /// <summary>A pass in a scope of its own, as the worker runs it.</summary>
        private Task<MetadataReadingSummary> RunAsync()
        {
            return Services.ReadMetadataAsync();
        }

        [Fact]
        public async Task RunAsync_APluginsReader_RecordsWhatItFindsAndLogsIt()
        {
            Install(TestDocumentReader.OfText());
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            long before = await ChangeLog.GetLatestSequenceAsync();

            MetadataReadingSummary summary = await RunAsync();

            Assert.Equal(new MetadataReadingSummary(OnReplica: false, Read: 1, Failed: 0, Removed: 0), summary);
            ContentPluginMetadata row = (await Rows.GetAsync(TestPluginMetadata.Plugin, file.Sha256))!;
            Assert.Equal(("{\"text\":\"hello\"}", 1, TestInstants.Now), (row.Document?.Json, row.ReaderVersion, row.ReadAt));
            ChangeLogEntry logged = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.ContentPluginMetadata, row.Id, ChangeAction.Added), (logged.EntityType, logged.EntityId, logged.Action));
            Assert.Equal(row.Document, ChangePayload.Deserialize<ContentPluginMetadata>(logged.Payload!).Document);
        }

        [Fact]
        public async Task RunAsync_TheServersOwnReader_RunsFirst()
        {
            bool ownRowWasThere = false;
            ShelfFile? photo = null;
            Install(new TestDocumentReader(
                async (_, _) =>
                {
                    using IServiceScope scope = Services.CreateScope();
                    ownRowWasThere = await scope.ServiceProvider.GetRequiredService<IObjectMetadataRepository>().GetBySha256Async(photo!.Sha256) != null;
                    return null;
                },
                mediaTypes: "image/*"));
            Shelf shelf = await CreateShelfAsync();
            photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");

            MetadataReadingSummary summary = await RunAsync();

            Assert.True(ownRowWasThere);
            Assert.Equal(2, summary.Read);
        }

        [Fact]
        public async Task RunAsync_FilesOfOtherTypes_AreNotRead()
        {
            Install(new TestDocumentReader((_, _) => Task.FromResult<JsonObject?>(null), mediaTypes: ["application/x-drawing", "audio/*"]));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile text = await StoreTextAsync(shelf, "/a.txt", "text");
            ShelfFile drawing = await StoreAsync(shelf, "/a.drawing", [1], "application/x-drawing");
            ShelfFile sound = await StoreAsync(shelf, "/a.wav", [2], "audio/wav");

            await RunAsync();

            Assert.Null(await Rows.GetAsync(TestPluginMetadata.Plugin, text.Sha256));
            Assert.NotNull(await Rows.GetAsync(TestPluginMetadata.Plugin, drawing.Sha256));
            Assert.NotNull(await Rows.GetAsync(TestPluginMetadata.Plugin, sound.Sha256));
        }

        [Fact]
        public async Task RunAsync_NothingFound_IsNotReadAgainUntilTheVersionChanges()
        {
            TestDocumentReader first = Install(new TestDocumentReader((_, _) => Task.FromResult<JsonObject?>(null)));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await RunAsync();
            await RunAsync();
            Assert.Equal(1, first.Reads);
            Assert.Null((await Rows.GetAsync(TestPluginMetadata.Plugin, file.Sha256))!.Document);

            Install(TestDocumentReader.OfText(version: 2));
            await RunAsync();

            ContentPluginMetadata row = (await Rows.GetAsync(TestPluginMetadata.Plugin, file.Sha256))!;
            Assert.Equal((2, "{\"text\":\"hello\"}"), (row.ReaderVersion, row.Document?.Json));
            Assert.Equal(ChangeAction.Updated, (await ChangeLog.GetAfterAsync(0, 100)).Last(entry => entry.EntityId == row.Id).Action);
        }

        [Fact]
        public async Task RunAsync_AReadThatThrows_IsRecordedAsNothingOnceAndStopsNoOther()
        {
            TestDocumentReader reader = Install(new TestDocumentReader(async (contents, cancellationToken) =>
            {
                string text = await TestDocumentReader.ReadTextAsync(contents, cancellationToken);
                return text == "bad" ? throw new InvalidOperationException("broken") : new JsonObject { ["text"] = text };
            }));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile bad = await StoreTextAsync(shelf, "/bad.txt", "bad");
            ShelfFile good = await StoreTextAsync(shelf, "/good.txt", "good");

            MetadataReadingSummary first = await RunAsync();
            MetadataReadingSummary second = await RunAsync();

            Assert.Equal((2, 1, 0), (first.Read, first.Failed, second.Read));
            Assert.Equal(2, reader.Reads);
            Assert.Null((await Rows.GetAsync(TestPluginMetadata.Plugin, bad.Sha256))!.Document);
            Assert.NotNull((await Rows.GetAsync(TestPluginMetadata.Plugin, good.Sha256))!.Document);
        }

        [Fact]
        public async Task RunAsync_MoreThanADocumentMayHold_CountsAsFailed()
        {
            Install(new TestDocumentReader((_, _) => Task.FromResult<JsonObject?>(new JsonObject { ["t"] = new string('x', PluginDocument.MaxBytes) })));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");

            MetadataReadingSummary summary = await RunAsync();

            Assert.Equal((1, 1), (summary.Read, summary.Failed));
            Assert.Null((await Rows.GetAsync(TestPluginMetadata.Plugin, file.Sha256))!.Document);
        }

        [Fact]
        public async Task RunAsync_AReadOverItsTimeThatStopsWhenCancelled_IsRecordedAsNothingAndTheReaderGoesOn()
        {
            // The first read, so the clock's one timer is that read's: an ended read's may linger a moment.
            TaskCompletionSource started = NewSignal();
            string? slow = null;
            TestDocumentReader reader = Install(new TestDocumentReader(async (contents, cancellationToken) =>
            {
                string text = await TestDocumentReader.ReadTextAsync(contents, cancellationToken);
                if (slow == null)
                {
                    slow = text;
                    started.SetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                return new JsonObject { ["read"] = true };
            }));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile a = await StoreTextAsync(shelf, "/a.txt", "a");
            ShelfFile b = await StoreTextAsync(shelf, "/b.txt", "b");

            Task<MetadataReadingSummary> run = RunAsync();
            await started.Task.WaitAsync(RealTimeLimit);
            await Clock.WaitForTimerAsync(ReadLimit);
            Clock.Advance(ReadLimit);
            MetadataReadingSummary summary = await run.WaitAsync(RealTimeLimit);

            Assert.Equal((2, 1), (summary.Read, summary.Failed));
            Assert.Equal(2, reader.Reads);
            foreach ((string text, ShelfFile file) in new[] { ("a", a), ("b", b) })
            {
                Assert.Equal(text == slow, (await Rows.GetAsync(TestPluginMetadata.Plugin, file.Sha256))!.Document == null);
            }
        }

        [Fact]
        public async Task RunAsync_AReadThatOnlyReadsItsStream_StopsWhenCancelledThoughItLooksAtNoToken()
        {
            Install(new TestDocumentReader((contents, _) =>
            {
                while (contents.Length == 1)
                {
                    contents.Position = 0;
                    contents.ReadByte();
                }

                return Task.FromResult<JsonObject?>(new JsonObject());
            }));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");

            Task<MetadataReadingSummary> run = RunAsync();
            await Clock.WaitForTimerAsync(ReadLimit);
            Clock.Advance(ReadLimit);

            // The clock stands still from here, so it stops of its own and is never left behind: it is run again.
            Assert.Equal(1, (await run.WaitAsync(RealTimeLimit)).Failed);
            Assert.Null((await Rows.GetAsync(TestPluginMetadata.Plugin, file.Sha256))!.Document);
            await StoreTextAsync(shelf, "/b.txt", "another");
            Assert.Equal((1, 0), Counts(await RunAsync()));
        }

        /// <summary>As one whose constructor hangs, or fails: the rest waits for a restart, and the other readers go on.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RunAsync_AReaderThatIsNotMade_ReadsNothingAndLeavesItUnreadAndStopsNoOther(bool hangs)
        {
            Task making = hangs ? new TaskCompletionSource().Task : Task.FromException(new InvalidOperationException("It was told to fail."));
            TestDocumentReader unmade = new TestDocumentReader((_, _) => Task.FromResult<JsonObject?>(new JsonObject())) { Making = making };
            TestDocumentReader other = new TestDocumentReader((_, _) => Task.FromResult<JsonObject?>(new JsonObject()));
            InstallPlugins(Plugin(unmade, "unmade"), Plugin(other, "other"));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");

            Task<MetadataReadingSummary> run = RunAsync();
            if (hangs)
            {
                await Clock.WaitForTimerAsync(PrepareLimit);
                Clock.Advance(PrepareLimit);
            }

            Assert.Equal((1, 0), Counts(await run.WaitAsync(RealTimeLimit)));
            await StoreTextAsync(shelf, "/b.txt", "b");
            Assert.Equal((1, 0), Counts(await RunAsync().WaitAsync(RealTimeLimit)));
            Assert.Equal((0, 2), (unmade.Reads, other.Reads));
            Assert.Null(await Rows.GetAsync("unmade", file.Sha256));
        }

        [Fact]
        public async Task RunAsync_AReaderThatIgnoresBeingCancelled_IsLeftBehindAndNotRunAgain()
        {
            // Bounded, so a break that never leaves it behind fails the test rather than hanging the run.
            using ManualResetEventSlim release = new ManualResetEventSlim();
            TestDocumentReader stuck = new TestDocumentReader((_, _) =>
            {
                release.Wait(RealTimeLimit);
                return Task.FromResult<JsonObject?>(new JsonObject { ["late"] = true });
            });
            TestDocumentReader other = new TestDocumentReader((_, _) => Task.FromResult<JsonObject?>(new JsonObject()));
            InstallPlugins(Plugin(stuck, "stuck"), Plugin(other, "other"));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/a.txt", "a");
            ShelfFile second = await StoreTextAsync(shelf, "/b.txt", "b");

            Task<MetadataReadingSummary> run = RunAsync();
            await Clock.WaitForTimerAsync(ReadLimit);
            Clock.Advance(ReadLimit);
            await Clock.WaitForTimerAsync(StopLimit);
            Clock.Advance(StopLimit);
            MetadataReadingSummary summary = await run.WaitAsync(RealTimeLimit);
            await StoreTextAsync(shelf, "/c.txt", "c");
            MetadataReadingSummary again = await RunAsync();

            // The one it hung on is recorded as nothing, the rest wait for a restart; another reader goes on.
            Assert.Equal((3, 1), Counts(summary));
            Assert.Equal((1, 0), Counts(again));
            Assert.Equal((1, 3), (stuck.Reads, other.Reads));
            string[] hashes = [first.Sha256, second.Sha256];
            string hungOn = hashes.Order(StringComparer.Ordinal).First();
            release.Set();
            await stuck.AllReadsEndedAsync();
            Assert.Null((await Rows.GetAsync("stuck", hungOn))!.Document);
            Assert.Null(await Rows.GetAsync("stuck", hashes.Single(hash => hash != hungOn)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RunAsync_ShuttingDown_IsNoFailureOfTheReader(bool stopsWhenCancelled)
        {
            TaskCompletionSource started = NewSignal();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            TestDocumentReader interrupted = Install(new TestDocumentReader(async (_, cancellationToken) =>
            {
                started.SetResult();
                if (stopsWhenCancelled)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    throw new InvalidOperationException("whatever a reader throws when it is cancelled");
                }

                release.Wait(CancellationToken.None);
                return null;
            }));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            using CancellationTokenSource stopping = new CancellationTokenSource();

            using IServiceScope scope = Services.CreateScope();
            Task run = scope.ServiceProvider.GetRequiredService<MetadataReading>().RunAsync(stopping.Token);
            await started.Task.WaitAsync(RealTimeLimit);
            await stopping.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(RealTimeLimit));
            release.Set();
            await interrupted.AllReadsEndedAsync();
            Assert.Null(await Rows.GetAsync(TestPluginMetadata.Plugin, file.Sha256));
            Install(TestDocumentReader.OfText());
            Assert.Equal(1, (await RunAsync()).Read);
            Assert.Equal(1, interrupted.Reads);
        }

        [Fact]
        public async Task RunAsync_EncryptedFile_IsNeverRead()
        {
            TestDocumentReader reader = Install(TestDocumentReader.OfText());
            Shelf shelf = await CreateShelfAsync();
            await StoreAsync(shelf, "/secret.txt", AgeBytes, "text/plain", encrypted: true);

            await RunAsync();

            Assert.Equal(0, reader.Reads);
        }

        [Fact]
        public async Task RunAsync_FileEncryptedInPlaceWhileItIsRead_RecordsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a secret");
            Install(new TestDocumentReader(async (contents, cancellationToken) =>
            {
                string text = await TestDocumentReader.ReadTextAsync(contents, cancellationToken);
                await EncryptInPlaceAsync(shelf, file);
                return new JsonObject { ["text"] = text };
            }));

            MetadataReadingSummary summary = await RunAsync();

            Assert.Equal(0, summary.Read);
            await AssertNothingOutlivesAsync(file.Sha256, "a secret");
        }

        [Fact]
        public async Task RunAsync_OnlyAnEncryptedFileHoldsTheBytesOnceRead_RecordsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            byte[] bytes = Encoding.ASCII.GetBytes("age-encryption.org/v1\nstored as plain as well");
            ShelfFile plain = await StoreAsync(shelf, "/plain.txt", bytes, "text/plain");
            await StoreAsync(shelf, "/secret.txt", bytes, "text/plain", encrypted: true);
            Install(new TestDocumentReader(async (_, _) =>
            {
                await EncryptInPlaceAsync(shelf, plain);
                return new JsonObject();
            }));

            MetadataReadingSummary summary = await RunAsync();

            Assert.Equal(0, summary.Read);
            Assert.Null(await Rows.GetAsync(TestPluginMetadata.Plugin, plain.Sha256));
        }

        [Fact]
        public async Task RunAsync_FileDeletedWhileItIsRead_RecordsNothingUntilItIsRestored()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            TrashEntry? deleted = null;
            Install(new TestDocumentReader(async (_, _) =>
            {
                deleted ??= await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
                return new JsonObject();
            }));

            MetadataReadingSummary whileDeleted = await RunAsync();
            await TrashService.RestoreAsync(shelf, deleted!.DeletionId);
            MetadataReadingSummary restored = await RunAsync();

            Assert.Equal((0, 1), (whileDeleted.Read, restored.Read));
        }

        [Fact]
        public async Task RecordAsync_TheServersOwnReadingOfContentsEncryptedMeanwhile_RecordsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");
            await EncryptInPlaceAsync(shelf, photo);

            Assert.False(await RecordOwnAsync(photo.Sha256));

            await AssertNothingOutlivesAsync(photo.Sha256, "Nikon");
        }

        [Fact]
        public async Task RecordAsync_OnlyAnEncryptedFileHoldsTheContents_RecordsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreAsync(shelf, "/secret.txt", AgeBytes, "text/plain", encrypted: true);

            Assert.False(await RecordOwnAsync(file.Sha256));
            Assert.False(await Services.RecordContentDocumentAsync(file.Sha256, "{}"));

            await AssertNothingOutlivesAsync(file.Sha256, "Nikon");
        }

        [Fact]
        public async Task RecordAsync_TheServersOwnVersionHasRecordedThemAlready_ChangesAndLogsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            Assert.True(await RecordOwnAsync(file.Sha256));
            long before = await ChangeLog.GetLatestSequenceAsync();

            Assert.False(await RecordOwnAsync(file.Sha256));

            Assert.Equal(before, await ChangeLog.GetLatestSequenceAsync());
        }

        [Fact]
        public async Task RecordAsync_TheVersionHasRecordedThemAlready_ChangesAndLogsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            Assert.True(await Services.RecordContentDocumentAsync(file.Sha256, "{\"first\":true}"));
            long before = await ChangeLog.GetLatestSequenceAsync();

            Assert.False(await Services.RecordContentDocumentAsync(file.Sha256, "{\"second\":true}"));

            Assert.Equal("{\"first\":true}", await Services.GetContentDocumentAsync(file.Sha256));
            Assert.Equal(before, await ChangeLog.GetLatestSequenceAsync());
        }

        [Fact]
        public async Task SwapAsync_FileEncryptedInPlace_DropsEveryPluginsReadingAtOnceAndFromTheLog()
        {
            InstallPlugins(Plugin(TestDocumentReader.OfText(), "first"), Plugin(TestDocumentReader.OfText(), "second"));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a secret");
            await RunAsync();
            InstallPlugins(Plugin(TestDocumentReader.OfText(version: 2), "first"));
            await RunAsync();

            // The second plugin is gone, the first has read them twice: all of it goes all the same.
            await EncryptInPlaceAsync(shelf, file);

            await AssertNothingOutlivesAsync(file.Sha256, "a secret");
        }

        [Fact]
        public async Task SwapAsync_FileEncryptedInPlaceWhileAPlainCopyRemains_KeepsTheCopysReading()
        {
            Install(TestDocumentReader.OfText());
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "shared");
            ShelfFile copy = await StoreTextAsync(shelf, "/copy.txt", "shared");
            await RunAsync();

            await EncryptInPlaceAsync(shelf, file);
            Assert.Equal("{\"text\":\"shared\"}", await Services.GetContentDocumentAsync(copy.Sha256));

            await EncryptInPlaceAsync(shelf, copy);
            await AssertNothingOutlivesAsync(copy.Sha256, "shared");
        }

        [Fact]
        public async Task SwapAsync_OnlyAnEncryptedFileHoldsTheBytesStill_DropsTheReading()
        {
            Install(TestDocumentReader.OfText());
            Shelf shelf = await CreateShelfAsync();
            byte[] bytes = Encoding.ASCII.GetBytes("age-encryption.org/v1\nstored as plain as well");
            ShelfFile plain = await StoreAsync(shelf, "/plain.txt", bytes, "text/plain");
            await StoreAsync(shelf, "/secret.txt", bytes, "text/plain", encrypted: true);
            await RunAsync();

            await EncryptInPlaceAsync(shelf, plain);

            await AssertNothingOutlivesAsync(plain.Sha256, "stored as plain as well");
        }

        [Fact]
        public async Task RunAsync_ContentsNoFileHoldsAnyMore_DropsTheirRowsOfUninstalledPluginsToo()
        {
            Install(TestDocumentReader.OfText());
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a secret");
            await RunAsync();
            InstallPlugins();
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            Assert.Equal(0, (await RunAsync()).Removed);
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            MetadataReadingSummary summary = await RunAsync();

            Assert.Equal(1, summary.Removed);
            await AssertNothingOutlivesAsync(file.Sha256, "a secret");
        }

        [Fact]
        public async Task RunAsync_OnlyAnEncryptedFileHoldsTheContentsOncePurged_DropsTheirRows()
        {
            Install(TestDocumentReader.OfText());
            Shelf shelf = await CreateShelfAsync();
            byte[] bytes = Encoding.ASCII.GetBytes("age-encryption.org/v1\nstored as plain as well");
            ShelfFile plain = await StoreAsync(shelf, "/plain.txt", bytes, "text/plain");
            await StoreAsync(shelf, "/secret.txt", bytes, "text/plain", encrypted: true);
            await RunAsync();
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(plain.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            MetadataReadingSummary summary = await RunAsync();

            Assert.Equal(1, summary.Removed);
            await AssertNothingOutlivesAsync(plain.Sha256, "stored as plain as well");
        }

        [Fact]
        public async Task RunAsync_OnAReplica_RunsNoReader()
        {
            TestDocumentReader reader = Install(TestDocumentReader.OfText());
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");
            await TestReplicas.MarkAsReplicaAsync(Services);

            await RunAsync();

            Assert.Equal(0, reader.Reads);
        }

        private IContentPluginMetadataRepository Rows => Services.GetRequiredService<IContentPluginMetadataRepository>();

        /// <summary>Makes <paramref name="reader"/> the one installed plugin's, as a restart with it would.</summary>
        private TestDocumentReader Install(TestDocumentReader reader)
        {
            InstallPlugins(Plugin(reader, TestPluginMetadata.Plugin));
            return reader;
        }

        private static TaskCompletionSource NewSignal()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static TestMetadataPlugin Plugin(TestDocumentReader reader, string id)
        {
            PluginField text = new PluginField("text", "Text", PluginFieldKind.Text, PluginDocumentSubject.Contents, searched: true, listed: true);
            return new TestMetadataPlugin(new PluginDeclaration(id, id, [text]), reader);
        }

        private static (int Read, int Failed) Counts(MetadataReadingSummary summary)
        {
            return (summary.Read, summary.Failed);
        }

        private Task<bool> RecordOwnAsync(string sha256)
        {
            return Services.InTransactionAsync((provider, session) => provider.GetRequiredService<IObjectMetadataRepository>()
                .RecordAsync(ObjectMetadata.Of(sha256, MetadataReader.Version, TestInstants.Now, new ContentMetadata { Camera = "Nikon" }), session));
        }

        /// <summary>No reader's row of the contents is left, and nothing in the whole log holds <paramref name="plaintext"/>.</summary>
        private async Task AssertNothingOutlivesAsync(string sha256, string plaintext)
        {
            await using IDbSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionAsync();
            Assert.Equal(0, await TestQueries.CountAsync(session, $"object_metadata WHERE sha256 = '{sha256}'"));
            Assert.Equal(0, await TestQueries.CountAsync(session, $"content_plugin_metadata WHERE sha256 = '{sha256}'"));
            Assert.Equal(0, await TestQueries.CountAsync(session, $"plugin_field_value WHERE sha256 = '{sha256}' OR instr(text_fold, '{plaintext}') > 0 OR instr(listed, '{plaintext}') > 0"));
            Assert.Equal(0, await TestQueries.CountAsync(session, $"change_log WHERE instr(payload, '{plaintext}') > 0"));
        }
    }
}
