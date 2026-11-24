using System.Text.Json.Nodes;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>How long one read may take, to the instant, and what is left of it.</summary>
    public class ContentReadRunnerTests : ServiceTestBase
    {
        private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan OneMoment = TimeSpan.FromMilliseconds(1);

        // Written out rather than taken from the code, so a change of either is noticed.
        private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan StopLimit = TimeSpan.FromSeconds(5);

        private readonly RecordingLogger<ContentReadRunner> _logger = new RecordingLogger<ContentReadRunner>();

        [Fact]
        public async Task ReadAsync_WithinTheReadLimit_IsNotCancelled()
        {
            TaskCompletionSource<CancellationToken> started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<JsonObject?> found = NewSignal();
            TestDocumentReader reader = new TestDocumentReader((_, cancellationToken) =>
            {
                started.SetResult(cancellationToken);
                return found.Task;
            });
            ContentReadRunner runner = NewRunner();

            Task<ReadOutcome?> read = runner.ReadAsync(Reader(reader), await StoreAFileAsync(), CancellationToken.None);
            CancellationToken given = await started.Task.WaitAsync(RealTimeLimit);
            await Clock.WaitForTimerAsync(ReadLimit);
            Clock.Advance(ReadLimit - OneMoment);
            Assert.False(given.IsCancellationRequested);
            found.SetResult(new JsonObject());

            Assert.False((await read.WaitAsync(RealTimeLimit))!.Failed);
        }

        [Fact]
        public async Task ReadAsync_StoppingWithinTheStopLimit_FailsAndIsNotLeftBehind()
        {
            TaskCompletionSource<CancellationToken> started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<JsonObject?> stopped = NewSignal();
            TestDocumentReader reader = new TestDocumentReader((_, cancellationToken) =>
            {
                started.SetResult(cancellationToken);
                return stopped.Task;
            });
            ContentReadRunner runner = NewRunner();
            ContentReader contentReader = Reader(reader);

            Task<ReadOutcome?> read = runner.ReadAsync(contentReader, await StoreAFileAsync(), CancellationToken.None);
            CancellationToken given = await started.Task.WaitAsync(RealTimeLimit);
            await Clock.WaitForTimerAsync(ReadLimit);
            Clock.Advance(ReadLimit);
            await Clock.WaitForTimerAsync(StopLimit);
            Assert.True(given.IsCancellationRequested);
            Clock.Advance(StopLimit - OneMoment);
            stopped.SetResult(new JsonObject());

            Assert.True((await read.WaitAsync(RealTimeLimit))!.Failed);
            Assert.False(runner.IsLeftBehind(contentReader));
        }

        [Fact]
        public async Task ReadAsync_StillRunningAtTheStopLimit_IsLeftBehindAndItsFileClosedOnceItEnds()
        {
            TaskCompletionSource<JsonObject?> stuck = NewSignal();
            TestDocumentReader reader = new TestDocumentReader((_, _) => stuck.Task);
            ContentReadRunner runner = NewRunner();
            ContentReader contentReader = Reader(reader);

            Task<ReadOutcome?> read = runner.ReadAsync(contentReader, await StoreAFileAsync(), CancellationToken.None);
            await Clock.WaitForTimerAsync(ReadLimit);
            Clock.Advance(ReadLimit);
            await Clock.WaitForTimerAsync(StopLimit);
            Clock.Advance(StopLimit);

            Assert.True((await read.WaitAsync(RealTimeLimit))!.Failed);
            Assert.True(runner.IsLeftBehind(contentReader));
            Assert.False(runner.IsLeftBehind(Reader(reader, "another")));
            stuck.SetResult(null);
            await reader.AllReadsEndedAsync();
        }

        [Fact]
        public async Task ReadAsync_ThatEnds_ClosesItsFile()
        {
            TestDocumentReader reader = TestDocumentReader.OfText();

            await NewRunner().ReadAsync(Reader(reader), await StoreAFileAsync(), CancellationToken.None);

            await reader.AllReadsEndedAsync();
        }

        [Fact]
        public async Task ReadAsync_ReaderThrows_LogsTheExceptionsTypeAndNothingItSays()
        {
            TestDocumentReader reader = new TestDocumentReader((_, _) => throw new FormatException("'a secret' is no number"));

            ReadOutcome? outcome = await NewRunner().ReadAsync(Reader(reader), await StoreAFileAsync(), CancellationToken.None);

            Assert.True(outcome!.Failed);
            string message = Assert.Single(_logger.Messages);
            Assert.Contains(nameof(FormatException), message, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", message, StringComparison.Ordinal);
            Assert.Empty(_logger.Exceptions);
        }

        [Fact]
        public async Task ReadAsync_ObjectIsGone_ReadsNothing()
        {
            TestDocumentReader reader = TestDocumentReader.OfText();

            Assert.Null(await NewRunner().ReadAsync(Reader(reader), Sha256Of("never stored"), CancellationToken.None));

            Assert.Equal(0, reader.Reads);
        }

        private ContentReadRunner NewRunner()
        {
            return new ContentReadRunner(ObjectStore, Clock, _logger);
        }

        private PluginContentReader Reader(TestDocumentReader reader, string plugin = TestPluginMetadata.Plugin)
        {
            return new PluginContentReader(plugin, new MetadataPluginReader(reader), Services.GetRequiredService<IContentPluginMetadataRepository>());
        }

        private async Task<string> StoreAFileAsync()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            return file.Sha256;
        }

        private static TaskCompletionSource<JsonObject?> NewSignal()
        {
            return new TaskCompletionSource<JsonObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
