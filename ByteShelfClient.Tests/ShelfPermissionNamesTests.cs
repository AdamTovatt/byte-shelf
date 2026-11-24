using ByteShelfClient.Http;
using ByteShelfCommon;

namespace ByteShelfClient.Tests
{
    /// <summary>The JSON names of a key's permission, written out, as released servers read and write them: none may change.</summary>
    public class ShelfPermissionNamesTests
    {
        private static readonly Guid KeyId = Guid.Parse("20000000-0000-0000-0000-000000000001");

        [Theory]
        [InlineData("read", ShelfPermission.Read)]
        [InlineData("addOnly", ShelfPermission.AddOnly)]
        [InlineData("readWrite", ShelfPermission.ReadWrite)]
        public async Task SetKeyGrantAsync_APermission_IsSentAndReadBackUnderTheReleasedName(string json, ShelfPermission permission)
        {
            string? sent = null;
            using ScriptedServer server = new ScriptedServer(async request =>
            {
                sent = await request.Content!.ReadAsStringAsync();
                return ScriptedServer.Json($$"""
                    {"keyId":"{{KeyId:D}}","shelfId":"10000000-0000-0000-0000-000000000001","shelfName":"docs",
                    "permission":"{{json}}","createdAt":"2026-11-02T00:00:00+00:00","updatedAt":"2026-11-02T00:00:00+00:00"}
                    """);
            });
            using HttpClient http = server.CreateClient();

            ShelfKeyGrantInfo grant = await new ByteShelfHttpClient(http, new ShelfKeyCredentials("secret")).Shelf("docs").SetKeyGrantAsync(KeyId, permission);

            Assert.Equal($$"""{"permission":"{{json}}"}""", sent);
            Assert.Equal(permission, grant.Permission);
        }
    }
}
