using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    public sealed class HeicTests
    {
        /// <summary>A phone's says so by a turn of the HEIC's own, and again in EXIF, which counts for nothing: turned by both it would lie.</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        public async Task MakeAsync_TurnsItUprightOnce(ushort exifOrientation)
        {
            byte[] contents = HeicFile.Make(quarterTurns: 3, exif: exifOrientation == 0 ? null : TestImages.Exif(exifOrientation));

            MadeFiles made = await MadeFiles.MakeAsync(contents, DerivedKind.Preview, DerivedKind.Full, DerivedKind.Agent);

            foreach (DerivedKind kind in new[] { DerivedKind.Preview, DerivedKind.Full, DerivedKind.Agent })
            {
                // A quarter turn clockwise, so what was left is above.
                using MagickImage image = made.Decode(kind);
                ImageAssert.Size(image, 48, 64);
                ImageAssert.Color(MagickColors.Red, ImageAssert.ColorAt(image, 24, 10));
                ImageAssert.Color(MagickColors.Blue, ImageAssert.ColorAt(image, 24, 54));
            }
        }

        [Fact]
        public async Task MakeAsync_IgnoresTheOrientationInExif()
        {
            MadeFiles made = await MadeFiles.MakeAsync(HeicFile.Make(exif: TestImages.Exif(6)), DerivedKind.Preview);

            using MagickImage preview = made.Decode(DerivedKind.Preview);
            ImageAssert.Size(preview, 64, 48);
        }

        [Fact]
        public async Task MakeAsync_FailsOnOneCutShort()
        {
            byte[] contents = HeicFile.Make();

            await Assert.ThrowsAnyAsync<MagickException>(() => MadeFiles.MakeAsync(contents[..^60], DerivedKind.Preview));
        }
    }
}
