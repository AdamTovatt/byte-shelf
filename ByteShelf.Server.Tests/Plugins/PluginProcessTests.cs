using System.Diagnostics;
using System.Runtime.InteropServices;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>Plugins and the server as a process of its own, its arguments and environment included.</summary>
    public sealed class PluginProcessTests : IDisposable
    {
        private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

        private readonly TestPlugins _plugins = new TestPlugins();

        /// <summary>The folder the server is started in, not its own.</summary>
        private string Elsewhere => _plugins.InHome("elsewhere");

        [Fact]
        public async Task CheckPlugin_WithPathAndHomeAloneFromAnyFolder_AnswersItsExitCode()
        {
            // What lies in the folder it is started from is not the server's.
            TestPlugins.Write(Elsewhere, "appsettings.json", "not settings");

            Assert.Equal((0, string.Empty), await CheckAsync([_plugins.AddServerPlugin("good")]));
            Assert.Equal((1, TestPlugins.FailingReason), await CheckAsync([_plugins.AddFailingServerPlugin("failing")]));
            Assert.Equal(2, (await CheckAsync([])).ExitCode);
        }

        [Fact]
        public async Task CheckPlugin_MakesNothingWhereItIsStarted()
        {
            Assert.Equal((0, string.Empty), await CheckAsync([_plugins.AddServerPlugin("good")]));

            // Such as a root.
            Assert.Empty(Directory.EnumerateFileSystemEntries(Elsewhere));
        }

        [Fact]
        public async Task CheckPlugin_WithSettingsTheServerWouldRefuse_ReadsNone()
        {
            string good = _plugins.AddServerPlugin("good");

            Assert.Equal((0, string.Empty), await CheckAsync([good], environment =>
            {
                environment["APP_BASE_URL"] = "no address";
                environment["BYTESHELF_RESTORE_WINDOW_DAYS"] = "no days";
                environment["BYTESHELF_REPLICATION_ALERT_MINUTES"] = "no minutes";
                environment["BYTESHELF_TRUSTED_PROXIES"] = "no proxy";
                environment["POSTMARK_API_KEY"] = "a token without a sender";
            }));
        }

        [Fact]
        public async Task Start_WithThePluginsVariable_LoadsThem()
        {
            _plugins.Add("markdown", TestPlugins.Manifest("markdown"));
            using Process server = Start([], environment =>
            {
                environment["BYTESHELF_ROOT"] = _plugins.InHome("root");
                environment["BYTESHELF_PLUGINS"] = _plugins.Path;
                environment["APP_BASE_URL"] = "http://localhost/";
                environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
            });
            using CancellationTokenSource patience = new CancellationTokenSource(Patience);

            // Read as the server writes it, so it never waits on a full pipe.
            Task<string> errors = server.StandardError.ReadToEndAsync(patience.Token);
            bool loaded = false;
            try
            {
                while (!loaded && await server.StandardOutput.ReadLineAsync(patience.Token) is { } line)
                {
                    loaded = line.Contains("Loaded the plugin markdown 1.2.3", StringComparison.Ordinal);
                }
            }
            finally
            {
                server.Kill(entireProcessTree: true);
            }

            Assert.True(loaded, await errors);
            await server.WaitForExitAsync(patience.Token);
        }

        public void Dispose()
        {
            _plugins.Dispose();
        }

        private async Task<(int ExitCode, string Errors)> CheckAsync(IEnumerable<string> arguments, Action<IDictionary<string, string?>>? addEnvironment = null)
        {
            using Process check = Start(["--check-plugin", .. arguments], addEnvironment);
            using CancellationTokenSource patience = new CancellationTokenSource(Patience);
            Task<string> output = check.StandardOutput.ReadToEndAsync(patience.Token);
            string errors = await check.StandardError.ReadToEndAsync(patience.Token);
            await output;
            await check.WaitForExitAsync(patience.Token);
            return (check.ExitCode, errors.Trim());
        }

        private Process Start(IEnumerable<string> arguments, Action<IDictionary<string, string?>>? addEnvironment)
        {
            Directory.CreateDirectory(Elsewhere);

            // The dotnet these tests run on, whatever is on PATH.
            string dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet"));
            ProcessStartInfo start = new ProcessStartInfo(dotnet, [Path.Combine(AppContext.BaseDirectory, "ByteShelf.Server.dll"), .. arguments])
            {
                WorkingDirectory = Elsewhere,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            start.Environment.Clear();
            start.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH");
            start.Environment["HOME"] = Environment.GetEnvironmentVariable("HOME");
            addEnvironment?.Invoke(start.Environment);
            return Process.Start(start)!;
        }
    }
}
