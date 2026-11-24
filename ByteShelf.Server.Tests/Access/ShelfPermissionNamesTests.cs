using System.Data.Common;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Access
{
    /// <summary>
    /// The names a key's permission has in JSON, in the database and in the change log, written out, as
    /// released servers, clients and replicas read and write them: none may change.
    /// </summary>
    public class ShelfPermissionNamesTests : IAsyncLifetime
    {
        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "docs" })).EnsureSuccessStatusCode();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Theory]
        [InlineData("read", "read")]
        [InlineData("addOnly", "add_only")]
        [InlineData("readWrite", "read_write")]
        public async Task AKey_CreatedWithAReleasedClientsJson_IsStoredLoggedAndAnsweredUnderTheReleasedNames(string json, string column)
        {
            HttpResponseMessage created = await _host.AdminClient.PostAsync(
                "/api/shelves/docs/keys", new StringContent($$"""{"name":"app","permission":"{{json}}"}""", Encoding.UTF8, "application/json"));
            created.EnsureSuccessStatusCode();

            using JsonDocument answer = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            Assert.Equal(json, answer.RootElement.GetProperty("key").GetProperty("grants")[0].GetProperty("permission").GetString());
            Assert.Equal(column, await ScalarAsync("SELECT permission FROM shelf_key_grant"));
            using JsonDocument logged = JsonDocument.Parse((await ScalarAsync("SELECT payload FROM change_log WHERE entity_type = 'shelf_key_grant' ORDER BY sequence DESC LIMIT 1"))!);
            Assert.Equal(json, logged.RootElement.GetProperty("permission").GetString());
        }

        [Theory]
        [InlineData("read", "read", ShelfPermission.Read)]
        [InlineData("add_only", "addOnly", ShelfPermission.AddOnly)]
        [InlineData("read_write", "readWrite", ShelfPermission.ReadWrite)]
        public async Task AKey_StoredByAReleasedServer_IsReadBackAndSaysWhatItMayDo(string column, string json, ShelfPermission expected)
        {
            CreatedShelfKey created = await _host.CreateKeyAsync("docs", ShelfPermission.Read);
            await ScalarAsync($"UPDATE shelf_key_grant SET permission = '{column}'");

            using JsonDocument grants = JsonDocument.Parse(await _host.AdminClient.GetStringAsync($"/api/keys/{created.Key.Id}/grants"));
            using HttpClient key = _host.CreateKeyClient(created.Secret);
            using JsonDocument shelf = JsonDocument.Parse(await key.GetStringAsync("/api/shelves/docs"));

            Assert.Equal(json, grants.RootElement[0].GetProperty("permission").GetString());
            Assert.Equal(json, shelf.RootElement.GetProperty("permission").GetString());
            Assert.Equal(expected, (await key.GetFromJsonAsync<ShelfInfo>("/api/shelves/docs"))?.Permission);
        }

        [Theory]
        [InlineData("read", ShelfPermission.Read)]
        [InlineData("addOnly", ShelfPermission.AddOnly)]
        [InlineData("readWrite", ShelfPermission.ReadWrite)]
        public void AGrant_LoggedByAReleasedPrimary_IsReadBack(string json, ShelfPermission expected)
        {
            string payload = $$"""
                {"id":"20000000-0000-0000-0000-000000000001","keyId":"20000000-0000-0000-0000-000000000001","shelfId":"10000000-0000-0000-0000-000000000001",
                "permission":"{{json}}","createdAt":"2026-11-02T00:00:00+00:00","updatedAt":"2026-11-02T00:00:00+00:00"}
                """;

            Assert.Equal(expected, ChangePayload.Deserialize<ShelfKeyGrant>(payload).Permission);
        }

        private async Task<string?> ScalarAsync(string sql)
        {
            await using DbConnection connection = await _host.TestRoot.DataSource.OpenConnectionAsync();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return (string?)await command.ExecuteScalarAsync();
        }
    }
}
