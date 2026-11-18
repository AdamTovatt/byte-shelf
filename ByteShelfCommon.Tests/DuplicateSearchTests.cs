namespace ByteShelfCommon.Tests
{
    public class DuplicateSearchTests
    {
        private static readonly string Sha256 = new string('a', 64);

        [Fact]
        public void Cursor_WrittenThenParsed_IsTheSameCursor()
        {
            DuplicateCursor cursor = new DuplicateCursor(5, Sha256);

            Assert.Equal(cursor, DuplicateCursor.Parse(cursor.ToString()));
        }

        [Theory]
        [InlineData("/a.txt")]
        [InlineData("5")]
        [InlineData("-1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [InlineData("5:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("5:abc")]
        public void Cursor_NotOneAPageEndedWith_ThrowsArgumentException(string text)
        {
            Assert.False(DuplicateCursor.TryParse(text, out _));
            Assert.Throws<ArgumentException>(() => DuplicateCursor.Parse(text));
        }

        [Fact]
        public void Normalize_KeepsTheCursorAndChecksTheCriteria()
        {
            DuplicateSearch normalized = new DuplicateSearch { Text = " Å ", After = new DuplicateCursor(5, Sha256) }.Normalize();

            Assert.Equal(("å", DuplicateSearch.DefaultLimit, new DuplicateCursor(5, Sha256)), (normalized.Text, normalized.Limit, normalized.After));
        }

        [Fact]
        public void ToQuery_WritesTheCursorAsItIsParsed()
        {
            DuplicateSearch search = new DuplicateSearch { Under = "/a b", Limit = 2, After = new DuplicateCursor(5, Sha256) };

            Assert.Equal($"under=%2Fa%20b&limit=2&after=5%3A{Sha256}", search.ToQuery());
        }

        [Fact]
        public void Normalize_MetadataCriteria_AreCheckedAndKept()
        {
            DuplicateSearch normalized = new DuplicateSearch { HasGps = true, MinPageCount = 2 }.Normalize();

            Assert.Equal((true, 2), (normalized.HasGps, normalized.MinPageCount));
            Assert.Throws<ArgumentException>(() => new DuplicateSearch { MinPageCount = -1 }.Normalize());
        }

        [Fact]
        public void ToQuery_MetadataCriteria_AreWritten()
        {
            DuplicateSearch search = new DuplicateSearch { HasGps = true, MaxDurationSeconds = 1.5 };

            Assert.Equal("hasGps=true&maxDurationSeconds=1.5", search.ToQuery());
        }
    }
}
