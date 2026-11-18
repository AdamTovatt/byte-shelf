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

        [Fact]
        public void ToQuery_MetadataCriteria_CarriesEachAndHasGpsOnlyWhenAsked()
        {
            FileSearch search = new FileSearch
            {
                TakenBefore = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero),
                HasGps = true,
                MinDurationSeconds = 1.5,
                MaxPageCount = 10,
            };

            Assert.Equal("takenBefore=2024-07-01T00%3A00%3A00.0000000%2B00%3A00&hasGps=true&minDurationSeconds=1.5&maxPageCount=10", search.ToQuery());
            Assert.Equal(string.Empty, new FileSearch { HasGps = false }.ToQuery());
        }

        [Fact]
        public void Normalize_MetadataCriteria_KeepsThem()
        {
            FileSearch search = new FileSearch
            {
                TakenFrom = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
                TakenBefore = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero),
                HasGps = true,
                MinDurationSeconds = 10,
                MaxDurationSeconds = 60,
                MinPageCount = 2,
                MaxPageCount = 5,
            };

            FileSearch normalized = search.Normalize();

            Assert.Equal(
                (search.TakenFrom, search.TakenBefore, true, 10.0, 60.0, 2, 5),
                (normalized.TakenFrom, normalized.TakenBefore, normalized.HasGps, normalized.MinDurationSeconds, normalized.MaxDurationSeconds, normalized.MinPageCount, normalized.MaxPageCount));
            Assert.True(normalized.AsksForMetadata());
        }

        [Fact]
        public void AsksForMetadata_OnlyOtherCriteria_IsFalse()
        {
            Assert.False(new FileSearch { Text = "x", MinSizeBytes = 1, HasGps = false }.Normalize().AsksForMetadata());
        }

        [Theory]
        [InlineData(-1.0, null, null, null)]
        [InlineData(null, null, -1, null)]
        [InlineData(60.0, 10.0, null, null)]
        [InlineData(null, null, 5, 2)]
        [InlineData(double.NaN, null, null, null)]
        [InlineData(null, double.PositiveInfinity, null, null)]
        public void Normalize_DurationOrPagesOutOfRange_Throws(double? minDuration, double? maxDuration, int? minPages, int? maxPages)
        {
            FileSearch search = new FileSearch { MinDurationSeconds = minDuration, MaxDurationSeconds = maxDuration, MinPageCount = minPages, MaxPageCount = maxPages };

            Assert.Throws<ArgumentException>(() => search.Normalize());
        }

        [Fact]
        public void Normalize_TakenRangeEndingBeforeItStarts_Throws()
        {
            DateTimeOffset at = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

            Assert.Throws<ArgumentException>(() => new FileSearch { TakenFrom = at, TakenBefore = at }.Normalize());
        }
    }
}
