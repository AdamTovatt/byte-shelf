using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.Access
{
    /// <summary>
    /// A shelf key sent as the bearer token belongs to the key scheme, so the session scheme passes over
    /// it rather than failing to read it as a token and logging that on every MCP call.
    /// </summary>
    public sealed class BearerKeyLoggingTests : IAsyncLifetime
    {
        /// <summary>What the session scheme logs about a token it could not read.</summary>
        private readonly RecordingLoggerProvider _logs = new RecordingLoggerProvider(
            message => message.Category == typeof(JwtBearerHandler).FullName
                && (message.Exception != null || message.Text.Contains("fail", StringComparison.OrdinalIgnoreCase)));
        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync(services =>
            {
                services.AddSingleton<ILoggerProvider>(_logs);
                services.Configure<LoggerFilterOptions>(options => options.AddFilter<RecordingLoggerProvider>(typeof(JwtBearerHandler).FullName, LogLevel.Trace));
            });
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "docs" })).EnsureSuccessStatusCode();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task KeyAsBearerToken_SessionSchemeLogsNoFailure()
        {
            HttpResponseMessage created = await _host.AdminClient.PostAsJsonAsync(
                "/api/shelves/docs/keys", new CreateShelfKeyRequest { Name = "agent", Permission = ShelfPermission.Read });
            string secret = (await created.Content.ReadFromJsonAsync<CreatedShelfKey>())!.Secret;

            HttpStatusCode withKey = await GetEntriesAsync(secret);
            int afterKey = _logs.Messages.Count;
            HttpStatusCode withBadToken = await GetEntriesAsync("not.a.token");

            Assert.Equal((HttpStatusCode.OK, 0), (withKey, afterKey));

            // The same request with a token the session scheme does read logs its failure, so the capture works.
            Assert.Equal(HttpStatusCode.Unauthorized, withBadToken);
            Assert.NotEmpty(_logs.Messages);
        }

        private async Task<HttpStatusCode> GetEntriesAsync(string bearer)
        {
            using HttpClient client = _host.CreateClient(bearer);
            return (await client.GetAsync("/api/shelves/docs/entries")).StatusCode;
        }
    }
}
