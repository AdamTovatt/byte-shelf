using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ByteShelf.Server.Invalidation.Controllers;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Invalidation.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Invalidation
{
    /// <summary><c>GET /api/invalidation/stream</c> as the pipeline serves it: its format, its limit, its heartbeat and who hears what.</summary>
    public sealed class InvalidationStreamTests : IAsyncLifetime
    {
        private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task Stream_Open_AnswersAnUnbufferedEventStreamAtOnce()
        {
            using HttpResponseMessage response = await OpenAsync(_host.MemberClient);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no", Assert.Single(response.Headers.GetValues("X-Accel-Buffering")));
            Assert.True(response.Headers.CacheControl?.NoCache);
        }

        [Fact]
        public async Task Stream_AChange_IsOneCamelCaseDataLineFollowedByABlankLine()
        {
            using HttpResponseMessage response = await OpenAsync(_host.AdminClient);
            using StreamReader reader = new StreamReader(await response.Content.ReadAsStreamAsync());

            ShelfInfo shelf = await CreateShelfAsync("docs");

            string line = await ReadLineAsync(reader);
            while (!line.Contains(shelf.Id.ToString("D"), StringComparison.Ordinal))
            {
                line = await ReadLineAsync(reader);
            }

            Assert.StartsWith("data: ", line, StringComparison.Ordinal);
            using JsonDocument data = JsonDocument.Parse(line["data: ".Length..]);
            Assert.Equal(shelf.Id.ToString("D"), data.RootElement.GetProperty("metadata").GetString());
            Assert.Contains(data.RootElement.GetProperty("resource").GetString(), new[] { InvalidationResources.ShelfContents, InvalidationResources.ShelfAccess });
            Assert.Equal(string.Empty, await ReadLineAsync(reader));
        }

        [Fact]
        public async Task Stream_PastTheLimit_Answers429ToThatUserOnly()
        {
            List<HttpResponseMessage> open = new List<HttpResponseMessage>();
            try
            {
                for (int stream = 0; stream < InMemoryInvalidationBroadcaster.MaxStreamsPerCaller; stream++)
                {
                    open.Add(await OpenAsync(_host.MemberClient));
                }

                using HttpResponseMessage refused = await OpenAsync(_host.MemberClient);
                using HttpResponseMessage another = await OpenAsync(_host.AdminClient);

                Assert.All(open, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
                Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
                Assert.False(string.IsNullOrEmpty((await refused.Content.ReadFromJsonAsync<ErrorResponse>())?.Message));
                Assert.Equal(HttpStatusCode.OK, another.StatusCode);
            }
            finally
            {
                open.ForEach(response => response.Dispose());
            }
        }

        [Fact]
        public async Task Stream_ShelfTheUserCannotSee_IsNeverNamed()
        {
            ShelfInfo theirs = await CreateShelfAsync("theirs");
            ShelfInfo mine = await CreateShelfAsync("mine");
            await _host.GrantMemberAsync("mine", ShelfAccessPermission.Read);

            using HttpResponseMessage memberResponse = await OpenAsync(_host.MemberClient);
            using HttpResponseMessage adminResponse = await OpenAsync(_host.AdminClient);
            using StreamReader member = new StreamReader(await memberResponse.Content.ReadAsStreamAsync());
            using StreamReader admin = new StreamReader(await adminResponse.Content.ReadAsStreamAsync());

            await PutAsync("theirs", "/a.txt");
            await PutAsync("mine", "/a.txt");

            // Events go out in change-log order, and the publisher may still be sending the setup's when
            // the streams open, so the reads end at a shelf made after the uploads: everything before it,
            // the upload to their shelf included, has been sent by then.
            ShelfInfo end = await CreateShelfAsync("end");
            await _host.GrantMemberAsync("end", ShelfAccessPermission.Read);
            IReadOnlyList<string> heardByMember = await ReadUntilAsync(member, end.Id);
            IReadOnlyList<string> heardByAdmin = await ReadUntilAsync(admin, end.Id);

            Assert.DoesNotContain(heardByMember, line => line.Contains(theirs.Id.ToString("D"), StringComparison.Ordinal));
            Assert.Contains(heardByMember, line => line.Contains(mine.Id.ToString("D"), StringComparison.Ordinal));
            Assert.Contains(heardByAdmin, line => line.Contains(theirs.Id.ToString("D"), StringComparison.Ordinal));
        }

        [Fact]
        public async Task Stream_Quiet_SendsAHeartbeatComment()
        {
            FakeTimeProvider clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using ServerTestHost host = await ServerTestHost.StartAsync(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
            using HttpResponseMessage response = await OpenAsync(host.MemberClient);
            using StreamReader reader = new StreamReader(await response.Content.ReadAsStreamAsync());

            // Past any events the host's own setup published, which the advancing clock lets through.
            Task<string> heartbeat = ReadCommentAsync(reader);
            await clock.AdvanceUntilAsync(heartbeat, InvalidationController.HeartbeatInterval);

            Assert.Equal(": heartbeat", await heartbeat);
            Assert.Equal(string.Empty, await ReadLineAsync(reader));
        }

        private static async Task<string> ReadCommentAsync(StreamReader reader)
        {
            string line = await ReadLineAsync(reader);
            while (!line.StartsWith(':'))
            {
                line = await ReadLineAsync(reader);
            }

            return line;
        }

        private static async Task<HttpResponseMessage> OpenAsync(HttpClient client)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/invalidation/stream");
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }

        private static async Task<string> ReadLineAsync(StreamReader reader)
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(ReadTimeout);
            return await reader.ReadLineAsync(timeout.Token) ?? throw new EndOfStreamException("The stream ended.");
        }

        /// <summary>Every line up to and including the first naming <paramref name="shelfId"/>.</summary>
        private static async Task<IReadOnlyList<string>> ReadUntilAsync(StreamReader reader, Guid shelfId)
        {
            List<string> lines = new List<string>();
            do
            {
                lines.Add(await ReadLineAsync(reader));
            }
            while (!lines[^1].Contains(shelfId.ToString("D"), StringComparison.Ordinal));

            return lines;
        }

        private Task<ShelfInfo> CreateShelfAsync(string name)
        {
            return _host.CreateShelfAsync(name);
        }

        private async Task PutAsync(string shelf, string path)
        {
            ByteArrayContent content = new ByteArrayContent(Encoding.UTF8.GetBytes("contents"));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            (await _host.AdminClient.PutAsync($"/api/shelves/{shelf}/content?path={Uri.EscapeDataString(path)}", content)).EnsureSuccessStatusCode();
        }
    }
}
