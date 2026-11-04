namespace ByteShelfCommon.Tests
{
    public class ShelfNameTests
    {
        [Theory]
        [InlineData("backups")]
        [InlineData("bokur2")]
        [InlineData("0day")]
        [InlineData("pg.backups-main_v2")]
        public void TryValidate_AllowedName_ReturnsTrue(string name)
        {
            Assert.True(ShelfName.TryValidate(name, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Backups")]
        [InlineData("-backups")]
        [InlineData(".backups")]
        [InlineData("back ups")]
        [InlineData("back/ups")]
        [InlineData("b\x00e4ckups")]
        public void TryValidate_NameNotAllowed_ReturnsFalseWithReason(string? name)
        {
            Assert.False(ShelfName.TryValidate(name, out string? error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void TryValidate_LongestAllowed_IsAccepted()
        {
            Assert.True(ShelfName.TryValidate(new string('a', ShelfName.MaxLength), out _));
        }

        [Fact]
        public void TryValidate_OneTooLong_IsRefused()
        {
            Assert.False(ShelfName.TryValidate(new string('a', ShelfName.MaxLength + 1), out _));
        }

        [Fact]
        public void Validate_NameNotAllowed_Throws()
        {
            Assert.Throws<ArgumentException>(() => ShelfName.Validate("Nope"));
        }
    }
}
