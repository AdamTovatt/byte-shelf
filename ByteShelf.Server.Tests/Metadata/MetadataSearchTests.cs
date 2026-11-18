using System.Text;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>Search by what files' contents carry, once metadata.reading has read them.</summary>
    public class MetadataSearchTests : ServiceTestBase
    {
        private Shelf _shelf = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _shelf = await CreateShelfAsync();
            await StoreAsync(_shelf, "/june.jpg", TestMedia.Photo("2024:06:15 10:00:00", "+00:00", (59.3293, 18.0686), "Apple", "iPhone 15"), "image/jpeg");
            await StoreAsync(_shelf, "/july.jpg", TestMedia.Photo("2024:07:01 00:00:00", "+00:00", make: "Fujifilm", model: "X-T5"), "image/jpeg");
            await StoreAsync(_shelf, "/clip.mov", TestMedia.Movie(new DateTimeOffset(2024, 6, 20, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(45)), "video/quicktime");
            await StoreAsync(_shelf, "/report.pdf", TestMedia.Pdf(pages: 12, title: "Annual Report"), "application/pdf");
            await StoreAsync(_shelf, "/memo.pdf", TestMedia.Pdf(pages: 1, title: "Memo"), "application/pdf");
            await Services.ReadMetadataAsync();
        }

        [Fact]
        public async Task SearchAsync_TakenRange_IncludesItsStartAndExcludesItsEnd()
        {
            FileSearch search = new FileSearch
            {
                TakenFrom = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero),
                TakenBefore = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero),
            };

            Assert.Equal(["/clip.mov", "/june.jpg"], await PathsAsync(search));
        }

        [Fact]
        public async Task SearchAsync_HasGps_FindsOnlyFilesWithAPosition()
        {
            Assert.Equal(["/june.jpg"], await PathsAsync(new FileSearch { HasGps = true }));
        }

        [Fact]
        public async Task SearchAsync_DurationRange_IsInclusive()
        {
            Assert.Equal(["/clip.mov"], await PathsAsync(new FileSearch { MinDurationSeconds = 45, MaxDurationSeconds = 45 }));
            Assert.Empty(await PathsAsync(new FileSearch { MinDurationSeconds = 46 }));
        }

        [Fact]
        public async Task SearchAsync_PageRange_IsInclusive()
        {
            Assert.Equal(["/report.pdf"], await PathsAsync(new FileSearch { MinPageCount = 2 }));
            Assert.Equal(["/memo.pdf"], await PathsAsync(new FileSearch { MaxPageCount = 1 }));
        }

        [Fact]
        public async Task SearchAsync_Text_MatchesTheTitleAndTheCamera()
        {
            Assert.Equal(["/report.pdf"], await PathsAsync(new FileSearch { Text = "annual" }));
            Assert.Equal(["/july.jpg"], await PathsAsync(new FileSearch { Text = "fujifilm x" }));
        }

        [Fact]
        public async Task SearchAsync_MetadataCriterionWithOthers_MustMeetBoth()
        {
            Assert.Equal(["/june.jpg"], await PathsAsync(new FileSearch { ContentType = "image/*", TakenBefore = new DateTimeOffset(2024, 6, 30, 0, 0, 0, TimeSpan.Zero) }));
        }

        [Fact]
        public async Task SearchAsync_EncryptedFileWithAReadFilesContents_MatchesNoMetadata()
        {
            // Bytes the server takes as an age file (it checks only the start) and PdfPig as a PDF, since a
            // PDF's header may come after other bytes: the plain copy's are read, the encrypted copy's never.
            byte[] both = [.. Encoding.ASCII.GetBytes("age-encryption.org/v1\n"), .. TestMedia.Pdf(pages: 3, title: "Hidden plans")];
            await StoreAsync(_shelf, "/plain.pdf", both, "application/pdf");
            await StoreAsync(_shelf, "/secret.pdf", both, "application/pdf", encrypted: true);
            await Services.ReadMetadataAsync();

            Assert.Equal(["/plain.pdf"], await PathsAsync(new FileSearch { Text = "hidden" }));
            Assert.Equal(["/plain.pdf"], await PathsAsync(new FileSearch { MinPageCount = 3, MaxPageCount = 3 }));
        }

        [Fact]
        public async Task FindDuplicatesAsync_MetadataCriterion_GroupsOnlyTheFilesThatMeetIt()
        {
            // Each built once and stored twice: two builds of a PDF differ in their bytes.
            byte[] photo = TestMedia.Photo("2024:08:01 10:00:00", "+00:00", (59.3293, 18.0686), "Apple", "iPhone 15");
            byte[] memo = TestMedia.Pdf(pages: 1, title: "Memo");
            foreach (string copy in new[] { "a", "b" })
            {
                await StoreAsync(_shelf, $"/copies/{copy}.jpg", photo, "image/jpeg");
                await StoreAsync(_shelf, $"/copies/{copy}.pdf", memo, "application/pdf");
            }

            await Services.ReadMetadataAsync();

            DuplicateSearchResults all = await FileService.FindDuplicatesAsync(_shelf, new DuplicateSearch { Under = "/copies" });
            DuplicateSearchResults found = await FileService.FindDuplicatesAsync(_shelf, new DuplicateSearch { Under = "/copies", HasGps = true });

            Assert.Equal(2, all.Groups.Count);
            DuplicateGroup group = Assert.Single(found.Groups);
            Assert.Equal(["/copies/a.jpg", "/copies/b.jpg"], group.Files.Select(file => file.Path));
        }

        private async Task<List<string>> PathsAsync(FileSearch search)
        {
            FileSearchResults results = await FileService.SearchAsync(_shelf, search);
            return results.Files.Select(file => file.Path).ToList();
        }
    }
}
