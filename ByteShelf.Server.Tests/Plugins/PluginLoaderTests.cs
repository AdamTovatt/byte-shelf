using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ByteShelf.Plugins;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.EnvironmentVariables;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>Loading plugins, and that one that cannot be loaded leaves nothing behind.</summary>
    public sealed class PluginLoaderTests : IDisposable
    {
        private readonly TestPlugins _plugins = new TestPlugins();
        private readonly RecordingLogger _logger = new RecordingLogger();
        private readonly IServiceCollection _services;
        private readonly ApplicationPartManager _parts;

        public PluginLoaderTests()
        {
            IMvcBuilder mvc = Program.AddServerServices(Program.CreateBuilder([]).Services, ServerSettings.OnNoRoot());
            _services = mvc.Services;
            _parts = mvc.PartManager;
        }

        [Fact]
        public void LoadAll_Plugins_AreLoadedInOrderOfId()
        {
            _plugins.Add("1", TestPlugins.Manifest("c"));
            _plugins.Add("2", TestPlugins.Manifest("a"));
            _plugins.Add("3", TestPlugins.Manifest("b"));

            Assert.Equal(["a", "b", "c"], LoadAll().Loaded.Select(plugin => plugin.Manifest.Id));
        }

        [Fact]
        public void LoadAll_TwoFoldersWithOneId_LoadsNeither()
        {
            string first = _plugins.Add("one", TestPlugins.Manifest("images"));
            string second = _plugins.Add("two", TestPlugins.Manifest("images", api: 99));
            _plugins.Add("three", TestPlugins.Manifest("markdown"));

            PluginCatalog catalog = LoadAll();

            Assert.Equal("markdown", Assert.Single(catalog.Loaded).Manifest.Id);
            Assert.Equal(
                [(first, $"The plugin in {second} has the id images too."), (second, $"The plugin in {first} has the id images too.")],
                catalog.Skipped.Select(plugin => (plugin.Folder, plugin.Reason)));
        }

        [UnixFact]
        public void LoadAll_AFolderAndALinkToIt_LoadItOnce()
        {
            _plugins.Add("images-1.0", TestPlugins.Manifest("images"));
            Directory.CreateSymbolicLink(Path.Combine(_plugins.Path, "images"), "images-1.0");

            PluginCatalog catalog = LoadAll();

            Assert.Equal("images", Assert.Single(catalog.Loaded).Manifest.Id);
            Assert.Empty(catalog.Skipped);
        }

        [UnixFact]
        public void LoadAll_TwoLinksToOneFolder_LoadItOnce()
        {
            TestPlugins.Write(_plugins.InHome("images"), "plugin.json", TestPlugins.Manifest("images"));
            Directory.CreateSymbolicLink(Path.Combine(_plugins.Path, "one"), _plugins.InHome("images"));
            Directory.CreateSymbolicLink(Path.Combine(_plugins.Path, "two"), _plugins.InHome("images"));

            PluginCatalog catalog = LoadAll();

            Assert.Equal("images", Assert.Single(catalog.Loaded).Manifest.Id);
            Assert.Empty(catalog.Skipped);
        }

        [Fact]
        public void LoadAll_AManifestWithAProblem_IsSkippedAndLogged()
        {
            string folder = _plugins.Add("images", TestPlugins.Manifest("images", api: 99));
            _plugins.Add("markdown", TestPlugins.Manifest("markdown"));

            PluginCatalog catalog = LoadAll();

            SkippedPlugin skipped = Assert.Single(catalog.Skipped);
            Assert.Equal((folder, "images", "Test images", "1.2.3"), (skipped.Folder, skipped.Id, skipped.Name, skipped.Version));
            Assert.Equal("markdown", Assert.Single(catalog.Loaded).Manifest.Id);
            Assert.Contains(_logger.Messages, message => message.Contains(folder, StringComparison.Ordinal) && message.Contains(skipped.Reason, StringComparison.Ordinal));
        }

        [Fact]
        public void LoadAll_APluginWithoutAModule_HasNoUi()
        {
            _plugins.Add("markdown", TestPlugins.Manifest("markdown"));

            Assert.Null(Assert.Single(LoadAll().Loaded).Ui);
        }

        [Fact]
        public void LoadAll_OneTree_HasTheSameUiHashUntilAFileChangesItsNameOrContents()
        {
            string folder = _plugins.Add("markdown", TestPlugins.Manifest("markdown"));
            TestPlugins.Write(folder, "ui/plugin.js", "code");
            TestPlugins.Write(folder, "ui/chunks/a", "more");
            PluginUi ui = LoadUi();
            Assert.Matches("^[0-9a-f]{32}$", ui.Hash);
            Assert.False(ui.HasStylesheet);

            TestPlugins.Write(folder, "outside.js", "not the ui's");
            Assert.Equal(ui.Hash, LoadUi().Hash);

            TestPlugins.Write(folder, "ui/chunks/a", "amore");
            string changed = LoadUi().Hash;
            File.Delete(Path.Combine(folder, "ui", "chunks", "a"));
            TestPlugins.Write(folder, "ui/chunks/aa", "more");
            string renamed = LoadUi().Hash;

            Assert.Equal(3, new[] { ui.Hash, changed, renamed }.Distinct().Count());
        }

        [UnixFact]
        public void LoadAll_AUiFolderThatCannotBeRead_IsSkipped()
        {
            string folder = _plugins.Add("markdown", TestPlugins.Manifest("markdown"));
            TestPlugins.Write(folder, "ui/plugin.js", "code");
            File.CreateSymbolicLink(Path.Combine(folder, "ui", "dangling"), "nowhere");

            Assert.StartsWith("Its ui folder could not be read: ", SkipReason(), StringComparison.Ordinal);
        }

        [Fact]
        public void LoadAll_AServerAssembly_RegistersItsServicesAndControllers()
        {
            _plugins.AddServerPlugin("server");

            Assert.Single(LoadAll().Loaded);

            Assert.Contains(_services, descriptor => descriptor.ServiceType.Name == "PluginGreeting");
            Assert.Contains(_parts.ApplicationParts, part => part.Name == "ByteShelf.Server.Tests.Plugin");
        }

        [Fact]
        public void LoadAll_AServerAssembly_HasItsOwnPackagesAndTheServersOwnContract()
        {
            _plugins.AddServerPlugin("server");

            LoadAll();

            Assert.Contains("OwnPackageVersion = 1.4.1", Greeting(), StringComparison.Ordinal);
            Assert.NotEqual(new System.Version(1, 4, 1, 0), typeof(VariableName).Assembly.GetName().Version);
            Assert.Contains($"CommonLocation = {typeof(ShelfPath).Assembly.Location}", Greeting(), StringComparison.Ordinal);
        }

        [Fact]
        public void LoadAll_APluginBringingTheServersAssemblies_StillGetsTheServersOwn()
        {
            string folder = AddServerPluginLoadingAnythingBesideIt("server");
            foreach (Assembly shared in new[] { typeof(IByteShelfPlugin).Assembly, typeof(ShelfPath).Assembly, typeof(IServiceCollection).Assembly })
            {
                File.Copy(shared.Location, Path.Combine(folder, Path.GetFileName(shared.Location)));
            }

            PluginCatalog catalog = LoadAll();

            Assert.Empty(catalog.Skipped.Select(plugin => plugin.Reason));
            Assert.Contains($"CommonLocation = {typeof(ShelfPath).Assembly.Location}", Greeting(), StringComparison.Ordinal);
        }

        [UnixFact]
        public void LoadAll_APluginWithANativeLibrary_LoadsItFromItsFolder()
        {
            // SQLite's, under a name only this plugin has, where its deps.json says: a place beside the
            // assembly the system would not look in itself.
            string extension = OperatingSystem.IsMacOS() ? "dylib" : "so";
            string runtime = RuntimeInformation.RuntimeIdentifier;
            string native = $"runtimes/{runtime}/native/libplugins_own.{extension}";
            string folder = _plugins.AddServerPlugin("server");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, native))!);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "runtimes", runtime, "native", $"libe_sqlite3.{extension}"), Path.Combine(folder, native));

            string depsPath = Path.Combine(folder, Path.ChangeExtension(TestPlugins.ServerAssemblyName, ".deps.json"));
            JsonNode deps = JsonNode.Parse(File.ReadAllText(depsPath))!;
            deps["targets"]!.AsObject().Single().Value!.AsObject()["Native/1.0.0"] = new JsonObject
            {
                ["runtimeTargets"] = new JsonObject { [native] = new JsonObject { ["rid"] = runtime, ["assetType"] = "native" } },
            };
            deps["libraries"]!["Native/1.0.0"] = new JsonObject { ["type"] = "package", ["serviceable"] = false, ["sha512"] = string.Empty };
            File.WriteAllText(depsPath, deps.ToJsonString());

            LoadAll();

            Assembly assembly = _services.Single(descriptor => descriptor.ServiceType.Name == "PluginGreeting").ServiceType.Assembly;
            Assert.True(NativeLibrary.TryLoad("plugins_own", assembly, null, out _));
            Assert.False(NativeLibrary.TryLoad("plugins_own", typeof(Program).Assembly, null, out _));
        }

        [Fact]
        public void LoadAll_ARegistrationThatThrows_LeavesNothingOfItBehind()
        {
            _plugins.AddFailingServerPlugin("failing");
            ServiceDescriptor[] services = _services.ToArray();
            ApplicationPart[] parts = _parts.ApplicationParts.ToArray();

            PluginCatalog catalog = LoadAll();

            Assert.Empty(catalog.Loaded);
            Assert.Equal(TestPlugins.FailingReason, Assert.Single(catalog.Skipped).Reason);
            Assert.Equal(services, _services);
            Assert.Equal(parts, _parts.ApplicationParts);
        }

        [Fact]
        public void LoadAll_TwoPluginsRegisteringTheSameFrameworkServices_LeaveOneOfEach()
        {
            // AddHttpClient fails once its registry is there twice, for the server and for the next plugin.
            _plugins.AddServerPlugin("first");
            _plugins.AddServerPlugin("second");
            _services.AddHttpClient();

            Assert.Equal(2, LoadAll().Loaded.Count);

            _services.AddHttpClient("the server's, after the plugins'");
            Assert.Single(_services, descriptor => descriptor.ServiceType == typeof(IHttpClientFactory));
        }

        [Fact]
        public void LoadAll_AnAssemblyThatIsNone_IsSkipped()
        {
            string folder = _plugins.Add("broken", TestPlugins.Manifest("broken", "broken.dll"));
            TestPlugins.Write(folder, "broken.dll", "no assembly");

            Assert.StartsWith("Its server assembly could not be loaded: ", SkipReason(), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void LoadAll_AnAssemblyWithoutExactlyOneEntry_IsSkipped(int entries)
        {
            Emit(assembly =>
            {
                assembly.AddUnfinishedEntry();
                for (int index = 0; index < entries; index++)
                {
                    assembly.AddEntry($"Entry{index}");
                }
            });

            Assert.Equal($"Its server assembly must hold one public class implementing IByteShelfPlugin; it holds {entries}.", SkipReason());
        }

        [Fact]
        public void LoadAll_AnEntryWhoseConstructorThrows_IsSkippedWithWhatItThrew()
        {
            Emit(assembly => assembly.AddThrowingEntry("It was told to fail."));

            Assert.Equal(TestPlugins.FailingReason, SkipReason());
        }

        [Fact]
        public void LoadAll_AnEntryWithoutAConstructorToCall_IsSkipped()
        {
            Emit(assembly => assembly.AddEntryMadeFromANumber());

            Assert.StartsWith("Registering its services failed: ", SkipReason(), StringComparison.Ordinal);
        }

        [Fact]
        public void LoadAll_ATypeWhoseBaseIsMissing_IsSkipped()
        {
            // Not one the entry needs, but the server looks through every type for controllers as it starts.
            Emit(assembly =>
            {
                assembly.AddEntry("Entry");
                assembly.AddInternalTypeWithAMissingBase();
            });

            Assert.StartsWith("Its server assembly could not be loaded: Could not load file or assembly 'Missing,", SkipReason(), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("{broken", false)]
        [InlineData(null, true)]
        public void LoadAll_AControllerTheServerWouldFailToBuild_IsSkippedLeavingNothingBehind(string? template, bool isApiController)
        {
            Emit(assembly =>
            {
                assembly.AddEntry("Entry");
                assembly.AddController("BrokenController", template, isApiController);
            });
            ServiceDescriptor[] services = _services.ToArray();
            ApplicationPart[] parts = _parts.ApplicationParts.ToArray();

            Assert.StartsWith("Its controllers could not be built: ", SkipReason(), StringComparison.Ordinal);
            Assert.Equal(services, _services);
            Assert.Equal(parts, _parts.ApplicationParts);
        }

        public void Dispose()
        {
            _plugins.Dispose();
        }

        private PluginCatalog LoadAll()
        {
            return PluginLoader.LoadAll(PluginDiscovery.Find(_plugins.Path, _logger), _services, _parts, _logger);
        }

        private PluginUi LoadUi()
        {
            return Assert.Single(LoadAll().Loaded).Ui!;
        }

        /// <summary>Why the one plugin there is skipped.</summary>
        private string SkipReason()
        {
            PluginCatalog catalog = LoadAll();
            Assert.Empty(catalog.Loaded);
            return Assert.Single(catalog.Skipped).Reason;
        }

        private string Greeting()
        {
            return _services.Single(descriptor => descriptor.ServiceType.Name == "PluginGreeting").ImplementationInstance!.ToString()!;
        }

        /// <summary>Makes a server plugin without its deps.json, so every library in its folder is one it could load.</summary>
        private string AddServerPluginLoadingAnythingBesideIt(string id)
        {
            string folder = _plugins.AddServerPlugin(id);
            File.Delete(Path.Combine(folder, Path.ChangeExtension(TestPlugins.ServerAssemblyName, ".deps.json")));
            return folder;
        }

        private void Emit(Action<EmittedPlugin> add)
        {
            string folder = _plugins.Add("emitted", TestPlugins.Manifest("emitted", "Emitted.dll"));
            EmittedPlugin.Write(Path.Combine(folder, "Emitted.dll"), add);
        }
    }
}
