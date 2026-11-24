using System.Text.Json.Nodes;
using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    public sealed class ImageReaderTests
    {
        private const string DataUrl = "data:image/webp;base64,";

        [Theory]
        [MemberData(nameof(TestImages.Formats), MemberType = typeof(TestImages))]
        public async Task ReadAsync_SaysTheSizeAndAPlaceholder(string format)
        {
            using MagickImage image = TestImages.Quarters(160, 80);

            JsonObject document = await ReadAsync(TestImages.Of(format, image));

            Assert.Equal(["width", "height", "placeholder"], document.Select(property => property.Key));
            Assert.Equal((160, 80), ((int)document["width"]!, (int)document["height"]!));
            AssertPlaceholder(document, 16, 8, MagickColors.Red, MagickColors.Lime);
        }

        [Fact]
        public async Task ReadAsync_SaysAHeics()
        {
            JsonObject document = await ReadAsync(HeicFile.Make(quarterTurns: 3));

            Assert.Equal((48, 64), ((int)document["width"]!, (int)document["height"]!));
            AssertPlaceholder(document, 12, 16, MagickColors.Red, MagickColors.Red);
        }

        [Theory]
        [InlineData("jpeg")]
        [InlineData("webp")]
        [InlineData("tiff")]
        public async Task ReadAsync_SaysThemUpright(string format)
        {
            using MagickImage image = TestImages.Quarters(160, 80);
            image.SetProfile(new ImageProfile("exif", TestImages.Exif(6)));
            image.Orientation = Orientation.RightTop;

            JsonObject document = await ReadAsync(TestImages.Of(format, image));

            Assert.Equal((80, 160), ((int)document["width"]!, (int)document["height"]!));
            AssertPlaceholder(document, 8, 16, MagickColors.Blue, MagickColors.Red);
        }

        [Fact]
        public async Task ReadAsync_SaysLittle()
        {
            using MagickImage image = TestImages.Noise(800, 800);
            image.SetProfile(ColorProfiles.AdobeRGB1998);

            JsonObject document = await ReadAsync(TestImages.Jpeg(image));

            Assert.InRange(document.ToJsonString().Length, 1, 600);
        }

        private static async Task<JsonObject> ReadAsync(byte[] contents)
        {
            using MemoryStream stream = new MemoryStream(contents, writable: false);
            return (await new ImageReader().ReadAsync(stream))!;
        }

        private static void AssertPlaceholder(JsonObject document, int width, int height, IMagickColor<byte> topLeft, IMagickColor<byte> topRight)
        {
            string url = (string)document["placeholder"]!;
            Assert.StartsWith(DataUrl, url, StringComparison.Ordinal);
            using MagickImage image = new MagickImage(Convert.FromBase64String(url[DataUrl.Length..]), MagickFormat.WebP);
            Assert.Empty(image.ProfileNames);
            ImageAssert.Size(image, width, height);
            ImageAssert.Color(topLeft, ImageAssert.ColorAt(image, 1, 1), tolerance: 20);
            ImageAssert.Color(topRight, ImageAssert.ColorAt(image, width - 2, 1), tolerance: 20);
        }
    }
}
