using System.Text;
using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    /// <summary>The first bytes contents of each format can have. What is none of them is in <see cref="RefusedContentsTests"/>.</summary>
    public sealed class ImageFormatTests
    {
        public static TheoryData<string, MagickFormat> Heads { get; } = MakeHeads();

        [Theory]
        [MemberData(nameof(Heads))]
        public void Of_TellsEveryVariant(string head, MagickFormat expected)
        {
            Assert.Equal(expected, ImageFormat.Of(Encoding.Latin1.GetBytes(head))?.Magick);
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

        private static TheoryData<string, MagickFormat> MakeHeads()
        {
            TheoryData<string, MagickFormat> heads = new TheoryData<string, MagickFormat>
            {
                { "ÿØÿà", MagickFormat.Jpeg },
                { "ÿØÿá", MagickFormat.Jpeg },
                { "\u0089PNG\r\n\u001A\n", MagickFormat.Png },
                { "GIF87a", MagickFormat.Gif },
                { "GIF89a", MagickFormat.Gif },
                { "RIFF\u0010\0\0\0WEBPVP8 ", MagickFormat.WebP },
                { "II*\0\u0008\0\0\0\0\0", MagickFormat.Tiff },
                { "MM\0*\0\0\0\u0008\0\0", MagickFormat.Tiff },
                { "II+\0\u0008\0\0\0\0\0", MagickFormat.Tiff },
                { "MM\0+\0\u0008\0\0\0\0", MagickFormat.Tiff },

                // A camera's CR2 begins as a TIFF, which LibRaw is asked of by the CR2's mark (CameraRawTests).
                { "II*\0\u0010\0\0\0CR\u0002\0", MagickFormat.Tiff },
                { "\0\0\u0001\0\u0001\0", MagickFormat.Ico },
                { "\0\0\u0001\0\0\u0001", MagickFormat.Ico },
                { "\0\0\0\u0018ftypmif1\0\0\0\0mif1heic", MagickFormat.Heic },
                { "\0\0\0\u0018ftypmif1\0\0\0\0mif1avif", MagickFormat.Avif },
                { "\0\0\0\u001Cftypavif\0\0\0\0mif1heicavif", MagickFormat.Avif },

                // A first box of 256 bytes begins as an icon does.
                { "\0\0\u0001\0ftypheic\0\0\0\0", MagickFormat.Heic },
                { "\0\0\u0001\0ftypavif\0\0\0\0", MagickFormat.Avif },
            };
            foreach (char size in (char[])['\u000C', '\u0010', '(', '4', '8', '@', 'l', '|'])
            {
                heads.Add($"BM\0\0\0\0\0\0\0\0\0\0\0\0{size}\0\0\0", MagickFormat.Bmp);
            }

            foreach (string brand in (string[])["heic", "heix", "heim", "heis", "hevc", "hevx", "hevm", "hevs"])
            {
                heads.Add($"\0\0\0\u0010ftyp{brand}\0\0\0\0", MagickFormat.Heic);
            }

            foreach (string brand in (string[])["avif", "avis"])
            {
                heads.Add($"\0\0\0\u0010ftyp{brand}\0\0\0\0", MagickFormat.Avif);
            }

            return heads;
        }
    }
}
