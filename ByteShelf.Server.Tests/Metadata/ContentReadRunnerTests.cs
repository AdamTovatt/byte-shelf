using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>How long one read may take, to the instant, and what is left of it.</summary>
    public class ContentReadRunnerTests : ServiceTestBase
    {
        private static readonly RecordReading Nothing = (_, _, _) => Task.FromResult(true);

        private static readonly TimeSpan OneMoment = TimeSpan.FromMilliseconds(1);

        private readonly RecordingLogger<ContentReadRunner> _logger = new RecordingLogger<ContentReadRunner>();

        [Fact]
        public async Task ReadAsync_WithinTheReadLimit_IsNotCancelled()
        {
            using BlockingRead blocking = new BlockingRead();
            ContentReadRunner runner = NewRunner();

            Task<ReadOutcome?> read = ReadAsync(runner, blocking.Read, await StoreAFileAsync());
            await blocking.Started.WaitAsync(TestReadLimits.Real);
            await Clock.WaitForTimerAsync(TestReadLimits.Read);
            Clock.Advance(TestReadLimits.Read - OneMoment);
            blocking.GoOn();

            Assert.False((await read.WaitAsync(TestReadLimits.Real))!.Failed);
            Assert.False(blocking.WasCancelled);
        }

        [Fact]
        public async Task ReadAsync_StoppingWithinTheStopLimit_FailsAndIsNotLeftBehind()
        {
            using BlockingRead blocking = new BlockingRead();
            ContentReadRunner runner = NewRunner();

            Task<ReadOutcome?> read = ReadAsync(runner, blocking.Read, await StoreAFileAsync());
            await blocking.Started.WaitAsync(TestReadLimits.Real);
            await Clock.WaitForTimerAsync(TestReadLimits.Read);
            Clock.Advance(TestReadLimits.Read);
            await Clock.WaitForTimerAsync(TestReadLimits.Stop);
            Clock.Advance(TestReadLimits.Stop - OneMoment);
            blocking.GoOn();

            Assert.True((await read.WaitAsync(TestReadLimits.Real))!.Failed);
            Assert.True(blocking.WasCancelled);
            Assert.True(runner.MayRun);
        }

        [Fact]
        public async Task ReadAsync_StillRunningAtTheStopLimit_IsLeftBehindAndItsFileClosedOnceItEnds()
        {
            using BlockingRead blocking = new BlockingRead();
            ContentReadRunner runner = NewRunner();

            Task<ReadOutcome?> read = ReadAsync(runner, blocking.Read, await StoreAFileAsync());
            await blocking.Started.WaitAsync(TestReadLimits.Real);
            await Clock.WaitForTimerAsync(TestReadLimits.Read);
            Clock.Advance(TestReadLimits.Read);
            await Clock.WaitForTimerAsync(TestReadLimits.Stop);
            Clock.Advance(TestReadLimits.Stop);

            Assert.True((await read.WaitAsync(TestReadLimits.Real))!.Failed);
            Assert.False(runner.MayRun);
            blocking.GoOn();
            await blocking.ClosedAsync();
        }

        [Fact]
        public async Task ReadAsync_ThatOnlyReadsItsStream_StopsWhenCancelledThoughItLooksAtNoToken()
        {
            ContentReadRunner runner = NewRunner();

            Task<ReadOutcome?> read = ReadAsync(
                runner,
                contents =>
                {
                    while (contents.Length == 1)
                    {
                        contents.Position = 0;
                        contents.ReadByte();
                    }
                },
                await StoreAFileAsync());
            await Clock.WaitForTimerAsync(TestReadLimits.Read);
            Clock.Advance(TestReadLimits.Read);

            // The clock stands still from here, so it stops of its own and is never left behind.
            Assert.True((await read.WaitAsync(TestReadLimits.Real))!.Failed);
            Assert.True(runner.MayRun);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ReadAsync_ShuttingDown_IsNoFailureOfTheReader(bool stopsWhenCancelled)
        {
            using BlockingRead blocking = new BlockingRead();
            ContentReadRunner runner = NewRunner();
            using CancellationTokenSource stopping = new CancellationTokenSource();

            Task read = ReadAsync(runner, blocking.Read, await StoreAFileAsync(), stopping.Token);
            await blocking.Started.WaitAsync(TestReadLimits.Real);
            await stopping.CancelAsync();
            if (stopsWhenCancelled)
            {
                blocking.GoOn();
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TestReadLimits.Real));
            blocking.GoOn();
            await blocking.ClosedAsync();
            Assert.True(runner.MayRun);
        }

        [Fact]
        public async Task ReadAsync_ThatEnds_ClosesItsFile()
        {
            Stream? given = null;

            await ReadAsync(NewRunner(), contents => given = contents, await StoreAFileAsync());

            Assert.False(given!.CanRead);
        }

        [Fact]
        public async Task ReadAsync_ReaderThrows_LogsTheExceptionsTypeAndNothingItSays()
        {
            ReadOutcome? outcome = await ReadAsync(NewRunner(), _ => throw new FormatException("'a secret' is no number"), await StoreAFileAsync());

            Assert.True(outcome!.Failed);
            string message = Assert.Single(_logger.Messages);
            Assert.Contains(nameof(FormatException), message, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", message, StringComparison.Ordinal);
            Assert.Empty(_logger.Exceptions);
        }

        [Fact]
        public async Task ReadAsync_ObjectIsGone_ReadsNothing()
        {
            bool wasRead = false;

            Assert.Null(await ReadAsync(NewRunner(), _ => wasRead = true, Sha256Of("never stored")));

            Assert.False(wasRead);
        }

        private ContentReadRunner NewRunner()
        {
            return new ContentReadRunner(ObjectStore, Clock, _logger);
        }

        private static Task<ReadOutcome?> ReadAsync(ContentReadRunner runner, Action<Stream> read, string sha256, CancellationToken cancellationToken = default)
        {
            return runner.ReadAsync(
                sha256,
                contents =>
                {
                    read(contents);
                    return Nothing;
                },
                Nothing,
                cancellationToken);
        }

        private async Task<string> StoreAFileAsync()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            return file.Sha256;
        }

        /// <summary>A read that waits until it is told to go on, and then reads a byte.</summary>
        private sealed class BlockingRead : IDisposable
        {
            private readonly ManualResetEventSlim _goOn = new ManualResetEventSlim();
            private readonly TaskCompletionSource _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            private Stream? _contents;

            public Task Started => _started.Task;

            /// <summary>Whether reading was refused once it went on.</summary>
            public bool WasCancelled { get; private set; }

            public void Read(Stream contents)
            {
                _contents = contents;
                _started.SetResult();
                _goOn.Wait();
                try
                {
                    contents.ReadByte();
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    throw;
                }
            }

            public void GoOn()
            {
                _goOn.Set();
            }

            /// <summary>Waits until the read has ended and its file is closed; throws when it is not.</summary>
            public async Task ClosedAsync()
            {
                using CancellationTokenSource limit = new CancellationTokenSource(TestReadLimits.Real);
                while (_contents!.CanRead)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(5), limit.Token);
                }
            }

            public void Dispose()
            {
                _goOn.Dispose();
            }
        }
    }
}
