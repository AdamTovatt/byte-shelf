namespace ByteShelfCommon.Tests
{
    /// <remarks>
    /// Characters outside ASCII are written as <c>\x</c> escapes so the source shows exactly which code
    /// points each case holds; an editor that normalizes text cannot change a test's meaning.
    /// </remarks>
    public class ShelfPathTests
    {
        [Theory]
        [InlineData("/", "/")]
        [InlineData("a", "/a")]
        [InlineData("/a/b", "/a/b")]
        [InlineData("a/b/", "/a/b")]
        [InlineData("/invoices/2026/march.pdf", "/invoices/2026/march.pdf")]
        [InlineData("/with space/r\x00e4ksm\x00f6rg\x00e5s.txt", "/with space/r\x00e4ksm\x00f6rg\x00e5s.txt")]
        public void Normalize_AllowedPath_ReturnsNormalForm(string path, string expected)
        {
            Assert.Equal(expected, ShelfPath.Normalize(path));
        }

        [Fact]
        public void Normalize_DecomposedName_ComposesIt()
        {
            Assert.Equal("/caf\x00e9", ShelfPath.Normalize("/cafe\x0301"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("//")]
        [InlineData("/a//b")]
        [InlineData("/a/./b")]
        [InlineData("/a/../b")]
        [InlineData("/a\\b")]
        [InlineData("/a\x0000b")]
        [InlineData("/a/b//")]
        [InlineData("/a\x200bb")]
        [InlineData("/\x202eexe.txt")]
        [InlineData("/a\x2028b")]
        public void TryNormalize_PathNotAllowed_ReturnsFalseWithReason(string path)
        {
            bool allowed = ShelfPath.TryNormalize(path, out string? normalized, out string? error);

            Assert.False(allowed);
            Assert.Null(normalized);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void TryNormalize_LoneSurrogates_AreRefused()
        {
            // Built in code: attribute arguments are stored as UTF-8, which turns a lone surrogate into
            // U+FFFD before the test ever sees it.
            string highAlone = "/a" + (char)0xD800 + "b";
            string lowAlone = "/a" + (char)0xDC00;

            Assert.False(ShelfPath.TryNormalize(highAlone, out _, out _));
            Assert.False(ShelfPath.TryNormalize(lowAlone, out _, out _));
        }

        [Fact]
        public void TryValidateName_LongestAllowed_IsAccepted()
        {
            Assert.True(ShelfPath.TryValidateName(new string('a', ShelfPath.MaxNameLength), out _));
        }

        [Fact]
        public void TryValidateName_OneTooLong_IsRefused()
        {
            Assert.False(ShelfPath.TryValidateName(new string('a', ShelfPath.MaxNameLength + 1), out _));
        }

        [Fact]
        public void TryNormalize_NameLongerOnceComposed_IsRefused()
        {
            // U+0958 is excluded from composition, so form C writes it as two characters.
            string name = new string('\x0958', ShelfPath.MaxNameLength);

            Assert.True(ShelfPath.TryValidateName(name, out _));
            Assert.False(ShelfPath.TryNormalize("/" + name, out _, out _));
        }

        [Fact]
        public void TryNormalize_LongestPathWithoutLeadingSeparator_IsRefusedOnceTheSeparatorIsAdded()
        {
            string longest = string.Join(ShelfPath.Separator, Enumerable.Repeat(new string('a', 250), 16)) + "/" + new string('a', 80);

            Assert.Equal(ShelfPath.MaxPathLength, longest.Length);
            Assert.True(ShelfPath.TryNormalize("/" + longest[1..], out _, out _));
            Assert.False(ShelfPath.TryNormalize(longest, out _, out _));
        }

        [Fact]
        public void TryValidateName_SurrogatePair_IsAccepted()
        {
            Assert.True(ShelfPath.TryValidateName("photo-\xd83d\xdcf7.jpg", out _));
        }

        [Theory]
        [InlineData("/a", "/", "a")]
        [InlineData("/a/b/c.txt", "/a/b", "c.txt")]
        public void GetParentAndGetName_SplitTheLastName(string path, string parent, string name)
        {
            Assert.Equal(parent, ShelfPath.GetParent(path));
            Assert.Equal(name, ShelfPath.GetName(path));
            Assert.Equal(path, ShelfPath.Combine(parent, name));
        }

        [Fact]
        public void GetNames_Root_ReturnsEmpty()
        {
            Assert.Empty(ShelfPath.GetNames("/"));
        }

        [Fact]
        public void GetNames_NestedPath_ReturnsEachNameOutermostFirst()
        {
            Assert.Equal(new[] { "a", "b" }, ShelfPath.GetNames("/a/b"));
        }

        [Fact]
        public void GetParent_Root_Throws()
        {
            Assert.Throws<ArgumentException>(() => ShelfPath.GetParent("/"));
        }
    }
}
