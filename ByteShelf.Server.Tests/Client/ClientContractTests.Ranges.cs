using System.Text;
using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task OpenReadRangeAsync_OffsetAndLength_AnswersThoseBytesWithTheWholeLength()
        {
            ShelfFile stored = await UploadTextAsync("/long.txt", LongText);

            await using ShelfFileRange range = await Shelf.OpenReadRangeAsync("/long.txt", 7, 5);

            Assert.Equal(("789ab", 7L, 5L, 50L, stored.Sha256), (await ReadTextAsync(range), range.Offset, range.Length, range.TotalLength, range.Sha256));
        }

        [Fact]
        public async Task OpenReadRangeAsync_NoLength_ReadsToTheEnd()
        {
            await UploadTextAsync("/long.txt", LongText);

            await using ShelfFileRange range = await Shelf.OpenReadRangeAsync("/long.txt", 46);

            Assert.Equal(("ABCD", 46L, 4L, 50L), (await ReadTextAsync(range), range.Offset, range.Length, range.TotalLength));
        }

        [Theory]
        [InlineData(100L)]
        [InlineData(long.MaxValue)]
        public async Task OpenReadRangeAsync_LengthPastTheEnd_IsCutAtTheEnd(long length)
        {
            await UploadTextAsync("/long.txt", LongText);

            await using ShelfFileRange range = await Shelf.OpenReadRangeAsync("/long.txt", 46, length);

            Assert.Equal(("ABCD", 4L, 50L), (await ReadTextAsync(range), range.Length, range.TotalLength));
        }

        [Fact]
        public async Task OpenReadRangeAsync_FromTheStartWithNoLength_ReadsTheWholeFile()
        {
            ShelfFile stored = await UploadTextAsync("/long.txt", LongText);

            await using ShelfFileRange range = await Shelf.OpenReadRangeAsync("/long.txt", 0);

            Assert.Equal((LongText, 0L, 50L, 50L, stored.Sha256), (await ReadTextAsync(range), range.Offset, range.Length, range.TotalLength, range.Sha256));
        }

        [Theory]
        [InlineData(50L)]
        [InlineData(1000L)]
        public async Task OpenReadRangeAsync_RangeThatIsTheWholeFile_ReadsItChecked(long length)
        {
            ShelfFile stored = await UploadTextAsync("/long.txt", LongText);

            await using ShelfFileRange range = await Shelf.OpenReadRangeAsync("/long.txt", 0, length, stored.Sha256);

            Assert.Equal((LongText, 0L, 50L, 50L, stored.Sha256), (await ReadTextAsync(range), range.Offset, range.Length, range.TotalLength, range.Sha256));
        }

        [Theory]
        [InlineData(50L, null)]
        [InlineData(50L, 1L)]
        [InlineData(51L, 5L)]
        public async Task OpenReadRangeAsync_OffsetAtOrPastTheEnd_ThrowsRangeNotSatisfiable(long offset, long? length)
        {
            await UploadTextAsync("/long.txt", LongText);

            ByteShelfException exception = await RefusalOf(Shelf.OpenReadRangeAsync("/long.txt", offset, length));

            Assert.Equal(ErrorCodes.RangeNotSatisfiable, exception.Code);
        }

        [Fact]
        public async Task OpenReadRangeAsync_EmptyFileFromTheStartWithNoLength_AnswersAnEmptyRange()
        {
            ShelfFile stored = await UploadTextAsync("/empty.txt", string.Empty);

            await using ShelfFileRange range = await Shelf.OpenReadRangeAsync("/empty.txt", 0);

            Assert.Equal((string.Empty, 0L, 0L, 0L, stored.Sha256), (await ReadTextAsync(range), range.Offset, range.Length, range.TotalLength, range.Sha256));
        }

        [Fact]
        public async Task OpenReadRangeAsync_EmptyFileWithALength_ThrowsRangeNotSatisfiable()
        {
            await UploadTextAsync("/empty.txt", string.Empty);

            ByteShelfException exception = await RefusalOf(Shelf.OpenReadRangeAsync("/empty.txt", 0, 1));

            Assert.Equal(ErrorCodes.RangeNotSatisfiable, exception.Code);
        }

        [Fact]
        public async Task OpenReadRangeAsync_ExpectedSha256OfTheContentsInEitherCase_AnswersTheRange()
        {
            ShelfFile stored = await UploadTextAsync("/long.txt", LongText);

            await using ShelfFileRange range = await Shelf.OpenReadRangeAsync("/long.txt", 7, 5, stored.Sha256.ToUpperInvariant());

            Assert.Equal(("789ab", stored.Sha256), (await ReadTextAsync(range), range.Sha256));
        }

        [Theory]
        [InlineData(7L, 5L)] // a part, by If-Range
        [InlineData(0L, null)] // the whole file, by a plain read compared with the ETag alone
        [InlineData(0L, 1000L)] // a range that would be the whole file
        [InlineData(500L, null)] // past the end: changed contents are refused before the range is weighed
        public async Task OpenReadRangeAsync_ExpectedSha256OfOtherContents_ThrowsContentChanged(long offset, long? length)
        {
            ShelfFile before = await UploadTextAsync("/long.txt", "the contents the caller read");
            await UploadTextAsync("/long.txt", LongText, overwrite: true);

            ByteShelfException exception = await RefusalOf(Shelf.OpenReadRangeAsync("/long.txt", offset, length, before.Sha256));

            Assert.Equal(ErrorCodes.ContentChanged, exception.Code);
        }

        [Fact]
        public async Task OpenReadRangeAsync_MissingFile_ThrowsNotFound()
        {
            ByteShelfException exception = await RefusalOf(Shelf.OpenReadRangeAsync("/missing.txt", 0, 5));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        [Fact]
        public async Task OpenReadRangeAsync_EncryptedFile_ReadsTheStoredAgeBytes()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());
            await UploadEncryptedAsync("/secret/a.txt", "contents");
            byte[] stored;
            await using (Stream whole = await Shelf.OpenReadAsync("/secret/a.txt"))
            {
                stored = await ReadAllAsync(whole);
            }

            await using ShelfFileRange range = await Shelf.OpenReadRangeAsync("/secret/a.txt", 3, 10);

            Assert.Equal(stored[3..13], await ReadAllAsync(range.Content));
            Assert.Equal(stored.Length, range.TotalLength);
        }

        [Theory]
        [InlineData(-1L, null)]
        [InlineData(0L, 0L)]
        [InlineData(0L, -1L)]
        public async Task OpenReadRangeAsync_OffsetNegativeOrLengthNotPositive_ThrowsBeforeAnythingIsSent(long offset, long? length)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Shelf.OpenReadRangeAsync("/missing.txt", offset, length));
        }

        [Fact]
        public async Task OpenReadRangeAsync_ExpectedSha256NotAHash_ThrowsBeforeAnythingIsSent()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.OpenReadRangeAsync("/missing.txt", 0, 5, "\"not a hash\""));
        }

        private static async Task<string> ReadTextAsync(ShelfFileRange range)
        {
            return Encoding.UTF8.GetString(await ReadAllAsync(range.Content));
        }

        private static async Task<byte[]> ReadAllAsync(Stream content)
        {
            using MemoryStream read = new MemoryStream();
            await content.CopyToAsync(read);
            return read.ToArray();
        }
    }
}
