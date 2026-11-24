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
        /// <summary>A pass in a scope of its own, as the worker runs it.</summary>
        private Task<PluginMetadataReadingSummary> RunAsync()
        {
            return Services.ReadPluginMetadataAsync();
        }

        [Fact]
        public async Task RunAsync_APluginsReader_RecordsWhatItFindsAndLogsIt()
        {
            Install(TestDocumentReader.OfText());
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            long before = await ChangeLog.GetLatestSequenceAsync();

            PluginMetadataReadingSummary summary = await RunAsync();

            Assert.Equal(new PluginMetadataReadingSummary(OnReplica: false, Read: 1, Failed: 0), summary);
            ContentPluginMetadata row = (await Rows.GetAsync(TestPluginMetadata.Plugin, file.Sha256))!;
            Assert.Equal(("{\"text\":\"hello\"}", 1, TestInstants.Now), (row.Document?.Json, row.ReaderVersion, row.ReadAt));
            ChangeLogEntry logged = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.ContentPluginMetadata, row.Id, ChangeAction.Added), (logged.EntityType, logged.EntityId, logged.Action));
            Assert.Equal(row.Document, ChangePayload.Deserialize<ContentPluginMetadata>(logged.Payload!).Document);
        }

        /// <summary>Both jobs run as the server runs them, the plugins' one woken by the upload it reads.</summary>
        [Fact]
        public async Task Workers_APluginsReaderIsBusy_TheServersOwnReadsANewUploadMeanwhile()
        {
            TaskCompletionSource started = NewSignal();
            TaskCompletionSource release = NewSignal();
            Install(new TestDocumentReader(async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return null;
            }));
            Shelf shelf = await CreateShelfAsync();
            IServiceScopeFactory scopes = Services.GetRequiredService<IServiceScopeFactory>();
            MetadataReadingSignal signal = Services.GetRequiredService<MetadataReadingSignal>();
            PassLogger<MetadataReadingWorker> passes = new PassLogger<MetadataReadingWorker>();
            PassLogger<PluginMetadataReadingWorker> pluginPasses = new PassLogger<PluginMetadataReadingWorker>();
            MetadataReadingWorker own = new MetadataReadingWorker(scopes, signal, Clock, passes);
            PluginMetadataReadingWorker plugins = new PluginMetadataReadingWorker(scopes, signal, Clock, pluginPasses);
            await own.StartAsync(CancellationToken.None);
            await plugins.StartAsync(CancellationToken.None);
            await Task.WhenAll(passes.Finished(1), pluginPasses.Finished(1)).WaitAsync(TestReadLimits.Real);

            // The fake clock never moves, so only the uploads start passes.
            await StoreTextAsync(shelf, "/a.txt", "a");
            await started.Task.WaitAsync(TestReadLimits.Real);
            await passes.Finished(2).WaitAsync(TestReadLimits.Real);
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");
            await passes.Finished(3).WaitAsync(TestReadLimits.Real);

            Assert.NotNull(await Services.GetRequiredService<IObjectMetadataRepository>().GetBySha256Async(photo.Sha256));
            Assert.False(pluginPasses.Finished(2).IsCompleted);
            release.SetResult();
            await pluginPasses.Finished(2).WaitAsync(TestReadLimits.Real);
            await own.StopAsync(CancellationToken.None);
            await plugins.StopAsync(CancellationToken.None);
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

            PluginMetadataReadingSummary first = await RunAsync();
            PluginMetadataReadingSummary second = await RunAsync();

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

            PluginMetadataReadingSummary summary = await RunAsync();

            Assert.Equal((1, 1), (summary.Read, summary.Failed));
            Assert.Null((await Rows.GetAsync(TestPluginMetadata.Plugin, file.Sha256))!.Document);
        }

        /// <summary>As one whose constructor fails: what it would have read stays to be read, and the other readers go on.</summary>
        [Fact]
        public async Task RunAsync_AReaderThatCannotRun_LeavesWhatItWouldReadForTheNextPassAndStopsNoOther()
        {
            TestDocumentReader unavailable = new TestDocumentReader((_, _) => Task.FromResult<JsonObject?>(new JsonObject())) { Unavailable = "it was told to fail" };
            TestDocumentReader other = new TestDocumentReader((_, _) => Task.FromResult<JsonObject?>(new JsonObject()));
            InstallPlugins(Plugin(unavailable, "unavailable"), Plugin(other, "other"));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await StoreTextAsync(shelf, "/b.txt", "b");
            ReaderProblems problems = Services.GetRequiredService<ReaderProblems>();

            Assert.Equal((2, 0), Counts(await RunAsync()));
            Assert.Equal((1, 2), (unavailable.Asked, other.Reads));
            Assert.Null(await Rows.GetAsync("unavailable", file.Sha256));
            Assert.Equal("Its reader of files' contents could not run, so it reads nothing until it can: it was told to fail.", problems.Of("unavailable"));

            unavailable.Unavailable = null;
            Assert.Equal((2, 0), Counts(await RunAsync()));
            Assert.NotNull(await Rows.GetAsync("unavailable", file.Sha256));
            Assert.Null(problems.Of("unavailable"));
        }

        [Fact]
        public async Task RunAsync_ShuttingDown_IsNoFailureOfTheReader()
        {
            TaskCompletionSource started = NewSignal();
            TestDocumentReader interrupted = Install(new TestDocumentReader(async (_, cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            }));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            using CancellationTokenSource stopping = new CancellationTokenSource();

            Task run = Services.ReadPluginMetadataAsync(stopping.Token);
            await started.Task.WaitAsync(TestReadLimits.Real);
            await stopping.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestReadLimits.Real));
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

            PluginMetadataReadingSummary whileDeleted = await RunAsync();
            await TrashService.RestoreAsync(shelf, deleted!.DeletionId);
            PluginMetadataReadingSummary restored = await RunAsync();

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
            foreach ((string plugin, int version) in new[] { ("first", 2), ("second", 1) })
            {
                ContentPluginMetadata read = (await Rows.GetAsync(plugin, file.Sha256))!;
                Assert.Equal((version, "{\"text\":\"a secret\"}"), (read.ReaderVersion, read.Document?.Json));
            }

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
            (ShelfFile plain, _) = await StorePlainAndEncryptedAsync(shelf);
            await RunAsync();
            Assert.Equal("{\"text\":\"age-encryption.org/v1\\nstored as plain as well\"}", await Services.GetContentDocumentAsync(plain.Sha256));

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
            Assert.Equal(0, (await Services.ReadMetadataAsync()).Removed);
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            MetadataReadingSummary summary = await Services.ReadMetadataAsync();

            Assert.Equal(1, summary.Removed);
            await AssertNothingOutlivesAsync(file.Sha256, "a secret");
        }

        [Fact]
        public async Task RunAsync_OnlyAnEncryptedFileHoldsTheContentsOncePurged_DropsTheirRows()
        {
            Install(TestDocumentReader.OfText());
            Shelf shelf = await CreateShelfAsync();
            (ShelfFile plain, _) = await StorePlainAndEncryptedAsync(shelf);
            await RunAsync();
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(plain.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            MetadataReadingSummary summary = await Services.ReadMetadataAsync();

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

        private static (int Read, int Failed) Counts(PluginMetadataReadingSummary summary)
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
