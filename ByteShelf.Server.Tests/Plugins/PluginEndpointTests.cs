using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database.Mapping;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>A server with plugins, over HTTP.</summary>
    public sealed class PluginEndpointTests : IDisposable
    {
        private readonly TestPlugins _plugins = new TestPlugins();

        [Fact]
        public async Task List_WithoutPlugins_IsEmpty()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();

            PluginList? list = await host.AdminClient.GetFromJsonAsync<PluginList>("/api/plugins");

            Assert.Empty(list!.Plugins);
            Assert.Empty(list.Skipped!);
        }

        [Fact]
        public async Task List_SignedOut_IsRefused()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.AnonymousClient.GetAsync("/api/plugins")).StatusCode);
        }

        [Fact]
        public async Task List_ForAdminsAlone_HasWhatWasSkipped()
        {
            _plugins.Add("markdown", TestPlugins.Manifest("markdown"));
            string broken = _plugins.Add("broken", null);
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);
            await host.CreateShelfAsync("docs");
            using HttpClient key = await host.CreateKeyClientAsync("docs", ShelfKeyPermission.Read);

            PluginList? admins = await host.AdminClient.GetFromJsonAsync<PluginList>("/api/plugins");
            Assert.Equal(("markdown", "Test markdown", "1.2.3"), Assert.Single(admins!.Plugins.Select(plugin => (plugin.Id, plugin.Name, plugin.Version))));
            Assert.Equal((broken, "It has no plugin.json."), Assert.Single(admins.Skipped!.Select(plugin => (plugin.Folder, plugin.Reason))));

            foreach (HttpClient client in new[] { host.MemberClient, key })
            {
                PluginList? others = await client.GetFromJsonAsync<PluginList>("/api/plugins");
                Assert.Single(others!.Plugins);
                Assert.Null(others.Skipped);
            }
        }

        [Fact]
        public async Task Assets_AreServedForGood()
        {
            string folder = _plugins.Add("markdown", TestPlugins.Manifest("markdown"));
            TestPlugins.Write(folder, "ui/plugin.js", "export default 1;");
            TestPlugins.Write(folder, "ui/plugin.css", "a {}");
            TestPlugins.Write(folder, "ui/data/table.bcmap", "table");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);
            PluginView plugin = Assert.Single((await host.MemberClient.GetFromJsonAsync<PluginList>("/api/plugins"))!.Plugins);

            Assert.Matches("^/plugins/markdown/[0-9a-f]{32}/plugin\\.js$", plugin.ModuleUrl);
            HttpResponseMessage module = await host.AnonymousClient.GetAsync(plugin.ModuleUrl);
            Assert.Equal("export default 1;", await module.Content.ReadAsStringAsync());
            Assert.Equal("text/javascript", module.Content.Headers.ContentType?.MediaType);
            Assert.Equal((true, TimeSpan.FromDays(365)), (module.Headers.CacheControl!.Public, module.Headers.CacheControl.MaxAge));
            Assert.Contains(module.Headers.CacheControl.Extensions, extension => extension.Name == "immutable");

            Assert.Equal("text/css", (await host.AnonymousClient.GetAsync(plugin.StylesheetUrl)).Content.Headers.ContentType?.MediaType);
            string table = plugin.ModuleUrl!.Replace("plugin.js", "data/table.bcmap", StringComparison.Ordinal);
            Assert.Equal("table", await host.AnonymousClient.GetStringAsync(table));
        }

        [Theory]
        [InlineData("{root}/../plugin.json")]
        [InlineData("{root}/..%2fplugin.json")]
        [InlineData("{root}//plugin.json")]
        [InlineData("/plugins/markdown/plugin.json")]
        [InlineData("/plugins/markdown/plugin.js")]
        [InlineData("/plugins/markdown/00000000000000000000000000000000/plugin.js")]
        public async Task Assets_OutsideTheUiFolder_AreNotServed(string path)
        {
            TestPlugins.Write(_plugins.Add("markdown", TestPlugins.Manifest("markdown")), "ui/plugin.js", "export default 1;");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);
            string root = host.Services.GetRequiredService<IPluginCatalog>().Loaded.Single().Ui!.RequestPath;

            // As it is written, since a Uri collapses the dots before it is sent.
            Assert.Equal(StatusCodes.Status200OK, (await host.SendRawAsync($"{root}/plugin.js")).Response.StatusCode);
            HttpContext answer = await host.SendRawAsync(path.Replace("{root}", root, StringComparison.Ordinal));

            Assert.Equal(StatusCodes.Status404NotFound, answer.Response.StatusCode);
        }

        [Fact]
        public async Task Assets_WithoutAModule_AreNotServed()
        {
            string folder = _plugins.Add("server-only", TestPlugins.Manifest("server-only"));
            TestPlugins.Write(folder, "ui/plugin.css", "a {}");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);

            PluginView plugin = Assert.Single((await host.MemberClient.GetFromJsonAsync<PluginList>("/api/plugins"))!.Plugins);

            Assert.Null(plugin.ModuleUrl);
            Assert.Null(plugin.StylesheetUrl);
        }

        [Fact]
        public async Task List_AModuleWithoutAStylesheet_NamesNone()
        {
            TestPlugins.Write(_plugins.Add("markdown", TestPlugins.Manifest("markdown")), "ui/plugin.js", "export default 1;");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);

            PluginView plugin = Assert.Single((await host.MemberClient.GetFromJsonAsync<PluginList>("/api/plugins"))!.Plugins);

            Assert.NotNull(plugin.ModuleUrl);
            Assert.Null(plugin.StylesheetUrl);
        }

        [Fact]
        public async Task Controllers_AnswerUnderTheirPluginsAddressWhateverTheyDeclare()
        {
            _plugins.AddServerPlugin("first");
            _plugins.AddServerPlugin("second");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);

            List<string> routes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace == "ByteShelf.Server.Tests.Plugin")
                .Select(endpoint => endpoint.RoutePattern.RawText!.TrimStart('/'))
                .ToList();
            string[] declared = ["api/shelves", "api/shelves/relative", "api/shelves/write", "api/version/current", "absolute", "bare", "api/escaped/it"];
            Assert.Equal(
                new[] { "first", "second" }.SelectMany(id => declared.Select(route => $"api/plugins/{id}/{route}")).Order(StringComparer.Ordinal),
                routes.Order(StringComparer.Ordinal));

            Assert.Equal("absolute", await host.AnonymousClient.GetStringAsync("/api/plugins/first/absolute"));
            Assert.Equal("bare", await host.AnonymousClient.GetStringAsync("/api/plugins/second/bare"));
            Assert.Equal("escaped", await host.AnonymousClient.GetStringAsync("/api/plugins/second/api/escaped/it"));
            Assert.Equal(HttpStatusCode.NotFound, (await host.AnonymousClient.GetAsync("/api/escaped/it")).StatusCode);
            Assert.Contains("versionId", await host.AnonymousClient.GetStringAsync("/api/version/current"), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Controllers_AreGuardedAsTheServersOwnAre()
        {
            _plugins.AddServerPlugin("server");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);
            await host.CreateShelfAsync("docs");
            using HttpClient key = await host.CreateKeyClientAsync("docs", ShelfKeyPermission.ReadWrite);

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.AnonymousClient.GetAsync("/api/plugins/server/api/shelves")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await key.GetAsync("/api/plugins/server/api/shelves")).StatusCode);
            Assert.Equal("guarded", await host.MemberClient.GetStringAsync("/api/plugins/server/api/shelves"));
        }

        [Fact]
        public async Task Controllers_RefuseWritesOnAReplica()
        {
            _plugins.AddServerPlugin("server");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);
            const string write = "/api/plugins/server/api/shelves/write";
            Assert.Equal(HttpStatusCode.OK, (await host.MemberClient.PostAsync(write, null)).StatusCode);

            await TestReplicas.MarkAsReplicaAsync(host.Services);

            Assert.Equal(HttpStatusCode.Conflict, (await host.MemberClient.PostAsync(write, null)).StatusCode);
        }

        [Fact]
        public async Task Controllers_AreLeftOutOfTheOpenApiDocument()
        {
            _plugins.AddServerPlugin("server");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);

            string document = await host.AnonymousClient.GetStringAsync("/swagger/v1/swagger.json");

            Assert.Contains("/api/plugins\"", document, StringComparison.Ordinal);
            Assert.DoesNotContain("/api/plugins/", document, StringComparison.Ordinal);
            Assert.DoesNotContain("Greeting", document, StringComparison.Ordinal);
        }

        [Fact]
        public async Task StartAsync_WithAPluginThatFails_StillServes()
        {
            _plugins.AddFailingServerPlugin("failing");

            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);

            PluginList? list = await host.AdminClient.GetFromJsonAsync<PluginList>("/api/plugins");
            Assert.Equal("failing", Assert.Single(list!.Skipped!).Id);
            Assert.Equal(HttpStatusCode.NotFound, (await host.AnonymousClient.GetAsync("/api/plugins/failing/bare")).StatusCode);
        }

        [Fact]
        public async Task DeclaredFields_OfALoadedPlugin_AreKeptAndFound()
        {
            _plugins.Add("notes", TestPlugins.Manifest("notes", fields: """[{ "name": "caption", "label": "Caption", "kind": "text", "document": "file", "searched": true }]"""));
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);
            await host.CreateShelfAsync("docs");
            await host.PutAsAdminAsync("docs", "/a.txt", "a"u8.ToArray(), "text/plain");
            await host.PutAsAdminAsync("docs", "/b.txt", "b"u8.ToArray(), "text/plain");
            SetPluginMetadataRequest write = new SetPluginMetadataRequest { Document = PluginDocument.Parse("""{ "caption": "Ett äpple" }""") };

            HttpResponseMessage written = await host.AdminClient.PutAsJsonAsync("/api/shelves/docs/files/plugin-metadata?path=/a.txt&plugin=notes", write);

            Assert.Equal(HttpStatusCode.OK, written.StatusCode);
            foreach (string criteria in new[] { "text=äpple", "pluginFields=notes.caption" })
            {
                FileSearchResults? found = await host.AdminClient.GetFromJsonAsync<FileSearchResults>($"/api/shelves/docs/search?{criteria}");
                Assert.Equal("/a.txt", Assert.Single(found!.Files).Path);
            }

            FileMetadata? metadata = await host.AdminClient.GetFromJsonAsync<FileMetadata>("/api/shelves/docs/files/metadata?path=/a.txt");
            Assert.Equal("Test notes", metadata!.Plugins["notes"].Name);
            Assert.Equal(["Caption"], metadata.Plugins["notes"].Fields.Select(field => field.Label));
        }

        [Fact]
        public async Task PluginMetadata_OfASkippedPlugin_IsNotFound()
        {
            _plugins.Add("skipped", TestPlugins.Manifest("skipped", api: 0));
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);
            await host.CreateShelfAsync("docs");
            await host.PutAsAdminAsync("docs", "/a.txt", "a"u8.ToArray(), "text/plain");

            HttpResponseMessage refused = await host.AdminClient.PutAsJsonAsync(
                "/api/shelves/docs/files/plugin-metadata?path=/a.txt&plugin=skipped", new SetPluginMetadataRequest { Document = PluginDocument.Parse("{}") });

            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        [Fact]
        public async Task AReader_OfALoadedPlugin_ReadsTheTypesItTakesAtItsVersion()
        {
            _plugins.AddReadingServerPlugin("reading", "3 text/plain text/html");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);
            await host.CreateShelfAsync("docs");
            ShelfFile file = await host.PutAsAdminAsync("docs", "/a.txt", "Ett äpple"u8.ToArray(), "text/plain");
            await host.PutAsAdminAsync("docs", "/a.html", "Html"u8.ToArray(), "text/html");
            await host.PutAsAdminAsync("docs", "/a.css", "Css"u8.ToArray(), "text/css");

            await host.Services.ReadPluginMetadataAsync();

            Assert.Equal(["reading: Ett äpple"], await ReadWordsAsync(host, "/a.txt"));
            Assert.Equal(["reading: Html"], await ReadWordsAsync(host, "/a.html"));
            Assert.Empty(await ReadWordsAsync(host, "/a.css"));
            Assert.Equal(3, await ReadReaderVersionAsync(host, file.Sha256));
        }

        [Fact]
        public async Task Readers_OfTwoLoadedPlugins_BothRead()
        {
            _plugins.AddReadingServerPlugin("first", "1 text/plain");
            _plugins.AddReadingServerPlugin("second", "1 text/plain");
            await using ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: _plugins.Path);
            await host.CreateShelfAsync("docs");
            await host.PutAsAdminAsync("docs", "/a.txt", "a"u8.ToArray(), "text/plain");

            await host.Services.ReadPluginMetadataAsync();

            Assert.Equal(["first: a", "second: a"], (await ReadWordsAsync(host, "/a.txt")).Order());
        }

        [Fact]
        public async Task AReaderThatCannotBeMade_IsLoggedAndLeavesWhatItWouldReadForTheNextStart()
        {
            RecordingLogger<ContentReadRunner> logger = new RecordingLogger<ContentReadRunner>();
            await using ServerTestHost host = await StartWithAnUnmadeReaderAsync(services => services.AddSingleton<ILogger<ContentReadRunner>>(logger));
            ShelfFile stored = await host.PutAsAdminAsync("docs", "/a.txt", "Ett äpple"u8.ToArray(), "text/plain");

            await host.Services.ReadPluginMetadataAsync();

            Assert.Equal(["healthy: Ett äpple"], await ReadWordsAsync(host, "/a.txt"));
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            IContentPluginMetadataRepository repository = scope.ServiceProvider.GetRequiredService<IContentPluginMetadataRepository>();
            Assert.Equal([stored.Sha256], await repository.GetUnreadAsync("reading", 1, MediaTypeSet.Of("text/plain"), null, 10));
            Assert.Equal(["The reader reading could not be made, so it reads nothing until the server restarts: It was told to fail."], logger.Messages);
            Assert.IsType<InvalidOperationException>(Assert.Single(logger.Exceptions));
        }

        [Fact]
        public async Task List_ForAdminsAlone_SaysWhyAReaderReadsNothing()
        {
            await using ServerTestHost host = await StartWithAnUnmadeReaderAsync();
            await host.PutAsAdminAsync("docs", "/a.txt", "a"u8.ToArray(), "text/plain");
            await host.Services.ReadPluginMetadataAsync();

            string problem = "Its reader of files' contents could not be made, so it reads nothing until the server restarts: It was told to fail.";
            Assert.Equal([("healthy", null), ("reading", problem)], await ReadProblemsAsync(host.AdminClient));
            Assert.Equal([("healthy", null), ("reading", null)], await ReadProblemsAsync(host.MemberClient));
        }

        public void Dispose()
        {
            _plugins.Dispose();
        }

        /// <summary>A server with the shelf "docs" and two reading plugins: "reading", whose reader cannot be made, and "healthy".</summary>
        private async Task<ServerTestHost> StartWithAnUnmadeReaderAsync(Action<IServiceCollection>? configureServices = null)
        {
            TestPlugins.Write(_plugins.AddReadingServerPlugin("reading", "1 text/plain"), "fail-reader", string.Empty);
            _plugins.AddReadingServerPlugin("healthy", "1 text/plain");
            ServerTestHost host = await ServerTestHost.StartAsync(configureServices, _plugins.Path);
            await host.CreateShelfAsync("docs");
            return host;
        }

        /// <summary>Each plugin with what is wrong with its reader, as <paramref name="client"/> is told.</summary>
        private static async Task<IEnumerable<(string Id, string? Problem)>> ReadProblemsAsync(HttpClient client)
        {
            PluginList? list = await client.GetFromJsonAsync<PluginList>("/api/plugins");
            return list!.Plugins.Select(plugin => (plugin.Id, plugin.ReaderProblem));
        }

        /// <summary>The words each plugin's reader found, as "plugin: words".</summary>
        private static async Task<IReadOnlyList<string>> ReadWordsAsync(ServerTestHost host, string path)
        {
            FileMetadata? metadata = await host.AdminClient.GetFromJsonAsync<FileMetadata>($"/api/shelves/docs/files/metadata?path={path}");
            return metadata!.Plugins.SelectMany(plugin => plugin.Value.Fields.Select(field => $"{plugin.Key}: {field.Value.Text}")).ToList();
        }

        private static Task<int> ReadReaderVersionAsync(ServerTestHost host, string sha256)
        {
            return host.Services.InTransactionAsync((_, session) => session.Connection.QuerySingleAsync<int>(
                "SELECT reader_version FROM content_plugin_metadata WHERE sha256 = @sha256", new { sha256 }, transaction: session.Transaction));
        }
    }
}
