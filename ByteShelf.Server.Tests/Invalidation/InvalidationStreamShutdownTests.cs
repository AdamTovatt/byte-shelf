using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient.Http;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.Invalidation
{
    /// <summary>
    /// An open stream as the server stops, over real HTTP: Kestrel waits for open requests when it stops,
    /// which the in-memory test server does not.
    /// </summary>
    public sealed class InvalidationStreamShutdownTests : IAsyncLifetime
    {
        /// <summary>Well under the host's 30-second shutdown timeout, which a stop held by the stream takes in full.</summary>
        private static readonly TimeSpan PromptStop = TimeSpan.FromSeconds(5);

        private readonly RecordingLoggerProvider _warnings = new RecordingLoggerProvider(message => message.Level >= LogLevel.Warning);
        private KestrelTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await KestrelTestHost.StartAsync(builder => builder.Logging.AddProvider(_warnings));
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task Stop_WithAStreamOpen_EndsPromptlyWithTheStreamsResponseComplete()
        {
            using UserSession session = await UserSession.SignInAsync(_host.Http, ServerTestHost.AdminEmail, ServerTestHost.Password);
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "api/invalidation/stream");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Tokens.Token);
            using HttpResponseMessage response = await _host.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using StreamReader reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            Task<string> rest = reader.ReadToEndAsync();
            // Starting warns of what a test root lacks, as a mail provider, which is not the stop's doing.
            _warnings.Messages.Clear();

            Stopwatch stop = Stopwatch.StartNew();
            await _host.StopAsync();
            stop.Stop();

            Assert.True(stop.Elapsed < PromptStop, $"The stop took {stop.Elapsed}.");
            // A response cut off rather than ended throws here instead of reading to its end.
            Assert.Equal(string.Empty, await rest.WaitAsync(PromptStop));
            // A stream left to fail as the server stops still ends, as the exception middleware swallows what
            // a started response throws, but logs a warning at every stop.
            Assert.Empty(_warnings.Messages);
        }
    }
}
