using System.Text;
using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    /// <summary>The first bytes contents of each format can have. What is none of them is in <see cref="RefusedContentsTests"/>.</summary>
    public sealed class ImageFormatTests
    {
        public static TheoryData<string, string> Heads { get; } = MakeHeads();

        [Theory]
        [MemberData(nameof(Heads))]
        public void Of_TellsEveryVariant(string head, string expected)
        {
            Assert.Equal(expected, ImageFormat.Of(Encoding.Latin1.GetBytes(head))?.MediaType);
        }

        [Fact]
        public async Task Decode_DecodesAJpegSmaller_WhereThatIsAllThatIsWanted()
        {
            using MagickImage image = TestImages.Plain(1600, 800, 255, 0, 0);
            using MemoryStream contents = new MemoryStream(TestImages.Jpeg(image), writable: false);
            ImageSource source = (await ImageSource.OpenAsync(Codecs.Prepare(), contents))!;

            using DecodedImage smaller = source.Decode(400);
            using DecodedImage whole = source.Decode(null);

            Assert.Equal((400u, 1600u), (smaller.Image.Width, whole.Image.Width));
            Assert.Equal((1600, 800), (smaller.UprightWidth, smaller.UprightHeight));
        }

        private static TheoryData<string, string> MakeHeads()
        {
            TheoryData<string, string> heads = new TheoryData<string, string>
            {
                { "ÿØÿà", "image/jpeg" },
                { "ÿØÿá", "image/jpeg" },
                { "\u0089PNG\r\n\u001A\n", "image/png" },
                { "GIF87a", "image/gif" },
                { "GIF89a", "image/gif" },
                { "RIFF\u0010\0\0\0WEBPVP8 ", "image/webp" },
                { "II*\0\u0008\0\0\0\0\0", "image/tiff" },
                { "MM\0*\0\0\0\u0008\0\0", "image/tiff" },
                { "II+\0\u0008\0\0\0\0\0", "image/tiff" },
                { "MM\0+\0\u0008\0\0\0\0", "image/tiff" },
                { "\0\0\u0001\0\u0001\0", "image/x-icon" },
                { "\0\0\u0001\0\0\u0001", "image/x-icon" },
                { "\0\0\0\u0018ftypmif1\0\0\0\0mif1heic", "image/heic" },
                { "\0\0\0\u0018ftypmif1\0\0\0\0mif1avif", "image/avif" },
                { "\0\0\0\u001Cftypavif\0\0\0\0mif1heicavif", "image/avif" },

                // A first box of 256 bytes begins as an icon does.
                { "\0\0\u0001\0ftypheic\0\0\0\0", "image/heic" },
                { "\0\0\u0001\0ftypavif\0\0\0\0", "image/avif" },
            };
            foreach (char size in (char[])['\u000C', '\u0010', '(', '4', '8', '@', 'l', '|'])
            {
                heads.Add($"BM\0\0\0\0\0\0\0\0\0\0\0\0{size}\0\0\0", "image/bmp");
            }

            foreach (string brand in (string[])["heic", "heix", "heim", "heis", "hevc", "hevx", "hevm", "hevs"])
            {
                heads.Add($"\0\0\0\u0010ftyp{brand}\0\0\0\0", "image/heic");
            }

            foreach (string brand in (string[])["avif", "avis"])
            {
                heads.Add($"\0\0\0\u0010ftyp{brand}\0\0\0\0", "image/avif");
            }

            return heads;
        }
    }
}
