using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>
    /// Listing and revoking a shelf's links, the same in both clients. A link that ran out is left out
    /// by both, which needs a clock to show: the server's tests (<c>LinksTests</c>) do.
    /// </summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task GetLinksAsync_LinksMade_ListsThemNewestFirstAsTheAdminsWithWhatTheyReach()
        {
            ShelfFile file = await UploadTextAsync("/a.txt", "a");
            DownloadLink download = await Shelf.CreateDownloadLinkAsync("/a.txt");
            UploadLink upload = await Shelf.CreateUploadLinkAsync("/in/b.txt", new UploadLinkOptions { Overwrite = true, Description = "the report" });

            IReadOnlyList<ShelfLink> links = await Shelf.GetLinksAsync();

            Assert.Equal(
                new[]
                {
                    (upload.Id, ShelfLinkKind.Upload, "/in/b.txt", (Guid?)null, true, "the report", upload.ExpiresAt),
                    (download.Id, ShelfLinkKind.Download, "/a.txt", file.Id, false, (string?)null, download.ExpiresAt),
                },
                links.Select(link => (link.Id, link.Kind, link.Path!, link.FileId, link.Overwrite, link.Description, link.ExpiresAt)));
            Assert.All(links, link => Assert.Equal((LinkMakerKind.User, true), (link.CreatedBy.Kind, link.CreatedBy.IsCaller)));
        }

        [Fact]
        public async Task GetLinksAsync_FileInTheTrash_ListsItsLinkWithoutAPath()
        {
            ShelfFile file = await UploadTextAsync("/a.txt", "a");
            await Shelf.CreateDownloadLinkAsync("/a.txt");
            await Shelf.DeleteFileAsync("/a.txt");

            ShelfLink listed = Assert.Single(await Shelf.GetLinksAsync());

            Assert.Equal((null, file.Id), (listed.Path, listed.FileId));
        }

        [Fact]
        public async Task GetLinksAsync_FilePurged_LeavesItsLinkOut()
        {
            await UploadTextAsync("/a.txt", "a");
            await Shelf.CreateDownloadLinkAsync("/a.txt");
            await Shelf.DeleteFileAsync("/a.txt");

            await Shelf.EmptyTrashAsync();

            Assert.Empty(await Shelf.GetLinksAsync());
        }

        [Fact]
        public async Task RevokeLinkAsync_LinkMade_LeavesItOutOfTheList()
        {
            UploadLink kept = await Shelf.CreateUploadLinkAsync("/a.txt");
            UploadLink revoked = await Shelf.CreateUploadLinkAsync("/b.txt");

            await Shelf.RevokeLinkAsync(revoked.Id);

            Assert.Equal(kept.Id, Assert.Single(await Shelf.GetLinksAsync()).Id);
        }

        [Fact]
        public async Task RevokeLinkAsync_NoSuchLink_ThrowsNotFound()
        {
            ByteShelfException exception = await RefusalOf(Shelf.RevokeLinkAsync(Guid.NewGuid()));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }
    }
}
