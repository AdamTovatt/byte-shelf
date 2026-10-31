namespace ByteShelfCommon.Tests
{
    public class ShelfSettingsTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData(0L)]
        [InlineData(1024L)]
        public void Validate_QuotaNoneOrNotNegative_IsAccepted(long? quotaBytes)
        {
            ShelfSettings.Validate("docs", quotaBytes);
        }

        [Fact]
        public void Validate_NegativeQuota_Throws()
        {
            Assert.Throws<ArgumentException>(() => ShelfSettings.Validate("docs", -1));
        }

        [Fact]
        public void Validate_NameNotAllowed_Throws()
        {
            Assert.Throws<ArgumentException>(() => ShelfSettings.Validate("Docs", null));
        }
    }
}
