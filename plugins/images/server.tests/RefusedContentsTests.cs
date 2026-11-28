using System.Text;
using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    /// <summary>What nothing is made of and nothing read from: contents that are no image of the formats, or one that is not decoded.</summary>
    public sealed class RefusedContentsTests
    {
        public static TheoryData<string> NoImages { get; } =
        [
            string.Empty,
            "B",
            "BM",
            "BMtext that begins as a bitmap does",
            "GIF90a, nearly",
            "RIFF\u0010\0\0\0WAVEfmt ",
            "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10'/></svg>",
            "<?xml version='1.0'?><image><read filename='label:secret'/></image>",
            "push graphic-context\nviewbox 0 0 10 10\nimage over 0,0 0,0 'label:secret'\npop graphic-context",
            "%!PS-Adobe-3.0\n",
            "%PDF-1.7\n",
            "8BPS\0\u0001\0\0\0\0\0\0\0\u0003",
            "P3 1 1 255 0 0 0",
            "\0\0\u0002\0\u0001\0",
            "\0\0\u0001\0\0\0",
            "\0\0\0\u0018ftypisom\0\0\0\0isommp42",
            "\0\0\0\u0014ftypmif1\0\0\0\0mif1",
            "\0\0\0\u0010ftypmif1\0\0\0\0heic",
            "\0\0\0\u0014ftypmif1heicmif1",
            "\0\0\0\u000CJXL \r\n\u0087\n",
            "\0\0\0\u000CjP  \r\n\u0087\n",
        ];

        public static TheoryData<string> Huge { get; } = ["png", "jpeg", "bmp", "tiff", "gif", "gif frame", "webp", "heic", "x-icon", "dng"];

        [Theory]
        [MemberData(nameof(NoImages))]
        public async Task MakeAsync_MakesNothingOfWhatIsNoImageOfTheFormats(string contents)
        {
            await AssertMakesNothingAsync(Encoding.Latin1.GetBytes(contents));
        }

        [Theory]
        [MemberData(nameof(NoImages))]
        public async Task ReadAsync_SaysNothingOfWhatIsNoImageOfTheFormats(string contents)
        {
            await AssertSaysNothingAsync(Encoding.Latin1.GetBytes(contents));
        }

        [Fact]
        public async Task MakeAsync_MakesNothingOfAnImageAfterOtherBytes()
        {
            using MagickImage image = TestImages.Plain(10, 10, 255, 0, 0);
            await AssertMakesNothingAsync([(byte)' ', .. image.ToByteArray(MagickFormat.Png)]);
        }

        [Theory]
        [MemberData(nameof(TestImages.Formats), MemberType = typeof(TestImages))]
        public async Task MakeAsync_FailsOnOneOfWhichThereIsOnlyAStart(string format)
        {
            using MagickImage image = TestImages.Quarters(80, 40);
            byte[] contents = TestImages.Of(format, image);

            await Assert.ThrowsAnyAsync<MagickException>(() => MadeFiles.MakeAsync(contents[..24]));
        }

        /// <summary>As a browser shows what there is of it.</summary>
        [Fact]
        public async Task MakeAsync_MakesWhatThereIsOfAJpegCutShort()
        {
            using MagickImage image = TestImages.Noise(160, 80);
            byte[] contents = TestImages.Jpeg(image);

            MadeFiles made = await MadeFiles.MakeAsync(contents[..(contents.Length / 2)]);

            Assert.Equal(MadeFiles.DeclaredKinds.Order(), made.Kinds);
        }

        [Theory]
        [InlineData("png")]
        [InlineData("gif")]
        [InlineData("webp")]
        [InlineData("avif")]
        [InlineData("tiff")]
        [InlineData("bmp")]
        [InlineData("x-icon")]
        public async Task MakeAsync_FailsOnAnotherCutShort(string format)
        {
            using MagickImage image = TestImages.Noise(160, 80);
            byte[] contents = TestImages.Of(format, image);

            await Assert.ThrowsAnyAsync<MagickException>(() => MadeFiles.MakeAsync(contents[..(contents.Length / 2)]));
        }

        [Theory]
        [MemberData(nameof(Huge))]
        public async Task MakeAsync_MakesNothingOfASmallFileThatDecodesToAHugeImage(string format)
        {
            using MagickImage image = TestImages.Plain(16, 16, 255, 0, 0);
            await AssertMakesNothingAsync(HugeImages.Of(format, image));
        }

        [Theory]
        [MemberData(nameof(Huge))]
        public async Task ReadAsync_SaysNothingOfASmallFileThatDecodesToAHugeImage(string format)
        {
            using MagickImage image = TestImages.Plain(16, 16, 255, 0, 0);
            await AssertSaysNothingAsync(HugeImages.Of(format, image));
        }

        /// <summary>As a camera's raw is that LibRaw does not read: a TIFF whose first page is a small copy, of a picture none a browser knows.</summary>
        [Fact]
        public async Task MakeAsync_MakesNothingOfATiffWhoseFirstPageIsASmallerCopy()
        {
            await AssertMakesNothingAsync(Tiff(firstIsSmallerCopy: true));
        }

        [Fact]
        public async Task ReadAsync_SaysNothingOfATiffWhoseFirstPageIsASmallerCopy()
        {
            await AssertSaysNothingAsync(Tiff(firstIsSmallerCopy: true));
        }

        /// <summary>As a TIFF with its smaller copies in it has them.</summary>
        [Fact]
        public async Task MakeAsync_MakesOneOfATiffWithASmallerCopyAfterIt()
        {
            MadeFiles made = await MadeFiles.MakeAsync(Tiff(firstIsSmallerCopy: false), DerivedKind.Preview);

            using MagickImage preview = made.Decode(DerivedKind.Preview);
            ImageAssert.Size(preview, 160, 80);
        }

        [Theory]
        [InlineData(200L * 1024 * 1024, 1)]
        [InlineData((200L * 1024 * 1024) + 1, 0)]
        public async Task MakeAsync_MakesNothingOfAFileOver200MiB(long length, int expected)
        {
            using MagickImage image = TestImages.Plain(16, 16, 255, 0, 0);
            using LongStream contents = new LongStream(image.ToByteArray(MagickFormat.Png), length);

            MadeFiles made = await MadeFiles.MakeAsync(contents, DerivedKind.Thumbnail);

            Assert.Equal(expected, made.Kinds.Count());
        }

        private static byte[] Tiff(bool firstIsSmallerCopy)
        {
            using MagickImageCollection pages = [TestImages.Quarters(40, 20), TestImages.Quarters(160, 80)];
            pages[0].SetAttribute("tiff:subfiletype", "REDUCEDIMAGE");
            if (!firstIsSmallerCopy)
            {
                pages.Reverse();
            }

            return pages.ToByteArray(MagickFormat.Tiff);
        }

        private static async Task AssertMakesNothingAsync(byte[] contents)
        {
            Assert.Empty((await MadeFiles.MakeAsync(contents)).Kinds);
        }

        private static async Task AssertSaysNothingAsync(byte[] contents)
        {
            using MemoryStream stream = new MemoryStream(contents, writable: false);
            Assert.Null(await new ImageReader().ReadAsync(stream));
        }
    }
}
