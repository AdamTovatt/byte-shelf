using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>Links to share a file, the same in both clients.</summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task CreateDownloadLinkAsync_NoLifetime_IsAnAbsoluteLinkForADay()
        {
            await UploadTextAsync("/a.txt", "a");
            DateTimeOffset before = DateTimeOffset.UtcNow;

            DownloadLink link = await Shelf.CreateDownloadLinkAsync("/a.txt");

            // A scheme, not just absolute: on Unix a path such as /api/x parses as an absolute file URI.
            Assert.Matches("^https?://", link.Url);
            // The server cuts the expiry to the whole second its token keeps.
            Assert.InRange(link.ExpiresAt, before + DownloadLink.DefaultLifetime - TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow + DownloadLink.DefaultLifetime);
        }

        [Fact]
        public async Task CreateDownloadLinkAsync_Lifetime_RunsOutThen()
        {
            await UploadTextAsync("/a.txt", "a");
            DateTimeOffset before = DateTimeOffset.UtcNow;

            DownloadLink link = await Shelf.CreateDownloadLinkAsync("/a.txt", TimeSpan.FromMinutes(90));

            // The server cuts the expiry to the whole second its token keeps.
            Assert.InRange(link.ExpiresAt, before.AddMinutes(90).AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(90));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(30)]
        [InlineData(5430)]
        [InlineData((7 * 24 * 3600) + 60)]
        public async Task CreateDownloadLinkAsync_LifetimeNotAllowed_ThrowsArgumentException(long seconds)
        {
            await UploadTextAsync("/a.txt", "a");

            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.CreateDownloadLinkAsync("/a.txt", TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public async Task CreateDownloadLinkAsync_FileInTheTrash_ThrowsNotFound()
        {
            await UploadTextAsync("/a.txt", "a");
            await Shelf.DeleteFileAsync("/a.txt");

            ByteShelfException exception = await RefusalOf(Shelf.CreateDownloadLinkAsync("/a.txt"));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }
    }
}
