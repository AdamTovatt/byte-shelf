using ByteShelf.Tools;
using ByteShelfCommon;

namespace ByteShelf.Cli.Tests
{
    public class TextFormatTests
    {
        private static readonly ShelfFile Video = File(encrypted: false);

        [Fact]
        public void Info_FileWithMetadata_AddsALineForEachPartFound()
        {
            FileMetadata metadata = new FileMetadata
            {
                Readable = true,
                ReadAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                TakenAt = new DateTimeOffset(2024, 6, 1, 14, 30, 0, TimeSpan.FromHours(2)),
                Latitude = 59.3293,
                Longitude = -18.0686,
                DurationSeconds = 3909.4,
                Camera = "Apple iPhone 15",
            };

            string info = TextFormat.Info(Entry.Of(Video), metadata);

            Assert.Contains("taken        2024-06-01 12:30 UTC", info, StringComparison.Ordinal);
            Assert.Contains("position     59.3293, -18.0686", info, StringComparison.Ordinal);
            Assert.Contains("duration     1:05:09", info, StringComparison.Ordinal);
            Assert.Contains("camera       Apple iPhone 15", info, StringComparison.Ordinal);
            Assert.DoesNotContain("pages", info, StringComparison.Ordinal);
        }

        [Fact]
        public void Info_ShortDuration_LeavesOutTheHours()
        {
            string info = TextFormat.Info(Entry.Of(Video), new FileMetadata { Readable = true, ReadAt = DateTimeOffset.UnixEpoch, DurationSeconds = 187.2 });

            Assert.Contains("duration     3:07", info, StringComparison.Ordinal);
        }

        [Fact]
        public void Info_DurationOfMoreThanADay_CountsAllItsHours()
        {
            string info = TextFormat.Info(Entry.Of(Video), new FileMetadata { Readable = true, ReadAt = DateTimeOffset.UnixEpoch, DurationSeconds = (25 * 3600) + 61 });

            Assert.Contains("duration     25:01:01", info, StringComparison.Ordinal);
        }

        [Fact]
        public void Info_ReadableFileNotReadYet_SaysSo()
        {
            string info = TextFormat.Info(Entry.Of(Video), new FileMetadata { Readable = true });

            Assert.Contains("metadata     none read from the contents yet", info, StringComparison.Ordinal);
        }

        [Fact]
        public void Info_FileOfATypeNotRead_SaysItNeverWillBe()
        {
            string info = TextFormat.Info(Entry.Of(Video), FileMetadata.None);

            Assert.Contains("metadata     not read for this type of file", info, StringComparison.Ordinal);
        }

        [Fact]
        public void Info_EncryptedFile_SaysNothingOfMetadata()
        {
            string info = TextFormat.Info(Entry.Of(File(encrypted: true)), FileMetadata.None);

            Assert.DoesNotContain("metadata", info, StringComparison.Ordinal);
        }

        private static ShelfFile File(bool encrypted)
        {
            return new ShelfFile
            {
                Id = new Guid("6a0f5f1e-2b1c-4c9e-9a51-3f2d7c1b0e11"),
                ShelfId = new Guid("0b7c4d2e-8f1a-4e3b-9c6d-5a2f1e0d3c22"),
                DirectoryId = new Guid("9e8d7c6b-5a4f-4e3d-8c2b-1a0f9e8d7c33"),
                Path = "/clip.mov",
                Name = "clip.mov",
                ContentType = "video/quicktime",
                SizeBytes = 10,
                Sha256 = new string('a', 64),
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Encrypted = encrypted,
            };
        }
    }
}
