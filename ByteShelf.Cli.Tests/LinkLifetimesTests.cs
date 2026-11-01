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
        public void Parse_ALifetime_AnswersIt(string text, int minutes)
        {
            Assert.Equal(TimeSpan.FromMinutes(minutes), LinkLifetimes.Parse(text));
        }

        [Fact]
        public void Parse_None_AnswersNull()
        {
            Assert.Null(LinkLifetimes.Parse(null));
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
        public void Parse_NotALifetimeALinkMayHave_ThrowsArgumentException(string text)
        {
            Assert.Throws<ArgumentException>(() => LinkLifetimes.Parse(text));
        }
    }
}
