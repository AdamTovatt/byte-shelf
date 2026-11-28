namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class SizesTests
    {
        [Fact]
        public void Fit_Larger_IsMadeSmallerKeepingItsShape()
        {
            Assert.Equal((1920, 1080), Sizes.Fit(3840, 2160, 1920, 1080, even: true));
        }

        [Fact]
        public void Fit_Smaller_StaysAsItIs()
        {
            Assert.Equal((640, 360), Sizes.Fit(640, 360, 1920, 1080, even: true));
        }

        [Fact]
        public void Fit_ByItsHeight_IsWithinBoth()
        {
            Assert.Equal((240, 320), Sizes.Fit(1080, 1440, 320, 320, even: false));
        }

        /// <summary>H.264's 4:2:0 takes even sizes alone.</summary>
        [Fact]
        public void Fit_Even_IsInEvenPixels()
        {
            Assert.Equal((1000, 564), Sizes.Fit(1001, 563, 1920, 1080, even: true));
        }

        [Fact]
        public void Fit_NotEven_IsInAnyPixels()
        {
            Assert.Equal((1001, 563), Sizes.Fit(1001, 563, 1920, 1080, even: false));
        }

        [Fact]
        public void Fit_Tiny_IsAPixelOrTwoAtLeast()
        {
            Assert.Equal((320, 2), Sizes.Fit(10000, 10, 320, 320, even: true));
        }
    }
}
