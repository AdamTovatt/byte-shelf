using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    [CollectionDefinition(nameof(LargestImageCollection), DisableParallelization = true)]
    public sealed class LargestImageCollection
    {
    }

    /// <summary>Images that take most of the memory there is, of which other tests then have none.</summary>
    [Collection(nameof(LargestImageCollection))]
    public sealed class LargestImageTests
    {
        [Fact]
        public async Task MakeAsync_MakesAThumbnailOfTheMostPixels()
        {
            MadeFiles made = await MadeFiles.MakeAsync(TestImages.BlackPng(12_000, 10_000), DerivedKind.Thumbnail);

            MagickImageInfo thumbnail = new MagickImageInfo(made.BytesOf(DerivedKind.Thumbnail));
            Assert.Equal((320u, 267u), (thumbnail.Width, thumbnail.Height));
        }

        /// <summary>Four bytes a pixel, and a copy of them as it is turned: what there is memory for, and no more.</summary>
        [Fact]
        public async Task MakeAsync_TurnsTheMostPixels()
        {
            MadeFiles made = await MadeFiles.MakeAsync(TestImages.BlankTiff(12_000, 10_000, orientation: 6), DerivedKind.Full);

            // Read without decoding its pixels, which there is no memory for.
            MagickImageInfo full = new MagickImageInfo(made.BytesOf(DerivedKind.Full));
            Assert.Equal((10_000u, 12_000u), (full.Width, full.Height));
        }
    }
}
