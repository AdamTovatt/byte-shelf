using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
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
        private readonly CapturingLoggerProvider _logs = new CapturingLoggerProvider();
        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync(services =>
            {
                services.AddSingleton<ILoggerProvider>(_logs);
                services.Configure<LoggerFilterOptions>(options => options.AddFilter<CapturingLoggerProvider>(typeof(JwtBearerHandler).FullName, LogLevel.Trace));
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
                "/api/shelves/docs/keys", new CreateShelfKeyRequest { Name = "agent", Permission = ShelfKeyPermission.Read });
            string secret = (await created.Content.ReadFromJsonAsync<CreatedShelfKey>())!.Secret;

            HttpStatusCode withKey = await GetEntriesAsync(secret);
            int afterKey = _logs.Failures;
            HttpStatusCode withBadToken = await GetEntriesAsync("not.a.token");

            Assert.Equal((HttpStatusCode.OK, 0), (withKey, afterKey));

            // The same request with a token the session scheme does read logs its failure, so the capture works.
            Assert.Equal(HttpStatusCode.Unauthorized, withBadToken);
            Assert.True(_logs.Failures > 0);
        }

        private async Task<HttpStatusCode> GetEntriesAsync(string bearer)
        {
            using HttpClient client = _host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            return (await client.GetAsync("/api/shelves/docs/entries")).StatusCode;
        }

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            private readonly ConcurrentQueue<string> _failures = new ConcurrentQueue<string>();

            /// <summary>How many messages the session scheme logged about a token it could not read.</summary>
            public int Failures => _failures.Count;

            public ILogger CreateLogger(string categoryName)
            {
                return categoryName == typeof(JwtBearerHandler).FullName ? new Logger(_failures) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            }

            public void Dispose()
            {
            }

            private sealed class Logger : ILogger
            {
                private readonly ConcurrentQueue<string> _failures;

                public Logger(ConcurrentQueue<string> failures)
                {
                    _failures = failures;
                }

                public IDisposable? BeginScope<TState>(TState state)
                    where TState : notnull
                {
                    return null;
                }

                public bool IsEnabled(LogLevel logLevel)
                {
                    return true;
                }

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    string message = formatter(state, exception);
                    if (exception != null || message.Contains("fail", StringComparison.OrdinalIgnoreCase))
                    {
                        _failures.Enqueue(message);
                    }
                }
            }
        }
    }
}
