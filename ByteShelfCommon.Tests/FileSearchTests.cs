namespace ByteShelfCommon.Tests
{
    public class FileSearchTests
    {
        [Fact]
        public void Normalize_NothingSet_SearchesTheWholeShelfAPageAtATime()
        {
            FileSearch normalized = new FileSearch().Normalize();

            Assert.Equal((ShelfPath.Root, FileSearch.DefaultLimit), (normalized.Under, normalized.Limit));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Normalize_BlankText_IsNoText(string? text)
        {
            Assert.Null(new FileSearch { Text = text }.Normalize().Text);
        }

        [Fact]
        public void Normalize_Text_IsTrimmedComposedAndLowercase()
        {
            Assert.Equal("årsrapport", new FileSearch { Text = " ÅRSRAPPORT " }.Normalize().Text);
        }

        [Fact]
        public void Normalize_UnderAPath_NormalizesIt()
        {
            Assert.Equal("/a/b", new FileSearch { Under = "a/b/" }.Normalize().Under);
        }

        [Theory]
        [InlineData("Image/*", null, "image/")]
        [InlineData("Text/Plain; charset=utf-8", "text/plain", null)]
        public void Normalize_ContentType_IsEitherOneTypeOrAFamily(string contentType, string? exact, string? prefix)
        {
            FileSearch normalized = new FileSearch { ContentType = contentType }.Normalize();

            Assert.Equal((exact, prefix), (normalized.ExactMediaType(), normalized.MediaTypePrefix()));
        }

        [Theory]
        [InlineData("/*")]
        [InlineData("image/png/*")]
        [InlineData("image;x=1/*")]
        [InlineData("not a type")]
        public void Normalize_ContentTypeNotAllowed_Throws(string contentType)
        {
            Assert.Throws<ArgumentException>(() => new FileSearch { ContentType = contentType }.Normalize());
        }

        [Theory]
        [InlineData("text/plain", "TEXT/PLAIN; charset=utf-8", true)]
        [InlineData("text/plain", "text/plainer", false)]
        [InlineData("image/*", "image/png", true)]
        [InlineData("image/*", "imagery/png", false)]
        public void MatchesContentType_ComparesTheTypeWithoutParameters(string asked, string stored, bool matches)
        {
            Assert.Equal(matches, new FileSearch { ContentType = asked }.Normalize().MatchesContentType(stored));
        }

        [Fact]
        public void Normalize_Sha256_IsLowercase()
        {
            string sha256 = new string('A', 64);

            Assert.Equal(new string('a', 64), new FileSearch { Sha256 = sha256 }.Normalize().Sha256);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(FileSearch.MaxLimit + 1)]
        public void Normalize_LimitOutsideItsRange_Throws(int limit)
        {
            Assert.Throws<ArgumentException>(() => new FileSearch { Limit = limit }.Normalize());
        }

        [Fact]
        public void Normalize_NegativeSize_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FileSearch { MaxSizeBytes = -1 }.Normalize());
        }

        [Fact]
        public void Normalize_Twice_IsTheSameAsOnce()
        {
            FileSearch once = new FileSearch { Text = "Å", ContentType = "IMAGE/*", Sha256 = new string('F', 64), Under = "a" }.Normalize();
            FileSearch twice = once.Normalize();

            Assert.Equal((once.Text, once.ContentType, once.Sha256, once.Under, once.Limit), (twice.Text, twice.ContentType, twice.Sha256, twice.Under, twice.Limit));
        }

        [Fact]
        public void ToQuery_EscapesValuesAndLeavesOutWhatIsNotSet()
        {
            FileSearch search = new FileSearch
            {
                Under = "/q&a #1",
                Text = "a+b",
                MinSizeBytes = 10,
                CreatedFrom = new DateTimeOffset(2026, 3, 1, 14, 5, 0, TimeSpan.FromHours(1)),
            };

            Assert.Equal(
                "under=%2Fq%26a%20%231&text=a%2Bb&minSizeBytes=10&createdFrom=2026-03-01T14%3A05%3A00.0000000%2B01%3A00",
                search.ToQuery());
        }
    }
}
