using ByteShelf.Server.Storage.Services;

namespace ByteShelf.Server.Tests.Storage
{
    public class StorageOptionsTests
    {
        [Theory]
        [InlineData(null, StorageOptions.DefaultRestoreWindowDays)]
        [InlineData("7", 7)]
        [InlineData("3650", 3650)]
        public void RestoreWindowFrom_UnsetOrDays_IsThatLong(string? days, int expected)
        {
            Assert.Equal(TimeSpan.FromDays(expected), StorageOptions.RestoreWindowFrom(days));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("a week")]
        [InlineData("3651")]
        [InlineData("2147483647")]
        public void RestoreWindowFrom_NotAWholeNumberOfDaysFromOneToTenYears_Throws(string days)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => StorageOptions.RestoreWindowFrom(days));
            Assert.Equal($"BYTESHELF_RESTORE_WINDOW_DAYS must be a whole number of days from 1 to 3650; it is '{days}'.", exception.Message);
        }
    }
}
