using System.Data.Common;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>What the database itself holds of plugins' documents, whatever writes them, and the ids every server must make alike.</summary>
    public class PluginMetadataTableTests : ServiceTestBase
    {
        private const string FileId = "(SELECT id FROM file)";

        [Theory]
        [InlineData("'{\"a\":1}'", true)]
        [InlineData("NULL", true)]
        [InlineData("'{'", false)]
        [InlineData("'[]'", false)]
        [InlineData("'null'", false)]
        [InlineData("'{\"t\":\"' || replace(hex(zeroblob(32764)), '00', 'xx') || '\"}'", true)]
        [InlineData("'{\"t\":\"' || replace(hex(zeroblob(32764)), '00', 'xx') || 'x\"}'", false)]
        [InlineData("'{\"t\":\"' || replace(hex(zeroblob(32764)), '00', 'åå') || '\"}'", false)]
        public async Task Document_IsAnObjectOfAtMost64KiBOrNull(string document, bool taken)
        {
            await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");

            await AssertTakenAsync(taken, $"INSERT INTO content_plugin_metadata (id, plugin_id, sha256, reader_version, read_at, document) VALUES ('a', 'p', '{new string('a', 64)}', 1, '', {document})");
            await AssertTakenAsync(taken, $"INSERT INTO file_plugin_metadata (id, plugin_id, file_id, version, document) VALUES ('a', 'p', {FileId}, 1, {document})");
        }

        /// <summary>Pinned: a primary and its replicas make these ids apart, and each must name the same row.</summary>
        [Fact]
        public void Ids_AreTheSameOnEveryVersion()
        {
            Assert.Equal(
                new Guid("a26d962f-19f0-8900-2410-8e4083134d26"),
                ContentPluginMetadata.IdFor("test-plugin", new string('a', 64)));
            Assert.Equal(
                new Guid("39827677-fde6-d12b-c14f-5d84a0bccb3d"),
                FilePluginMetadata.IdFor("test-plugin", new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")));
        }

        private async Task AssertTakenAsync(bool taken, string insert)
        {
            Task<int> write = Services.InTransactionAsync((_, session) => session.Connection.ExecuteAsync(insert, transaction: session.Transaction));
            if (taken)
            {
                Assert.Equal(1, await write);
            }
            else
            {
                await Assert.ThrowsAnyAsync<DbException>(() => write);
            }
        }
    }
}
