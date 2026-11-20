namespace ByteShelfCommon.Tests
{
    public class ShelfKeysTests
    {
        [Theory]
        [InlineData(ShelfKeyPermission.Read)]
        [InlineData(ShelfKeyPermission.AddOnly)]
        [InlineData(ShelfKeyPermission.ReadWrite)]
        public void ValidatePermission_OneOfTheValues_IsAccepted(ShelfKeyPermission permission)
        {
            ShelfKeys.ValidatePermission(permission);
        }

        [Fact]
        public void ValidatePermission_NoneOfTheValues_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => ShelfKeys.ValidatePermission((ShelfKeyPermission)42));

            Assert.Equal("permission", exception.ParamName);
        }
    }
}
