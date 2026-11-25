using System.Linq.Expressions;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Tools;
using ByteShelf.Tools.Mcp;
using ByteShelfClient;
using ByteShelfCommon;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Moq;

namespace ByteShelf.Server.Tests.Mcp
{
    /// <summary>How long the tools wait for a preview that is being made, and which they show.</summary>
    public sealed class AgentPreviewsTests
    {
        // Written out rather than taken from the code, so a change of it is noticed.
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

        private static readonly Expression<Func<IShelfClient, Task<DerivedFileContent>>> OpenPreview =
            shelf => shelf.OpenDerivedAsync(It.IsAny<EntryReference>(), DerivedKind.Agent, It.IsAny<CancellationToken>());

        private readonly TestClock _clock = new TestClock(TestInstants.Now);
        private readonly Mock<IShelfClient> _shelf = new Mock<IShelfClient>();

        [Fact]
        public async Task ReadAsync_BeingMade_AsksAgainUntilItIs()
        {
            _shelf.SetupSequence(OpenPreview)
                .ThrowsAsync(NotReady())
                .ReturnsAsync(new DerivedFileContent("image/jpeg", new MemoryStream([1, 2, 3])));

            Task<byte[]?> read = AgentPreviews.ReadAsync(FileSource.Live(_shelf.Object), File(), _clock, CancellationToken.None);
            await _clock.WaitForTimerAsync(TimeSpan.FromSeconds(1));
            _clock.Advance(TimeSpan.FromSeconds(1));

            Assert.Equal([1, 2, 3], await read);
        }

        [Fact]
        public async Task ReadAsync_StillBeingMadeAfterHalfAMinute_GivesUp()
        {
            _shelf.Setup(OpenPreview)
                .Returns(async (EntryReference _, DerivedKind _, CancellationToken cancellationToken) =>
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return null!;
                });

            Task<byte[]?> read = AgentPreviews.ReadAsync(FileSource.Live(_shelf.Object), File(), _clock, CancellationToken.None);
            await _clock.WaitForTimerAsync(WaitLimit);
            _clock.Advance(WaitLimit - TimeSpan.FromMilliseconds(1));
            Assert.False(read.IsCompleted);
            _clock.Advance(TimeSpan.FromMilliseconds(1));

            await Assert.ThrowsAsync<TimeoutException>(() => read);
        }

        [Fact]
        public async Task ReadAsync_CancelledByTheAgent_IsCancelled()
        {
            _shelf.Setup(OpenPreview).ThrowsAsync(NotReady());
            using CancellationTokenSource cancelled = new CancellationTokenSource();

            Task<byte[]?> read = AgentPreviews.ReadAsync(FileSource.Live(_shelf.Object), File(), _clock, cancelled.Token);
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        }

        [Fact]
        public async Task DownloadFile_StillBeingMadeAfterHalfAMinute_SaysToCallAgainOrHowToFetchIt()
        {
            _shelf.Setup(OpenPreview).ThrowsAsync(NotReady());

            Task<IEnumerable<ContentBlock>> read = DownloadAsync();
            await _clock.WaitForTimerAsync(TimeSpan.FromSeconds(1));
            _clock.Advance(WaitLimit);

            Assert.Equal(
                "A preview of /a.heic is still being made: call again in a while, or make a link to it with share_file and fetch it from there, such as with curl.",
                (await Assert.ThrowsAsync<McpException>(() => read)).Message);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        public async Task DownloadFile_APreviewOfAtMostOneMebibyte_IsShown(int bytesOver, bool expected)
        {
            byte[] preview = new byte[1024 * 1024 + bytesOver];
            new byte[] { 0xFF, 0xD8, 0xFF }.CopyTo(preview, 0);
            _shelf.Setup(OpenPreview).ReturnsAsync(() => new DerivedFileContent("image/jpeg", new MemoryStream(preview)));

            if (expected)
            {
                Assert.Equal(preview.Length, Assert.Single((await DownloadAsync()).OfType<ImageContentBlock>()).DecodedData.Length);
            }
            else
            {
                Assert.StartsWith("the preview made of /a.heic is no image these tools can answer;", (await Assert.ThrowsAsync<McpException>(DownloadAsync)).Message, StringComparison.Ordinal);
            }
        }

        private Task<IEnumerable<ContentBlock>> DownloadAsync()
        {
            ShelfFile file = File();
            _shelf.Setup(shelf => shelf.GetFileAsync(It.IsAny<EntryReference>(), It.IsAny<CancellationToken>())).ReturnsAsync(file);
            _shelf.Setup(shelf => shelf.OpenReadAsync(It.IsAny<EntryReference>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream([0xFF]));
            Mock<IByteShelfClient> client = new Mock<IByteShelfClient>();
            client.Setup(each => each.Shelf("docs")).Returns(_shelf.Object);
            return new ByteShelfMcpTools(new OneClient(client.Object), ToolHost.Server([MediaTypePattern.Of("image/*")]), _clock).DownloadFileAsync("/a.heic", shelf: "docs");
        }

        private static ByteShelfException NotReady()
        {
            return new ByteShelfException("Still being made.", ErrorCodes.NotReady);
        }

        private static ShelfFile File()
        {
            return TestFiles.Record("/a.heic", "image/heic");
        }

        private sealed class OneClient : IShelfClientSource
        {
            private readonly IByteShelfClient _client;

            public OneClient(IByteShelfClient client)
            {
                _client = client;
            }

            public Task<IByteShelfClient> OpenAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_client);
            }
        }
    }
}
