using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>Finding plugins' folders, and what each says it is.</summary>
    public sealed class PluginManifestReaderTests : IDisposable
    {
        private readonly TestPlugins _plugins = new TestPlugins();

        [Fact]
        public void Read_AValidManifest_TakesEverythingFromIt()
        {
            string path = _plugins.Add("some folder", """{ "id": "my-plugin2", "name": "My plugin", "version": "1.0", "api": 1, "serverAssembly": "server/My.dll", "more": [] }""");
            TestPlugins.Write(path, "server/My.dll", "no assembly");

            PluginCandidate plugin = PluginManifestReader.Read(path);

            Assert.Equal(new ReadablePlugin(path, new PluginManifest("my-plugin2", "My plugin", "1.0", Path.Combine(path, "server", "My.dll"))), plugin);
        }

        [Theory]
        [InlineData(null, "no plugin.json")]
        [InlineData("", "could not be read")]
        [InlineData("[]", "not a JSON object")]
        [InlineData("""{ "id": "a", "name": "\ud800", "version": "1", "api": 1 }""", "could not be read")]
        [InlineData("""{ "name": "A", "version": "1", "api": 1 }""", "id")]
        [InlineData("""{ "id": "", "name": "A", "version": "1", "api": 1 }""", "id")]
        [InlineData("""{ "id": "My", "name": "A", "version": "1", "api": 1 }""", "id")]
        [InlineData("""{ "id": "../a", "name": "A", "version": "1", "api": 1 }""", "id")]
        [InlineData("""{ "id": "a\n", "name": "A", "version": "1", "api": 1 }""", "id")]
        [InlineData("""{ "id": 7, "name": "A", "version": "1", "api": 1 }""", "id")]
        [InlineData("""{ "id": "a", "name": "A", "version": "1" }""", "api must")]
        [InlineData("""{ "id": "a", "name": "A", "version": "1", "api": "1" }""", "api must")]
        [InlineData("""{ "id": "a", "name": "A", "version": "1", "api": 1.5 }""", "api must")]
        [InlineData("""{ "id": "a", "name": "A", "version": "1", "api": 2 }""", "api 2, and this server's is 1")]
        [InlineData("""{ "id": "a", "api": 2 }""", "api 2, and this server's is 1")]
        [InlineData("""{ "id": "a", "name": " ", "version": "1", "api": 1 }""", "no name")]
        [InlineData("""{ "id": "a", "name": "A", "version": 1, "api": 1 }""", "no version")]
        [InlineData("""{ "id": "a", "name": "A", "version": "1", "api": 1, "serverAssembly": 1 }""", "must be the path")]
        [InlineData("""{ "id": "a", "name": "A", "version": "1", "api": 1, "serverAssembly": "a.dll" }""", "is not there")]
        [InlineData("""{ "id": "a", "name": "A", "version": "1", "api": 1, "serverAssembly": "../a.dll" }""", "outside")]
        [InlineData("""{ "id": "a", "name": "A", "version": "1", "api": 1, "serverAssembly": "/etc/passwd" }""", "outside")]
        [InlineData("""{ "id": "a", "name": "A", "version": "1", "api": 1, "serverAssembly": "a\u0000" }""", "could not be read")]
        public void Read_AManifestThatIsNotValid_SaysWhy(string? manifest, string because)
        {
            UnreadablePlugin plugin = Assert.IsType<UnreadablePlugin>(PluginManifestReader.Read(_plugins.Add("plugin", manifest)));

            Assert.Contains(because, plugin.Skipped.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Read_AFolderThatIsNotThere_SaysSo()
        {
            UnreadablePlugin plugin = Assert.IsType<UnreadablePlugin>(PluginManifestReader.Read(Path.Combine(_plugins.Path, "missing")));

            Assert.Equal("There is no such folder.", plugin.Skipped.Reason);
        }

        [Fact]
        public void Read_AManifestStartingWithAByteOrderMark_ReadsIt()
        {
            string path = _plugins.Add("markdown", "\uFEFF" + TestPlugins.Manifest("markdown"));

            Assert.IsType<ReadablePlugin>(PluginManifestReader.Read(path));
        }

        [UnixFact]
        public void Read_ALinkToAFolderWithoutAManifest_NamesTheFolderItself()
        {
            string real = _plugins.Add("real", null);
            Directory.CreateSymbolicLink(_plugins.InHome("link"), real);

            Assert.Equal(real, PluginManifestReader.Read(_plugins.InHome("link")).Folder);
        }

        [UnixFact]
        public void Read_ALinkThatLeadsToItself_SaysItCannotBeRead()
        {
            string link = Path.Combine(_plugins.Path, "circle");
            Directory.CreateSymbolicLink(link, "circle");

            UnreadablePlugin plugin = Assert.IsType<UnreadablePlugin>(PluginManifestReader.Read(link));

            Assert.StartsWith("Its plugin.json could not be read: ", plugin.Skipped.Reason, StringComparison.Ordinal);
        }

        [UnixFact]
        public void Find_LinksToFolders_AreResolvedAllTheWay()
        {
            // As updaemon has it: the plugins folder behind one link, each plugin another, and a version
            // folder that is itself reached through a link.
            string real = _plugins.InHome("versions/2/images");
            TestPlugins.Write(real, "plugin.json", TestPlugins.Manifest("images"));
            Directory.CreateSymbolicLink(_plugins.InHome("current"), "versions/2");
            Directory.CreateSymbolicLink(Path.Combine(_plugins.Path, "linked"), "../current/images");
            Directory.CreateSymbolicLink(_plugins.InHome("extensions"), "plugins");
            File.WriteAllText(Path.Combine(_plugins.Path, "a-file"), "no folder");

            PluginCandidate found = Assert.Single(PluginDiscovery.Find(_plugins.InHome("extensions") + "/", NullLogger.Instance));

            Assert.Equal("images", found.Id);
            Assert.Equal(real, found.Folder);
        }

        [UnixFact]
        public void Find_ALinkThatLeadsNowhere_IsFoundWithTheReason()
        {
            string link = Path.Combine(_plugins.Path, "dangling");
            Directory.CreateSymbolicLink(link, "missing");

            UnreadablePlugin plugin = Assert.IsType<UnreadablePlugin>(Assert.Single(PluginDiscovery.Find(_plugins.Path, NullLogger.Instance)));

            Assert.Equal((link, "There is no such folder."), (plugin.Folder, plugin.Skipped.Reason));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(" ")]
        public void Find_WithoutAPluginsFolder_FindsNone(string? path)
        {
            Assert.Empty(PluginDiscovery.Find(path, NullLogger.Instance));
        }

        [Fact]
        public void Find_APluginsFolderThatIsNotThere_FindsNoneAndLogsIt()
        {
            RecordingLogger logger = new RecordingLogger();

            Assert.Empty(PluginDiscovery.Find(_plugins.InHome("missing"), logger));
            Assert.Contains(logger.Messages, message => message.Contains("BYTESHELF_PLUGINS", StringComparison.Ordinal));
        }

        public void Dispose()
        {
            _plugins.Dispose();
        }
    }
}
