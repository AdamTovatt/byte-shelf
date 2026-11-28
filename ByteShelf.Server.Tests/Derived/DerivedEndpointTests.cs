using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfClient.Http;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>Derived files over HTTP, with the server's own workers running.</summary>
    public sealed class DerivedEndpointTests : IAsyncLifetime
    {
        private readonly TestDerivedPlugins _plugins = new TestDerivedPlugins();
        private ServerTestHost _host = null!;
        private ShelfFile _file = null!;

        public async Task InitializeAsync()
        {
            _plugins.All = [_plugins.Plugin()];
            _host = await ServerTestHost.StartAsync(_plugins.AddTo);
            await _host.CreateShelfAsync("docs");
            _file = await _host.PutAsAdminAsync("docs", "/a.txt", "hello"u8.ToArray(), "text/plain");
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
            _plugins.Dispose();
        }

        [Theory]
        [InlineData("/api/shelves/docs/files/derived?path=/a.txt&kind=thumbnail")]
        [InlineData("/api/open/docs/files/derived?path=/a.txt&kind=thumbnail")]
        public async Task Get_SendsItAsContentsAreSentForABrowserToKeepAnHour(string url)
        {
            using HttpClient key = await _host.CreateKeyClientAsync("docs", ShelfPermission.Read);

            HttpResponseMessage response = await key.GetAsync(url);

            Assert.Equal("Thumbnail of hello by test-maker 1", await response.Content.ReadAsStringAsync());
            Assert.Equal("image/webp", response.Content.Headers.ContentType?.ToString());
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("sandbox; default-src 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
            Assert.Equal("max-age=3600, private", response.Headers.CacheControl?.ToString());
            Assert.Equal($"\"thumbnail-{_file.Sha256}-test-maker-1\"", response.Headers.ETag?.ToString());
            Assert.Null(response.Content.Headers.ContentDisposition);
        }

        [Fact]
        public async Task Get_WithTheETagItHas_IsNotModifiedAndAHeadHasNoBody()
        {
            string url = $"/api/shelves/docs/files/derived?id={_file.Id}&kind=preview";
            EntityTagHeaderValue etag = (await _host.AdminClient.GetAsync(url)).Headers.ETag!;
            using HttpRequestMessage again = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { IfNoneMatch = { etag } } };
            using HttpRequestMessage head = new HttpRequestMessage(HttpMethod.Head, url);

            HttpResponseMessage notModified = await _host.AdminClient.SendAsync(again);
            HttpResponseMessage headed = await _host.AdminClient.SendAsync(head);

            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
            Assert.Equal((HttpStatusCode.OK, etag), (headed.StatusCode, headed.Headers.ETag));
            Assert.Empty(await headed.Content.ReadAsByteArrayAsync());
        }

        [Theory]
        [InlineData("/api/shelves/docs/files/derived?path=/none.txt&kind=thumbnail", HttpStatusCode.NotFound)]
        [InlineData("/api/shelves/docs/files/derived?path=/a.txt", HttpStatusCode.BadRequest)]
        [InlineData("/api/shelves/docs/files/derived?path=/a.txt&kind=poster", HttpStatusCode.BadRequest)]
        [InlineData("/api/shelves/docs/files/derived?path=/a.txt&kind=99", HttpStatusCode.BadRequest)]
        [InlineData("/api/shelves/docs/files/derived?kind=thumbnail", HttpStatusCode.BadRequest)]
        public async Task Get_WhatIsNotThereOrNotAsked_IsRefused(string url, HttpStatusCode expected)
        {
            Assert.Equal(expected, (await _host.AdminClient.GetAsync(url)).StatusCode);
        }

        [Fact]
        public async Task Get_ByAUserWithoutTheShelf_IsNotFoundAndWithoutAUserIsUnauthorized()
        {
            string url = $"/api/shelves/docs/files/derived?id={_file.Id}&kind=thumbnail";

            Assert.Equal(HttpStatusCode.NotFound, (await _host.MemberClient.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _host.AnonymousClient.GetAsync(url)).StatusCode);
        }

        [Theory]
        [InlineData("/api/shelves/docs/trash")]
        [InlineData("/api/open/docs/trash")]
        public async Task GetDeleted_AFileInTheTrash_IsSentAlike(string trash)
        {
            TrashEntry deleted = (await (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/files?id={_file.Id}")).Content.ReadFromJsonAsync<TrashEntry>())!;

            HttpResponseMessage response = await _host.AdminClient.GetAsync($"{trash}/{deleted.DeletionId}/derived?id={_file.Id}&kind=thumbnail");

            Assert.Equal("Thumbnail of hello by test-maker 1", await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await _host.AdminClient.GetAsync($"/api/shelves/docs/files/derived?id={_file.Id}&kind=thumbnail")).StatusCode);
        }

        [Fact]
        public async Task Get_NotMadeYet_IsUnavailableWithWhenToComeBack()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync(services =>
            {
                _plugins.AddTo(services);
                services.AddScoped<IDerivedFileService>(_ => new NotReady());
            });
            await host.CreateShelfAsync("docs");

            HttpResponseMessage response = await host.AdminClient.GetAsync("/api/shelves/docs/files/derived?path=/a.txt&kind=full");

            Assert.Equal((HttpStatusCode.ServiceUnavailable, "5"), (response.StatusCode, response.Headers.RetryAfter?.ToString()));
            Assert.Equal(ErrorCodes.NotReady, (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
        }

        [Fact]
        public async Task HttpClient_OneNotMadeYet_ThrowsNotReady()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync(services =>
            {
                _plugins.AddTo(services);
                services.AddScoped<IDerivedFileService>(_ => new NotReady());
            });
            await host.CreateShelfAsync("docs");
            using HttpClient http = host.CreateClient();
            CreatedShelfKey key = await host.CreateKeyAsync("docs", ShelfPermission.Read);
            IShelfClient shelf = new ByteShelfHttpClient(http, new ShelfKeyCredentials(key.Secret)).Shelf("docs");

            ByteShelfException refused = await Assert.ThrowsAsync<ByteShelfException>(() => shelf.OpenDerivedAsync("/a.txt", DerivedKind.Full));

            Assert.Equal((HttpStatusCode.ServiceUnavailable, ErrorCodes.NotReady), (refused.StatusCode, refused.Code));
        }

        [Fact]
        public async Task Upload_HasThumbnailAndPreviewMadeWithoutAsking()
        {
            await _host.PutAsAdminAsync("docs", "/b.txt", "stored"u8.ToArray(), "text/plain");

            await TestDerivedPlugins.UntilAsync(() => _plugins.Runs.Contains("stored: Thumbnail, Preview"));
        }

        [Fact]
        public async Task Upload_AfterOneThatCouldNotBeMade_IsMadeAllTheSame()
        {
            await _host.PutAsAdminAsync("docs", "/b.txt", "in the way"u8.ToArray(), "text/plain");
            await TestDerivedPlugins.UntilAsync(() => _plugins.Runs.Contains("in the way: Thumbnail, Preview"));

            await _host.PutAsAdminAsync("docs", "/c.txt", "stored"u8.ToArray(), "text/plain");

            await TestDerivedPlugins.UntilAsync(() => _plugins.Runs.Contains("stored: Thumbnail, Preview"));
        }

        [Fact]
        public async Task Get_ARange_IsSentAlone()
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"/api/shelves/docs/files/derived?id={_file.Id}&kind=thumbnail")
            {
                Headers = { Range = new RangeHeaderValue(0, 8) },
            };

            HttpResponseMessage response = await _host.AdminClient.SendAsync(request);

            Assert.Equal((HttpStatusCode.PartialContent, "Thumbnail"), (response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        [Fact]
        public async Task Start_RemovesWhatAServerThatDidNotStopWasMakingAndWhatNothingHolds()
        {
            WorkFolder? left = null;
            WorkFolder? leftList = null;
            string? unheld = null;
            await using ServerTestHost host = await ServerTestHost.StartAsync(services =>
            {
                RootFolder root = services.Select(service => service.ImplementationInstance).OfType<RootFolder>().First();
                left = WorkFolder.Create(root);
                left.Add(TestHashes.Sha256Of("gone"));
                File.WriteAllText(Path.Combine(left.Path, "thumbnail"), "read from contents");
                leftList = WorkFolder.Create(root);
                leftList.Add(TestHashes.Sha256Of("gone"));
                Directory.Delete(leftList.Path);
                unheld = Path.Combine(root.DerivedPath, "thumbnail", "aa", "aa", new string('a', 64));
                Directory.CreateDirectory(Path.GetDirectoryName(unheld)!);
                File.WriteAllText(unheld, "read from contents");
            });

            // Those of a process before it takes requests, the other by the maintenance that runs as it starts.
            Assert.DoesNotContain(WorkFolder.Find(host.TestRoot.Root), folder => folder.Path == left!.Path || folder.Path == leftList!.Path);
            await TestDerivedPlugins.UntilAsync(() => !File.Exists(unheld));
        }

        [Fact]
        public void Start_RemovesWhatIsLeftBeforeAnythingHasPluginsDoMore()
        {
            List<Type> started = _host.Services.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

            Type[] first = [typeof(PluginProcessesLifetime), typeof(MetadataReadingWorker), typeof(StoredContentsWorker), typeof(DerivedMakingWorker)];
            Assert.Equal(first[0], started.First(first.Contains));
            Assert.All(first, worker => Assert.Contains(worker, started));
        }

        [Fact]
        public void Start_HasAWorkerOfEachGroupMadeInTheBackground()
        {
            Assert.Equal(MakingGroup.InBackground, _host.Services.GetServices<IHostedService>().OfType<DerivedMakingWorker>().Select(worker => worker.Group));
        }

        [Fact]
        public async Task Get_AStreamBeingMade_IsNotReadyAtOnceWithAMinuteToComeBack()
        {
            using TestDerivedPlugins plugins = new TestDerivedPlugins();
            plugins.All = [plugins.Plugin(kinds: [DerivedKind.Stream])];
            await using ServerTestHost host = await ServerTestHost.StartAsync(plugins.AddTo);
            await host.CreateShelfAsync("docs");
            ShelfFile file = await host.PutAsAdminAsync("docs", "/s.txt", Encoding.UTF8.GetBytes(plugins.Waiting("s")), "text/plain");

            // Made by the background, as it was stored: answered while it is, as only a wait for none says a minute.
            using (await plugins.StartedAsync("s"))
            {
                using HttpResponseMessage response = await host.AdminClient.GetAsync($"/api/shelves/docs/files/derived?id={file.Id}&kind=stream");

                ErrorResponse? error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                Assert.Equal((HttpStatusCode.ServiceUnavailable, "60", ErrorCodes.NotReady), (response.StatusCode, response.Headers.RetryAfter?.ToString(), error?.Code));
                plugins.GoOn("s");
            }
        }

        [UnixFact]
        public async Task Start_WithWhatCannotBeRemovedLeft_StartsAllTheSame()
        {
            UndeletableFolder? left = null;
            await using ServerTestHost host = await ServerTestHost.StartAsync(services =>
                left = UndeletableFolder.Create(services.Select(service => service.ImplementationInstance).OfType<RootFolder>().First(), TestHashes.Sha256Of("gone")));

            Assert.True(Directory.Exists(left!.Path));
            left.Release();
        }

        [UnixFact]
        public async Task EmptyTrash_OnceARunLeftWhatCannotBeRemoved_IsUnavailableWithWhenToComeBack()
        {
            ShelfFile file = await _host.PutAsAdminAsync("docs", "/u.txt", "undeletable"u8.ToArray(), "text/plain");
            (await _host.AdminClient.GetAsync($"/api/shelves/docs/files/derived?id={file.Id}&kind=thumbnail")).EnsureSuccessStatusCode();
            using UndeletableFolder left = new UndeletableFolder(_host.TestRoot, file.Sha256);
            (await _host.AdminClient.DeleteAsync("/api/shelves/docs/files?path=/u.txt")).EnsureSuccessStatusCode();

            using HttpResponseMessage response = await _host.AdminClient.DeleteAsync("/api/shelves/docs/trash");

            ErrorResponse? error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal((HttpStatusCode.ServiceUnavailable, "30", ErrorCodes.NotReady), (response.StatusCode, response.Headers.RetryAfter?.ToString(), error?.Code));
        }

        [Fact]
        public async Task Stop_EndsWhatIsBeingMadeAsItBeginsToStopAndRecordsNothing()
        {
            ShelfFile file = await _host.PutAsAdminAsync("docs", "/b.txt", Encoding.UTF8.GetBytes(_plugins.Waiting("b")), "text/plain");
            using Process process = await _plugins.StartedAsync("b");
            bool ended = false;

            // Stopped with the workers still running, as everything else is.
            using CancellationTokenRegistration stopped = _host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(() => ended = process.HasExited);
            await _host.StopAsync();

            Assert.True(ended);
            Assert.Null(_host.Services.GetRequiredService<DerivedStore>().Find(DerivedKind.Thumbnail, file.Sha256));
        }

        private sealed class NotReady : IDerivedFileService
        {
            public Task<ByteShelf.Server.Derived.Models.DerivedFile> GetAsync(Server.Storage.Models.Shelf shelf, EntryReference reference, DerivedKind kind, CancellationToken cancellationToken)
            {
                throw new Server.Shared.Errors.RetryLaterException("Still being made.", TimeSpan.FromSeconds(5));
            }

            public Task<ByteShelf.Server.Derived.Models.DerivedFile> GetDeletedAsync(Server.Storage.Models.Shelf shelf, Guid deletionId, Guid fileId, DerivedKind kind, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
