using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.AnonymousClient.GetAsync("/api/plugins/server/api/shelves")).StatusCode);
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

        public void Dispose()
        {
            _plugins.Dispose();
        }
    }
}
