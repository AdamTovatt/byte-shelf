namespace ByteShelfCommon.Tests
{
    public class ShelfKeysTests
    {
        [Theory]
        [InlineData(ShelfPermission.Read)]
        [InlineData(ShelfPermission.AddOnly)]
        [InlineData(ShelfPermission.ReadWrite)]
        public void ValidatePermission_OneOfTheValues_IsAccepted(ShelfPermission permission)
        {
            ShelfKeys.ValidatePermission(permission);
        }

        [Fact]
        public void ValidatePermission_NoneOfTheValues_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => ShelfKeys.ValidatePermission((ShelfPermission)42));

            Assert.Equal("permission", exception.ParamName);
        }
    }
}
