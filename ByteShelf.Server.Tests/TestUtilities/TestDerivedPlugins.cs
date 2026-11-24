using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>The plugins that make derived files a test says are installed, run by <see cref="TestProgram"/>, and what they did.</summary>
    public sealed class TestDerivedPlugins : IDerivedFilePlugins, IDisposable
    {
        private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(60);

        private readonly string _folder = Directory.CreateTempSubdirectory("byteshelf-makers-").FullName;

        /// <summary>The tests' own executable: the dotnet they run on, whatever is on PATH, with their assembly.</summary>
        public static MakerExecutable Executable { get; } = new MakerExecutable(
            Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet")), [typeof(TestProgram).Assembly.Location]);

        public IReadOnlyList<DerivedFilePlugin> All { get; set; } = [];

        /// <summary>What runs the makers; another than <see cref="Executable"/> is set before <see cref="AddTo"/>.</summary>
        public MakerExecutable Program { get; set; } = Executable;

        private string LogPath => Path.Combine(_folder, "log");

        /// <summary>What the makers ran for, in order: the contents, then the kinds.</summary>
        public IReadOnlyList<string> Runs => File.Exists(LogPath) ? File.ReadAllLines(LogPath) : [];

        /// <summary>A plugin that makes <paramref name="kinds"/> (every one by default) from text files, or other types.</summary>
        public DerivedFilePlugin Plugin(string id = TestMaker.Spec.DefaultId, int version = 1, IEnumerable<DerivedKind>? kinds = null, params string[] mediaTypes)
        {
            return TestMaker.Plugin(new TestMaker.Spec(
                id, (kinds ?? DerivedKinds.All).Select(kind => new TestMaker.KindSpec(kind, mediaTypes.Length == 0 ? ["text/plain"] : mediaTypes, version)).ToList(), LogPath));
        }

        public IDerivedFileMaker? Load(string location)
        {
            return TestMaker.Load(location);
        }

        /// <summary>Installs these plugins in place of none, with <see cref="Program"/> to run them.</summary>
        public void AddTo(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton<IDerivedFilePlugins>(this));
            services.Replace(ServiceDescriptor.Singleton(Program));
        }

        /// <summary>Contents whose run waits for <see cref="GoOn"/>.</summary>
        public string Waiting(string name)
        {
            return $"wait {Path.Combine(_folder, name)}";
        }

        /// <summary>Contents whose run starts a process that goes on, and then does as <paramref name="then"/> says: <c>crash</c> or <c>wait</c>.</summary>
        public string WithChild(string name, string then)
        {
            return $"child {then} {Path.Combine(_folder, name)}";
        }

        /// <summary>Waits until the run of <see cref="Waiting"/>'s contents has read them; answers its process.</summary>
        public async Task<Process> StartedAsync(string name)
        {
            return Process.GetProcessById(await ProcessIdAsync(name + ".started"));
        }

        /// <summary>Waits until the run of <see cref="WithChild"/>'s contents has started the process that goes on; answers its id.</summary>
        public Task<int> ChildOfAsync(string name)
        {
            return ProcessIdAsync(name + ".child");
        }

        /// <summary>Whether a process runs: not one that has ended and that nobody has reaped.</summary>
        public static bool IsRunning(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return !process.HasExited && (!OperatingSystem.IsLinux() || !File.ReadAllText($"/proc/{processId}/stat").Contains(") Z ", StringComparison.Ordinal));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                return false;
            }
        }

        public void GoOn(string name)
        {
            File.WriteAllText(Path.Combine(_folder, name), string.Empty);
        }

        public static async Task UntilAsync(Func<bool> condition)
        {
            using CancellationTokenSource limit = new CancellationTokenSource(RealTimeLimit);
            while (!condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), limit.Token);
            }
        }

        public void Dispose()
        {
            Directory.Delete(_folder, recursive: true);
        }

        private async Task<int> ProcessIdAsync(string name)
        {
            string path = Path.Combine(_folder, name);
            await UntilAsync(() => File.Exists(path));
            return int.Parse(await File.ReadAllTextAsync(path), CultureInfo.InvariantCulture);
        }
    }
}
