namespace ByteShelfCommon.Tests
{
    public class FileDescriptionTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t\n ")]
        public void Normalize_NothingLeft_IsNoDescription(string? description)
        {
            Assert.Null(FileDescription.Normalize(description));
        }

        [Fact]
        public void Normalize_Text_IsTrimmedAndComposed()
        {
            Assert.Equal("Årsrapport", FileDescription.Normalize("  Årsrapport\n"));
        }

        [Fact]
        public void Normalize_AtTheLimitInCodePoints_IsKept()
        {
            string longest = string.Concat(Enumerable.Repeat("😀", FileDescription.MaxLength));

            Assert.Equal(longest, FileDescription.Normalize(longest));
        }

        [Fact]
        public void Normalize_OneOverTheLimit_Throws()
        {
            Assert.Throws<ArgumentException>(() => FileDescription.Normalize(new string('a', FileDescription.MaxLength + 1)));
        }

        [Fact]
        public void Normalize_NotValidUnicode_Throws()
        {
            Assert.Throws<ArgumentException>(() => FileDescription.Normalize("a\uD800b"));
        }
    }
}
