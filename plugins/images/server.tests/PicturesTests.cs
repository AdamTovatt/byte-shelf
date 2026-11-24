using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    public sealed class PicturesTests
    {
        /// <summary>ImageMagick does not say that a stream was full, and what is written by then would be kept as all of it.</summary>
        [Theory]
        [InlineData(MagickFormat.Jpeg)]
        [InlineData(MagickFormat.WebP)]
        public void Write_ToAStreamThatIsFull_ThrowsWhatItThrew(MagickFormat format)
        {
            using MagickImage image = TestImages.Noise(400, 400);
            using LimitedStream full = new LimitedStream(10_000);

            IOException failure = Assert.Throws<IOException>(() => Pictures.Write(image, format, 90, full));

            Assert.Equal("A derived file may take 10000 bytes.", failure.Message);
        }

        [Theory]
        [InlineData(MagickFormat.Jpeg)]
        [InlineData(MagickFormat.WebP)]
        public void Write_WritesWhatEncodeAnswers(MagickFormat format)
        {
            using MagickImage image = TestImages.Noise(400, 400);
            using LimitedStream written = new LimitedStream(long.MaxValue);

            Pictures.Write(image, format, 90, written);

            Assert.Equal(Pictures.Encode(image, format, 90), written.ToArray());
        }
    }
}
