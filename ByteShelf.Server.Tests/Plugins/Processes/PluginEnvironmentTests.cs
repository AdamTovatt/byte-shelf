using System.Diagnostics;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.Derived;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>What a plugin's process has of the server's environment.</summary>
    [Collection(nameof(ProcessStateCollection))]
    public sealed class PluginEnvironmentTests : DerivedTestBase
    {
        private const string Secret = "BYTESHELF_SECRET_FOR_TESTS";

        public override async Task InitializeAsync()
        {
            Environment.SetEnvironmentVariable(Secret, "secret");
            await base.InitializeAsync();
        }

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();
            Environment.SetEnvironmentVariable(Secret, null);
        }

        [LinuxFact]
        public async Task RunAsync_HasNothingOfTheServersEnvironmentAndNoDumpMadeOfItsMemory()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = MakeAsync(file);
            using (Process process = await Plugins.StartedAsync("a"))
            {
                string[] environment = (await File.ReadAllTextAsync($"/proc/{process.Id}/environ")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                Assert.Contains("DOTNET_DbgEnableMiniDump=0", environment);
                Assert.Contains("DOTNET_EnableDiagnostics=0", environment);
                Assert.Contains("DOTNET_gcConcurrent=0", environment);
                Assert.All(environment.Select(variable => variable.Split('=')[0]), name => Assert.Matches("^(PATH|LANG|LANGUAGE|TZ|TMPDIR|TMP|TEMP|LC_.*|DOTNET_.*)$", name));
                Plugins.GoOn("a");
            }

            await making;
        }
    }
}
