namespace ByteShelfCommon.Tests
{
    public sealed class DownloadLinkTests
    {
        [Fact]
        public void NormalizeLifetime_None_IsADay()
        {
            Assert.Equal(TimeSpan.FromHours(24), DownloadLink.Lifetime.Normalize(null));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(90)]
        [InlineData(7 * 24 * 60)]
        public void NormalizeLifetime_WholeMinutesWithinTheBounds_IsKept(int minutes)
        {
            Assert.Equal(TimeSpan.FromMinutes(minutes), DownloadLink.Lifetime.Normalize(TimeSpan.FromMinutes(minutes)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-60)]
        [InlineData(59)]
        [InlineData(61)]
        [InlineData((7 * 24 * 3600) + 60)]
        public void NormalizeLifetime_OutsideTheBoundsOrNotWholeMinutes_ThrowsArgumentException(int seconds)
        {
            Assert.Throws<ArgumentException>(() => DownloadLink.Lifetime.Normalize(TimeSpan.FromSeconds(seconds)));
        }
    }
}
