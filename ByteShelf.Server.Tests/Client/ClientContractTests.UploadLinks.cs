using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task CreateUploadLinkAsync_NoOptions_IsAnAbsoluteLinkForTheNormalizedPathFor15Minutes()
        {
            DateTimeOffset before = DateTimeOffset.UtcNow;

            UploadLink link = await Shelf.CreateUploadLinkAsync("in/a.txt/");

            // A scheme, not just absolute: on Unix a path such as /api/x parses as an absolute file URI.
            Assert.Matches("^https?://", link.Url);
            Assert.Equal(("/in/a.txt", false), (link.Path, link.Overwrite));
            // The server cuts the expiry to the whole second its token keeps.
            Assert.InRange(link.ExpiresAt, before + UploadLink.Lifetime.Default - TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow + UploadLink.Lifetime.Default);
        }

        [Fact]
        public async Task CreateUploadLinkAsync_Lifetime_RunsOutThen()
        {
            DateTimeOffset before = DateTimeOffset.UtcNow;

            UploadLink link = await Shelf.CreateUploadLinkAsync("/a.txt", new UploadLinkOptions { Lifetime = TimeSpan.FromHours(24), Overwrite = true });

            Assert.True(link.Overwrite);
            Assert.InRange(link.ExpiresAt, before.AddHours(24).AddSeconds(-1), DateTimeOffset.UtcNow.AddHours(24));
        }

        [Theory]
        [InlineData("/a.txt", 30L, null)]
        [InlineData("/a.txt", (24 * 3600) + 60L, null)]
        [InlineData("/", null, null)]
        [InlineData("/a.txt", null, 3000)]
        public async Task CreateUploadLinkAsync_NotAllowed_ThrowsArgumentException(string path, long? lifetimeSeconds, int? descriptionLength)
        {
            UploadLinkOptions options = new UploadLinkOptions
            {
                Lifetime = lifetimeSeconds is long seconds ? TimeSpan.FromSeconds(seconds) : null,
                Description = descriptionLength is int length ? new string('d', length) : null,
            };

            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.CreateUploadLinkAsync(path, options));
        }
    }
}
