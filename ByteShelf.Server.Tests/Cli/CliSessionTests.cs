using ByteShelf.Cli.Commands;
using ByteShelf.Cli.Config;
using ByteShelf.Cli.Connection;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfClient.Http;
using ByteShelfCommon;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Cli
{
    /// <summary>
    /// The CLI's login against the whole server: kept between commands, renewed one process at a
    /// time even by commands running side by side, and ended by logout. Each
    /// <see cref="ConfiguredClientSource"/> stands for one run of the <c>byteshelf</c> process.
    /// </summary>
    public sealed class CliSessionTests : IAsyncLifetime
    {
        private const string Server = "http://localhost/";

        private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "cli-sessions", Guid.NewGuid().ToString("N"));
        private ServerTestHost _host = null!;
        private HttpMessageHandler _handler = null!;
        private CliConfigStore _store = null!;

        private SessionTokens KeptTokens => Assert.IsType<SessionLogin>(_store.Load()!.Login).Tokens;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            _handler = _host.CreateHandler();
            _store = new CliConfigStore(Path.Combine(_folder, "config.json"));
        }

        public async Task DisposeAsync()
        {
            _handler.Dispose();
            await _host.DisposeAsync();
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        [Fact]
        public async Task Login_ThenALaterCommand_UsesTheKeptSessionWithoutRenewing()
        {
            await SignInAsync();
            string refreshToken = KeptTokens.RefreshToken;

            using ConfiguredClientSource later = Process();
            UserInfo me = await (await later.OpenAsync(CancellationToken.None)).GetCurrentUserAsync();

            Assert.Equal(ServerTestHost.AdminEmail, me.Email);
            Assert.Equal(refreshToken, KeptTokens.RefreshToken);
        }

        [Fact]
        public async Task KeptSessionAboutToRunOut_IsRenewedAndTheNewTokensKept()
        {
            await SignInAsync();
            string refreshToken = KeptTokens.RefreshToken;

            using (ConfiguredClientSource later = Process(NearExpiry()))
            {
                await (await later.OpenAsync(CancellationToken.None)).GetCurrentUserAsync();
            }

            Assert.NotEqual(refreshToken, KeptTokens.RefreshToken);
            await AssertSignedInAsync();
        }

        [Fact]
        public async Task CommandsSideBySide_SessionDue_RenewOneAtATimeAndKeepItWorking()
        {
            await SignInAsync();
            string refreshToken = KeptTokens.RefreshToken;
            using ConfiguredClientSource first = Process(NearExpiry());
            using ConfiguredClientSource second = Process(NearExpiry());

            // Renewing twice with the same refresh token would end every session in its family.
            await Task.WhenAll(
                (await first.OpenAsync(CancellationToken.None)).GetCurrentUserAsync(),
                (await second.OpenAsync(CancellationToken.None)).GetCurrentUserAsync());

            Assert.NotEqual(refreshToken, KeptTokens.RefreshToken);
            await AssertSignedInAsync();
        }

        [Fact]
        public async Task LongRunningProcess_AnotherRenewedMeanwhile_CarriesOnFromTheirTokens()
        {
            await SignInAsync();
            using ConfiguredClientSource mcpServer = Process(NearExpiry());
            IByteShelfClient client = await mcpServer.OpenAsync(CancellationToken.None);
            await client.GetCurrentUserAsync();
            using (ConfiguredClientSource command = Process(NearExpiry()))
            {
                await (await command.OpenAsync(CancellationToken.None)).GetCurrentUserAsync();
            }

            // Due again by its clock; renewing from the tokens it renewed to earlier would be a reuse.
            UserInfo me = await (await mcpServer.OpenAsync(CancellationToken.None)).GetCurrentUserAsync();

            Assert.Equal(ServerTestHost.AdminEmail, me.Email);
            await AssertSignedInAsync();
        }

        [Fact]
        public async Task Logout_EndsTheSessionOnTheServerAndForgetsIt()
        {
            await SignInAsync();
            SessionTokens kept = KeptTokens;

            using (ConfiguredClientSource logout = Process())
            {
                await logout.SignOutAsync(CancellationToken.None);
            }

            Assert.False(File.Exists(_store.FilePath));
            using HttpClient http = new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri(Server) };
            SessionTokens runOut = new SessionTokens { Token = kept.Token, ExpiresAt = DateTimeOffset.MinValue, RefreshToken = kept.RefreshToken };
            using UserSession resumed = UserSession.Resume(http, runOut);
            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(() => new ByteShelfHttpClient(http, resumed).GetCurrentUserAsync());
            Assert.Equal(ErrorCodes.Unauthenticated, exception.Code);
        }

        [Fact]
        public async Task LoginWithAKey_WrongKey_KeepsNothing()
        {
            using ConfiguredClientSource login = Process();

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => login.UseKeyAsync(Server, ShelfKeys.SecretPrefix + "not-a-key", CancellationToken.None));

            Assert.Equal(ErrorCodes.Unauthenticated, exception.Code);
            Assert.False(File.Exists(_store.FilePath));
        }

        [Fact]
        public async Task CliApp_LoginWithAKey_ThenWhoAmIShowsItsShelf()
        {
            await SignInAsync();
            using (ConfiguredClientSource admin = Process())
            {
                await (await admin.OpenAsync(CancellationToken.None)).CreateShelfAsync("docs");
            }

            string secret = await CreateKeyAsync("docs");
            await RunAsync(new[] { "login", Server, "--key" }, secret + Environment.NewLine);
            string whoami = await RunAsync(new[] { "whoami" });
            await RunAsync(new[] { "logout" });

            Assert.Equal($"{Server} with a shelf key{Environment.NewLine}docs  0 B used  0 files", whoami);
            Assert.False(File.Exists(_store.FilePath));
        }

        [Fact]
        public async Task CliApp_LoginWithThePasswordOnStdin_ThenPutAndLs()
        {
            await RunAsync(new[] { "login", Server, "--email", ServerTestHost.AdminEmail }, ServerTestHost.Password + Environment.NewLine);
            await RunAsync(new[] { "shelf", "create", "docs" });
            await RunAsync(new[] { "put", "-", "docs:/notes/a.txt" }, "hello");

            string listing = await RunAsync(new[] { "ls", "docs:/notes" });

            Assert.Contains("a.txt  5 B", listing, StringComparison.Ordinal);
        }

        private ConfiguredClientSource Process(TimeProvider? clock = null)
        {
            return new ConfiguredClientSource(_store, _ => null, _handler, clock);
        }

        private async Task SignInAsync()
        {
            using ConfiguredClientSource login = Process();
            await login.SignInAsync(Server, ServerTestHost.AdminEmail, ServerTestHost.Password, CancellationToken.None);
        }

        private async Task<string> CreateKeyAsync(string shelf)
        {
            using ConfiguredClientSource admin = Process();
            return (await (await admin.OpenAsync(CancellationToken.None)).Shelf(shelf).CreateKeyAsync("job", ShelfKeyPermission.Read)).Secret;
        }

        /// <summary>The kept session still works for a fresh process with the real clock.</summary>
        private async Task AssertSignedInAsync()
        {
            using ConfiguredClientSource afterwards = Process();
            Assert.Equal(ServerTestHost.AdminEmail, (await (await afterwards.OpenAsync(CancellationToken.None)).GetCurrentUserAsync()).Email);
        }

        /// <summary>A clock a second before the kept access token runs out, which is due for renewal by then.</summary>
        private FakeTimeProvider NearExpiry()
        {
            return new FakeTimeProvider(KeptTokens.ExpiresAt.AddSeconds(-1));
        }

        /// <summary>Runs one command line as its own process and answers what it printed; fails the test unless it succeeded.</summary>
        /// <remarks>
        /// Much like CliRun in ByteShelf.Cli.Tests. Kept as a copy: sharing it would take a test
        /// utilities project of its own for these few lines.
        /// </remarks>
        private async Task<string> RunAsync(string[] commandLine, string input = "")
        {
            using ConfiguredClientSource clients = Process();
            using StringReader inputText = new StringReader(input);
            using StringWriter output = new StringWriter();
            using StringWriter error = new StringWriter();
            Terminal terminal = new Terminal(
                inputText,
                output,
                error,
                interactive: false,
                () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input)),
                () => Stream.Null);

            int exitCode = await new CliApp(clients, terminal).RunAsync(commandLine, CancellationToken.None);
            Assert.True(exitCode == CliApp.Succeeded, $"byteshelf {string.Join(' ', commandLine)}: {error}");
            return output.ToString().TrimEnd();
        }
    }
}
