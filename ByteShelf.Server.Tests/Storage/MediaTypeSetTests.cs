using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Tests.TestUtilities;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Tests.Storage
{
    public class MediaTypeSetTests : ServiceTestBase
    {
        [Theory]
        [InlineData("image/png", true)]
        [InlineData("image/svg+xml", true)]
        [InlineData("application/pdf", true)]
        [InlineData("application/pdfx", false)]
        [InlineData("imagex/png", false)]
        [InlineData("text/plain", false)]
        public void Includes_ASingleTypeOrAFamily(string mediaType, bool included)
        {
            Assert.Equal(included, MediaTypeSet.Of("image/*", "application/pdf").Includes(mediaType));
        }

        [Theory]
        [InlineData("image")]
        [InlineData("*/*")]
        [InlineData("*")]
        [InlineData("Image/png")]
        [InlineData("image/p*")]
        [InlineData("text/plain; charset=utf-8")]
        [InlineData("text/plain' OR '1'='1")]
        [InlineData("text/plain\n")]
        [InlineData("")]
        public void Of_SomethingElse_IsRefused(string pattern)
        {
            Assert.Throws<ArgumentException>(() => MediaTypeSet.Of(pattern));
        }

        [Theory]
        [InlineData("image/png", 1)]
        [InlineData("application/pdf", 1)]
        [InlineData("application/pdfx", 0)]
        [InlineData("text/plain", 0)]
        public async Task PlainContentsOneOf_IsTrueOfWhatTheSetIncludes(string mediaType, long expected)
        {
            Assert.Equal(expected, await EvaluateAsync(MediaTypeSet.Of("image/*", "application/pdf"), mediaType));
            Assert.Equal(0, await EvaluateAsync(MediaTypeSet.Of(), mediaType));
        }

        private Task<long> EvaluateAsync(MediaTypeSet set, string mediaType)
        {
            return Services.InTransactionAsync((_, session) => session.Connection.QuerySingleAsync<long>(
                $"SELECT {PlainContents.OneOf(set, $"'{mediaType}'")}", transaction: session.Transaction));
        }
    }
}
