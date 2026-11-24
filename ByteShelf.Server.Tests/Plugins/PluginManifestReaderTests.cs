using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
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

            Assert.Equal(new ReadablePlugin(path, new PluginManifest(new PluginDeclaration("my-plugin2", "My plugin"), "1.0", Path.Combine(path, "server", "My.dll"))), plugin);
        }

        [Fact]
        public void Read_DeclaredFields_TakesEachInTheOrderGiven()
        {
            string path = _plugins.Add("notes", TestPlugins.Manifest("notes", fields: """
                [
                    { "name": "caption", "label": "Caption", "kind": "text", "document": "file", "searched": true, "listed": true, "more": 1 },
                    { "name": "width", "label": "Width", "kind": "number", "document": "contents", "searched": false, "listed": false },
                    { "name": "due", "label": "Due", "kind": "date", "document": "file", "listed": true },
                    { "name": "done", "label": "Done", "kind": "flag", "document": "file" }
                ]
                """));

            ReadablePlugin plugin = Assert.IsType<ReadablePlugin>(PluginManifestReader.Read(path));

            Assert.Equal(
                [
                    new PluginField("caption", "Caption", PluginFieldKind.Text, PluginDocumentSubject.File, searched: true, listed: true),
                    new PluginField("width", "Width", PluginFieldKind.Number, PluginDocumentSubject.Contents),
                    new PluginField("due", "Due", PluginFieldKind.Date, PluginDocumentSubject.File, listed: true),
                    new PluginField("done", "Done", PluginFieldKind.Flag, PluginDocumentSubject.File),
                ],
                plugin.Manifest.Declaration.Fields);
        }

        [Theory]
        [InlineData("""{}""", "Its fields must be a list of objects.")]
        [InlineData("""null""", "Its fields must be a list of objects.")]
        [InlineData("""["caption"]""", "Its fields must be a list of objects.")]
        [InlineData("""[{ "label": "A", "kind": "text", "document": "file" }]""", "One of its fields has no name.")]
        [InlineData("""[{ "name": "a b", "label": "A", "kind": "text", "document": "file" }]""", "'a b' is not a field's name: a letter, then letters, digits, hyphens and underscores, 64 characters at most.")]
        [InlineData("""[{ "name": "a", "label": 1, "kind": "text", "document": "file" }]""", "The field 'a' has no label.")]
        [InlineData("""[{ "name": "a", "label": "A", "document": "file" }]""", "The kind of its field 'a' must be one of: text, number, date, flag.")]
        [InlineData("""[{ "name": "a", "label": "A", "kind": "Text", "document": "file" }]""", "The kind of its field 'a' must be one of: text, number, date, flag.")]
        [InlineData("""[{ "name": "a", "label": "A", "kind": "0", "document": "file" }]""", "The kind of its field 'a' must be one of: text, number, date, flag.")]
        [InlineData("""[{ "name": "a", "label": "A", "kind": "text" }]""", "The document of its field 'a' must be one of: contents, file.")]
        [InlineData("""[{ "name": "a", "label": "A", "kind": "text", "document": "file", "searched": "true" }]""", "The searched of its field 'a' must be true or false.")]
        [InlineData("""[{ "name": "a", "label": "A", "kind": "text", "document": "file", "listed": null }]""", "The listed of its field 'a' must be true or false.")]
        [InlineData("""[{ "name": "a", "label": "A", "kind": "flag", "document": "file", "searched": true }]""", "The field 'a' is searched by free text, which only a text field can be.")]
        [InlineData("""[{ "name": "a", "label": "A", "kind": "text", "document": "file" }, { "name": "a", "label": "B", "kind": "flag", "document": "contents" }]""", "The plugin 'notes' declares the field 'a' twice.")]
        public void Read_DeclaredFieldsThatAreNotValid_SaysWhyAndWhichPlugin(string fields, string because)
        {
            UnreadablePlugin plugin = Assert.IsType<UnreadablePlugin>(PluginManifestReader.Read(_plugins.Add("notes", TestPlugins.Manifest("notes", fields: fields))));

            Assert.Equal(("notes", because), (plugin.Skipped.Id, plugin.Skipped.Reason));
        }

        [Theory]
        [InlineData(null, "no plugin.json")]
        [InlineData("", "could not be read")]
        [InlineData("[]", "not a JSON object")]
        [InlineData("""{ "id": "a", "name": "\ud800", "version": "1", "api": 1 }""", "could not be read")]
        [InlineData("""{ "name": "A", "version": "1", "api": 1 }""", "Its id must be")]
        [InlineData("""{ "id": "", "name": "A", "version": "1", "api": 1 }""", "Its id must be")]
        [InlineData("""{ "id": "My", "name": "A", "version": "1", "api": 1 }""", "Its id must be")]
        [InlineData("""{ "id": "../a", "name": "A", "version": "1", "api": 1 }""", "Its id must be")]
        [InlineData("""{ "id": "a\n", "name": "A", "version": "1", "api": 1 }""", "Its id must be")]
        [InlineData("""{ "id": 7, "name": "A", "version": "1", "api": 1 }""", "Its id must be")]
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
