using System.Net;
using ByteShelf.Cli.Config;
using ByteShelf.Cli.Connection;
using ByteShelf.Cli.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Cli.Tests
{
    /// <summary>
    /// The config file, and how it and the environment choose the server and the credentials each
    /// request carries. A <see cref="ScriptedHandler"/> stands in for the servers.
    /// </summary>
    public sealed class ConfigTests : IDisposable
    {
        private const string ShelvesJson = "[]";
        private const string UserJson = "{\"id\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"email\":\"a@b.test\",\"displayName\":\"A\",\"roles\":[]}";

        private readonly WorkFolder _work = new WorkFolder();
        private readonly ScriptedHandler _servers = new ScriptedHandler(request => ScriptedHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal) ? UserJson : ShelvesJson));

        private CliConfigStore Store => new CliConfigStore(_work.File("config/config.json"));

        public void Dispose()
        {
            _servers.Dispose();
            _work.Dispose();
        }

        [Fact]
        public void Load_NoFileYet_AnswersNull()
        {
            Assert.Null(Store.Load());
        }

        [Fact]
        public void Save_ThenLoad_KeepsTheSession()
        {
            Store.Save(SessionConfig("https://files.test/", "t", "r"));

            CliConfig loaded = Store.Load()!;

            SessionLogin login = Assert.IsType<SessionLogin>(loaded.Login);
            Assert.Equal(("https://files.test/", "a@b.test", "t", "r"), (loaded.Server, login.Email, login.Tokens.Token, login.Tokens.RefreshToken));
        }

        [Fact]
        public async Task SaveAndLock_OnUnix_OnlyTheOwnerCanReachTheFileOrItsFolder()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            await using (await Store.LockAsync(CancellationToken.None))
            {
                Store.Save(KeyConfig("https://files.test/", "bsk_secret"));
            }

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Store.FilePath));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(Store.FilePath)!));
        }

        [Fact]
        public void Load_DamagedFile_ThrowsUsageNamingIt()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Store.FilePath)!);
            File.WriteAllText(Store.FilePath, "{ not json");

            UsageException exception = Assert.Throws<UsageException>(() => Store.Load());

            Assert.Contains(Store.FilePath, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Lock_HeldByOne_MakesTheNextWait()
        {
            IAsyncDisposable first = await Store.LockAsync(CancellationToken.None);
            Task<IAsyncDisposable> second = Store.LockAsync(CancellationToken.None);
            await Task.Delay(200);
            bool waited = !second.IsCompleted;

            await first.DisposeAsync();
            await (await second).DisposeAsync();

            Assert.True(waited);
        }

        [Fact]
        public async Task Open_ServerAndKeyInTheEnvironment_UsesThemOverTheFile()
        {
            Store.Save(KeyConfig("https://files.test/", "bsk_file"));
            using ConfiguredClientSource clients = Clients(Variables((ConfiguredClientSource.ServerVariable, "https://other.test"), (ConfiguredClientSource.KeyVariable, "bsk_env")));

            await (await clients.OpenAsync(CancellationToken.None)).GetShelvesAsync();

            Assert.Equal(new[] { "GET https://other.test/api/shelves bsk_env" }, _servers.Requests);
        }

        [Fact]
        public async Task Open_KeyAloneInTheEnvironment_UsesItWithTheKeptServer()
        {
            Store.Save(SessionConfig("https://files.test/", "t", "r"));
            using ConfiguredClientSource clients = Clients(Variables((ConfiguredClientSource.KeyVariable, "bsk_env")));

            await (await clients.OpenAsync(CancellationToken.None)).GetShelvesAsync();

            Assert.Equal(new[] { "GET https://files.test/api/shelves bsk_env" }, _servers.Requests);
        }

        [Fact]
        public async Task Open_ServerInTheEnvironmentWithoutAKey_ThrowsRatherThanUseTheFilesLogin()
        {
            Store.Save(KeyConfig("https://files.test/", "bsk_file"));
            using ConfiguredClientSource clients = Clients(Variables((ConfiguredClientSource.ServerVariable, "https://other.test/")));

            await Assert.ThrowsAsync<UsageException>(() => clients.OpenAsync(CancellationToken.None));
            Assert.Empty(_servers.Requests);
        }

        [Fact]
        public async Task Open_NothingConfigured_ThrowsNotSignedIn()
        {
            using ConfiguredClientSource clients = Clients(NoVariables);

            UsageException exception = await Assert.ThrowsAsync<UsageException>(() => clients.OpenAsync(CancellationToken.None));

            Assert.StartsWith("Not signed in", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Open_LoginChangedSinceTheLastCall_FollowsIt()
        {
            using ConfiguredClientSource mcpServer = Clients(NoVariables);
            Store.Save(SessionConfig("https://first.test/", "first-token", "r1"));
            await (await mcpServer.OpenAsync(CancellationToken.None)).GetCurrentUserAsync();

            Store.Save(SessionConfig("https://second.test/", "second-token", "r2"));
            await (await mcpServer.OpenAsync(CancellationToken.None)).GetCurrentUserAsync();
            Store.Save(KeyConfig("https://second.test/", "bsk_key"));
            await (await mcpServer.OpenAsync(CancellationToken.None)).GetShelvesAsync();
            Store.Delete();

            await Assert.ThrowsAsync<UsageException>(() => mcpServer.OpenAsync(CancellationToken.None));
            Assert.Equal(
                new[] { "GET https://first.test/api/users/me first-token", "GET https://second.test/api/users/me second-token", "GET https://second.test/api/shelves bsk_key" },
                _servers.Requests);
        }

        [Theory]
        [InlineData("session", null, null, "https://files.test/ as a@b.test")]
        [InlineData("key", null, null, "https://files.test/ with a shelf key")]
        [InlineData("session", null, "bsk_env", "https://files.test/ with the key in BYTESHELF_KEY")]
        [InlineData(null, "https://other.test", "bsk_env", "https://other.test/ with the key in BYTESHELF_KEY")]
        [InlineData(null, null, null, null)]
        public void Describe_SaysWhichLoginIsInEffect(string? kept, string? serverVariable, string? keyVariable, string? expected)
        {
            if (kept != null)
            {
                Store.Save(kept == "key" ? KeyConfig("https://files.test/", "bsk_file") : SessionConfig("https://files.test/", "t", "r"));
            }

            using ConfiguredClientSource clients = Clients(Variables((ConfiguredClientSource.ServerVariable, serverVariable), (ConfiguredClientSource.KeyVariable, keyVariable)));

            Assert.Equal(expected, clients.Describe());
        }

        [Fact]
        public async Task SignOut_KeyLogin_ForgetsItWithoutAskingTheServer()
        {
            Store.Save(KeyConfig("https://files.test/", "bsk_file"));
            using ConfiguredClientSource clients = Clients(NoVariables);

            await clients.SignOutAsync(CancellationToken.None);

            Assert.False(File.Exists(Store.FilePath));
            Assert.Empty(_servers.Requests);
        }

        [Fact]
        public async Task SignOut_ServerUnreachable_StillForgetsTheSession()
        {
            Store.Save(SessionConfig("https://files.test/", "t", "r"));
            using ScriptedHandler unreachable = new ScriptedHandler(_ => throw new HttpRequestException("No route to host."));
            using ConfiguredClientSource clients = new ConfiguredClientSource(Store, NoVariables, unreachable);

            await clients.SignOutAsync(CancellationToken.None);

            Assert.False(File.Exists(Store.FilePath));
        }

        [Fact]
        public async Task SignOut_SessionAlreadyOver_StillForgetsIt()
        {
            Store.Save(SessionConfig("https://files.test/", "t", "r"));
            using ScriptedHandler refusing = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            using ConfiguredClientSource clients = new ConfiguredClientSource(Store, NoVariables, refusing);

            await clients.SignOutAsync(CancellationToken.None);

            Assert.False(File.Exists(Store.FilePath));
        }

        [Fact]
        public async Task SignOut_DamagedFile_RemovesIt()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Store.FilePath)!);
            File.WriteAllText(Store.FilePath, "{ not json");
            using ConfiguredClientSource clients = Clients(NoVariables);

            await clients.SignOutAsync(CancellationToken.None);

            Assert.False(File.Exists(Store.FilePath));
        }

        [Theory]
        [InlineData("https://files.test", "https://files.test/")]
        [InlineData("http://localhost:5000/byteshelf", "http://localhost:5000/byteshelf/")]
        [InlineData("https://files.test/?x=1", "https://files.test/")]
        public void NormalizeServer_EndsTheAddressInASlash(string server, string expected)
        {
            Assert.Equal(expected, ConfiguredClientSource.NormalizeServer(server));
        }

        [Theory]
        [InlineData("files.test")]
        [InlineData("ftp://files.test/")]
        public void NormalizeServer_NotAnHttpAddress_ThrowsUsage(string server)
        {
            Assert.Throws<UsageException>(() => ConfiguredClientSource.NormalizeServer(server));
        }

        private static string? NoVariables(string name)
        {
            return null;
        }

        private static Func<string, string?> Variables(params (string Name, string? Value)[] variables)
        {
            return name => variables.FirstOrDefault(variable => variable.Name == name).Value;
        }

        private static CliConfig KeyConfig(string server, string key)
        {
            return new CliConfig { Server = server, Login = new KeyLogin { Key = key } };
        }

        /// <summary>A session whose access token works for another hour.</summary>
        private static CliConfig SessionConfig(string server, string token, string refreshToken)
        {
            SessionTokens tokens = new SessionTokens { Token = token, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), RefreshToken = refreshToken };
            return new CliConfig { Server = server, Login = new SessionLogin { Email = "a@b.test", Tokens = tokens } };
        }

        private ConfiguredClientSource Clients(Func<string, string?> variables)
        {
            return new ConfiguredClientSource(Store, variables, _servers);
        }
    }
}
