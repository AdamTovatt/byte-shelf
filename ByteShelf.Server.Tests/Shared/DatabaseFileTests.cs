using System.Data.Common;
using System.Text;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database.Mapping;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>What every connection to a database has, and that what is deleted leaves the file.</summary>
    public sealed class DatabaseFileTests : ServiceTestBase
    {
        [Fact]
        public async Task Open_EveryWay_OverwritesWhatIsDeletedAndChecksForeignKeys()
        {
            DatabaseFile unpooled = new DatabaseFile(TestRoot.Root.DatabasePath, pooling: false);

            await AssertSettingsAsync(await TestRoot.DataSource.OpenConnectionAsync());
            await AssertSettingsAsync(TestRoot.DataSource.OpenConnection());
            await AssertSettingsAsync(await TestRoot.Root.Database.OpenConnectionAsync());
            await AssertSettingsAsync(TestRoot.Root.Database.OpenConnection());
            await AssertSettingsAsync(await unpooled.OpenConnectionAsync());
        }

        /// <summary>A pooled connection left without the settings goes back to the pool so.</summary>
        [Fact]
        public async Task Open_FromThePool_OverwritesWhatIsDeletedAndChecksForeignKeys()
        {
            // Held together, so they are three.
            DbConnection[] first = [await TestRoot.DataSource.OpenConnectionAsync(), await TestRoot.DataSource.OpenConnectionAsync(), await TestRoot.DataSource.OpenConnectionAsync()];
            object?[] handles = first.Select(HandleOf).ToArray();
            foreach (DbConnection connection in first)
            {
                await connection.ExecuteAsync("PRAGMA secure_delete = OFF; PRAGMA foreign_keys = OFF");
                await connection.DisposeAsync();
            }

            DbConnection[] again = [await TestRoot.DataSource.OpenConnectionAsync(), await TestRoot.DataSource.OpenConnectionAsync(), await TestRoot.DataSource.OpenConnectionAsync()];

            Assert.Equal(handles.ToHashSet(), again.Select(HandleOf).ToHashSet());
            foreach (DbConnection connection in again)
            {
                await AssertSettingsAsync(connection);
            }
        }

        [Fact]
        public async Task Open_TheSameConnectionAgain_OverwritesWhatIsDeletedAndChecksForeignKeys()
        {
            await using DbConnection connection = await TestRoot.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("PRAGMA secure_delete = OFF; PRAGMA foreign_keys = OFF");
            await connection.CloseAsync();

            await connection.OpenAsync();

            await AssertSettingsAsync(connection);
        }

        [Fact]
        public async Task EncryptInPlace_WhatWasReadFromThePlainContents_IsNotInTheDatabaseFile()
        {
            const string camera = "RecognisableCamera";
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreReadPhotoAsync(shelf, camera);

            // On pages that are their table's only one: a split page may keep a copy of a row it moved, which
            // is SQLite's (DEPLOYMENT.md, Deleted data) and would make this test depend on where rows fall.
            List<long> pages = await PagesHoldingAsync(camera);
            Assert.NotEmpty(pages);
            Assert.Empty(pages.Except(await RootPagesAsync()));

            await EncryptInPlaceAsync(shelf, photo);

            Assert.Empty(await PagesHoldingAsync(camera));
        }

        /// <summary>The pages a long value is on beside its row are freed with it, which overwrites them too.</summary>
        [Fact]
        public async Task EncryptInPlace_WhatAPluginReadOnPagesOfItsOwn_IsNotInTheDatabaseFile()
        {
            const string end = "RecognisableEnd";
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "plain contents");
            Assert.True(await Services.RecordContentDocumentAsync(file.Sha256, $"{{\"text\":\"{new string('x', 20_000)}{end}\"}}"));
            List<long> pages = await PagesHoldingAsync(end);
            Assert.NotEmpty(pages);
            Assert.Empty(pages.Intersect(await RootPagesAsync()));

            await EncryptInPlaceAsync(shelf, file);

            Assert.Empty(await PagesHoldingAsync(end));
        }

        private static async Task AssertSettingsAsync(DbConnection connection)
        {
            Assert.Equal(1L, await connection.ExecuteScalarAsync<long>("PRAGMA secure_delete"));
            Assert.Equal(1L, await connection.ExecuteScalarAsync<long>("PRAGMA foreign_keys"));
            await connection.DisposeAsync();
        }

        /// <summary>SQLite's own connection under <paramref name="connection"/>.</summary>
        private static object? HandleOf(DbConnection connection)
        {
            return ((SqliteConnection)connection).Handle;
        }

        /// <summary>The database file's pages with <paramref name="text"/> on them, once everything in the write-ahead log is written into it.</summary>
        private async Task<List<long>> PagesHoldingAsync(string text)
        {
            await using DbConnection connection = await TestRoot.DataSource.OpenConnectionAsync();
            long pageSize = await connection.ExecuteScalarAsync<long>("PRAGMA page_size");

            // Its first column is whether a reader held it up.
            Assert.Equal(0L, await connection.ExecuteScalarAsync<long>("PRAGMA wal_checkpoint(TRUNCATE)"));
            byte[] file = await File.ReadAllBytesAsync(TestRoot.Root.DatabasePath);
            return file.Chunk((int)pageSize)
                .Select((page, index) => (Number: index + 1L, Text: Encoding.Latin1.GetString(page)))
                .Where(page => page.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Select(page => page.Number)
                .ToList();
        }

        private async Task<IReadOnlyList<long>> RootPagesAsync()
        {
            await using DbConnection connection = await TestRoot.DataSource.OpenConnectionAsync();
            return (await connection.QueryAsync<long>("SELECT rootpage FROM sqlite_master")).ToList();
        }
    }
}
