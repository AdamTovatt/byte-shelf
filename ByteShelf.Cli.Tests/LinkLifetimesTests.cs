using ByteShelf.Tools;

namespace ByteShelf.Cli.Tests
{
    public sealed class LinkLifetimesTests
    {
        [Theory]
        [InlineData("30m", 30)]
        [InlineData("12H", 12 * 60)]
        [InlineData(" 7d ", 7 * 24 * 60)]
        [InlineData("10080m", 7 * 24 * 60)]
        public void ForDownload_ALifetime_AnswersIt(string text, int minutes)
        {
            Assert.Equal(TimeSpan.FromMinutes(minutes), LinkLifetimes.ForDownload(text));
        }

        [Fact]
        public void ForDownload_None_AnswersNull()
        {
            Assert.Null(LinkLifetimes.ForDownload(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("m")]
        [InlineData("7")]
        [InlineData("1w")]
        [InlineData("1.5h")]
        [InlineData("-1h")]
        [InlineData("0m")]
        [InlineData("8d")]
        [InlineData("10081m")]
        [InlineData("99999999999m")]
        public void ForDownload_NotALifetimeALinkMayHave_ThrowsUsage(string text)
        {
            Assert.Throws<UsageException>(() => LinkLifetimes.ForDownload(text));
        }

        [Theory]
        [InlineData("15m", 15)]
        [InlineData("24h", 24 * 60)]
        [InlineData("1440m", 24 * 60)]
        [InlineData("1d", 24 * 60)]
        public void ForUpload_ALifetime_AnswersIt(string text, int minutes)
        {
            Assert.Equal(TimeSpan.FromMinutes(minutes), LinkLifetimes.ForUpload(text));
        }

        [Theory]
        [InlineData("25h")]
        [InlineData("2d")]
        [InlineData("1441m")]
        public void ForUpload_PastTheMost_ThrowsUsageNamingTheBounds(string text)
        {
            UsageException exception = Assert.Throws<UsageException>(() => LinkLifetimes.ForUpload(text));

            Assert.EndsWith("from 1 minute to 24 hours.", exception.Message, StringComparison.Ordinal);
        }
    }
}
