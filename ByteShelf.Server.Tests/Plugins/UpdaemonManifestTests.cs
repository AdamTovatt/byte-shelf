using System.Text.Json;
using ByteShelf.Server.Plugins.Services;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>What <c>updaemon.json</c> promises updaemon of plugins, which nothing reads before a release is installed.</summary>
    public sealed class UpdaemonManifestTests
    {
        private static readonly string Repository = FindRepository();

        private static readonly JsonElement Manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository, "ByteShelf.Server", "updaemon.json"))).RootElement;

        private static readonly JsonElement Extensions = Manifest.GetProperty("extensions");

        [Fact]
        public void Extensions_NameTheVariableAndTheCheckTheServerHas()
        {
            Assert.Equal(EnvironmentVariables.Plugins.Name, Extensions.GetProperty("variable").GetString());
            Assert.Equal([PluginCheckCommand.Flag], Extensions.GetProperty("check").EnumerateArray().Select(argument => argument.GetString()));
        }

        [Fact]
        public void Settings_LeaveOutTheVariable_WhichUpdaemonRefusesBesideExtensions()
        {
            Assert.DoesNotContain(EnvironmentVariables.Plugins.Name, Manifest.GetProperty("settings").EnumerateObject().Select(setting => setting.Name));
        }

        [Fact]
        public void Extensions_KnowEachPluginMadeHere_ByTheAssetReleaseGivesIt()
        {
            IEnumerable<(string, string)> made = Directory.EnumerateDirectories(Path.Combine(Repository, "plugins"))
                .Where(folder => File.Exists(Path.Combine(folder, "plugin.json")))
                .Select(folder => Path.GetFileName(folder))
                .Select(id => (id, $"github:AdamTovatt/byte-shelf/byteshelf-{id}.zip"));
            IEnumerable<(string, string)> known = Extensions.TryGetProperty("known", out JsonElement entries)
                ? entries.EnumerateObject().Select(entry => (entry.Name, entry.Value.GetString()!))
                : [];

            Assert.Equal(made.Order(), known.Order());
        }

        private static string FindRepository()
        {
            DirectoryInfo? folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "ByteShelf.slnx")))
            {
                folder = folder.Parent;
            }

            return folder?.FullName ?? throw new InvalidOperationException("The tests run from a build in the repository.");
        }
    }
}
