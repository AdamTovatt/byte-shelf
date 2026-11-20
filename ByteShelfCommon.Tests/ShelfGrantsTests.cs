namespace ByteShelfCommon.Tests
{
    public class ShelfGrantsTests
    {
        [Theory]
        [InlineData(ShelfAccessPermission.Read)]
        [InlineData(ShelfAccessPermission.ReadWrite)]
        public void ValidatePermission_OneOfTheValues_IsAccepted(ShelfAccessPermission permission)
        {
            ShelfGrants.ValidatePermission(permission);
        }

        [Fact]
        public void ValidatePermission_NoneOfTheValues_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => ShelfGrants.ValidatePermission((ShelfAccessPermission)42));

            Assert.Equal("permission", exception.ParamName);
        }
    }
}
