using System.Reflection;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfClient.InMemory;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>The client contract against the in-memory client, which must behave as the server does.</summary>
    public class InMemoryClientContractTests : ClientContractTests
    {
        protected override Task<IByteShelfClient> CreateAdminClientAsync()
        {
            return Task.FromResult<IByteShelfClient>(new InMemoryByteShelfClient(password: AdminPassword));
        }

        protected override string PrimaryUrl => ((InMemoryByteShelfClient)Client).Address;

        protected override Assembly ReleaseAssembly => typeof(InMemoryByteShelfClient).Assembly;

        protected override Task<IByteShelfClient> CreateServerToFollowAsync()
        {
            return Task.FromResult<IByteShelfClient>(new InMemoryByteShelfClient());
        }

        [Fact]
        public async Task GetFileMetadataAsync_Pdf_HasNoneSinceNothingIsRead()
        {
            await Shelf.UploadAsync("/q3.pdf", new MemoryStream(TestMedia.Pdf(pages: 2, title: "Q3 report")), new UploadOptions { ContentType = "application/pdf" });

            Assert.Same(FileMetadata.None, await Shelf.GetFileMetadataAsync("/q3.pdf"));
        }

        [Fact]
        public async Task SearchAsync_MetadataCriterion_MatchesNothingSinceNothingIsRead()
        {
            await Shelf.UploadAsync("/q3.pdf", new MemoryStream(TestMedia.Pdf(pages: 2, title: "Q3 report")), new UploadOptions { ContentType = "application/pdf" });

            FileSearchResults found = await Shelf.SearchAsync(new FileSearch { MinPageCount = 0 });

            Assert.Empty(found.Files);
        }

        [Fact]
        public async Task SearchAsync_TextInATitle_MatchesNothingSinceNothingIsRead()
        {
            await Shelf.UploadAsync("/q3.pdf", new MemoryStream(TestMedia.Pdf(pages: 2, title: "Q3 report")), new UploadOptions { ContentType = "application/pdf" });

            FileSearchResults found = await Shelf.SearchAsync(new FileSearch { Text = "Q3 report" });

            Assert.Empty(found.Files);
        }

        [Fact]
        public async Task FindDuplicatesAsync_MetadataCriterion_MatchesNothingSinceNothingIsRead()
        {
            byte[] pdf = TestMedia.Pdf(pages: 2, title: "Q3 report");
            await Shelf.UploadAsync("/a.pdf", new MemoryStream(pdf), new UploadOptions { ContentType = "application/pdf" });
            await Shelf.UploadAsync("/b.pdf", new MemoryStream(pdf), new UploadOptions { ContentType = "application/pdf" });

            // Any PDF the server had read meets at least zero pages, so finding none means nothing was read.
            DuplicateSearchResults found = await Shelf.FindDuplicatesAsync(new DuplicateSearch { MinPageCount = 0 });
            DuplicateSearchResults withoutTheCriterion = await Shelf.FindDuplicatesAsync(new DuplicateSearch());

            Assert.Empty(found.Groups);
            Assert.Single(withoutTheCriterion.Groups);
        }
    }
}
