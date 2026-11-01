namespace ByteShelfCommon.Tests
{
    public class ContentTypesTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void Normalize_None_AnswersTheDefault(string? contentType)
        {
            Assert.Equal(ContentTypes.Default, ContentTypes.Normalize(contentType));
        }

        [Theory]
        [InlineData(" text/plain ", "text/plain")]
        [InlineData("text/plain; charset=utf-8", "text/plain; charset=utf-8")]
        public void Normalize_MediaType_AnswersItTrimmed(string contentType, string expected)
        {
            Assert.Equal(expected, ContentTypes.Normalize(contentType));
        }

        [Theory]
        [InlineData("not a type")]
        [InlineData("text/")]
        public void Normalize_NotAMediaType_Throws(string contentType)
        {
            Assert.Throws<ArgumentException>(() => ContentTypes.Normalize(contentType));
        }

        [Theory]
        [InlineData("/2026/receipt.pdf", "application/pdf")]
        [InlineData("photo.JPG", "image/jpeg")]
        [InlineData("notes.md", "text/markdown")]
        [InlineData("archive.unknown", null)]
        [InlineData("no-extension", null)]
        public void ForFileName_AnswersTheCommonTypeByExtension(string fileName, string? expected)
        {
            Assert.Equal(expected, ContentTypes.ForFileName(fileName));
        }

        [Fact]
        public void Normalize_LongestAllowed_IsAccepted()
        {
            string contentType = "text/" + new string('a', 250);

            Assert.Equal(contentType, ContentTypes.Normalize(contentType));
        }

        [Fact]
        public void Normalize_OneTooLong_Throws()
        {
            Assert.Throws<ArgumentException>(() => ContentTypes.Normalize("text/" + new string('a', 251)));
        }

        [Theory]
        [InlineData("text/plain", "text/plain")]
        [InlineData(" Text/Plain ; charset=UTF-8", "text/plain")]
        public void MediaTypeOf_AnswersTheTypeLowercaseWithoutParameters(string contentType, string expected)
        {
            Assert.Equal(expected, ContentTypes.MediaTypeOf(contentType));
        }
    }
}
