using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Users
{
    /// <summary>
    /// The server's user commands, run against the root of a server running in memory, and checked
    /// through that server's API: signing in, and what an admin may do.
    /// </summary>
    public sealed class UserCommandTests : IAsyncLifetime
    {
        private const string NewEmail = "new@byteshelf.test";
        private const string NewPassword = "a-new-password";
        private const string OtherAdminEmail = "other-admin@byteshelf.test";

        // One of each command, which can succeed on the host's users once OtherAdminEmail is an admin too.
        private const string CreateUser = "--create-user " + NewEmail + " --name New";
        private const string ResetPassword = "--reset-password " + ServerTestHost.MemberEmail;
        private const string SetAdmin = "--set-admin " + ServerTestHost.MemberEmail;
        private const string UnsetAdmin = "--unset-admin " + ServerTestHost.AdminEmail;

        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task CreateUser_WithAdmin_CreatesAnAdminWhoCanSignInToTheRunningServer()
        {
            int exitCode = await RunAsync(new Passwords(NewPassword), "--create-user", NewEmail, "--name", "New", "--admin");

            Assert.Equal(RootCommand.Done, exitCode);
            HttpClient client = await _host.CreateSignedInClientAsync(NewEmail, NewPassword);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
        }

        [Fact]
        public async Task CreateUser_WithoutAdmin_CreatesAUserWhoIsNotAnAdmin()
        {
            int exitCode = await RunAsync(new Passwords(NewPassword), "--create-user", NewEmail, "--name", "New");

            Assert.Equal(RootCommand.Done, exitCode);
            HttpClient client = await _host.CreateSignedInClientAsync(NewEmail, NewPassword);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        }

        [Fact]
        public async Task CreateUser_RecordsTheUserInTheChangeLog()
        {
            long before = await LatestChangeAsync();

            await RunAsync(new Passwords(NewPassword), "--create-user", NewEmail, "--name", "New");

            ChangeLogEntry added = Assert.Single(await ChangesAfterAsync(before), change => (change.EntityType, change.Action) == (ChangeEntityType.User, ChangeAction.Added));
            Assert.Equal(NewEmail, ChangePayload.Deserialize<User>(added.Payload!).Email);
        }

        [Fact]
        public async Task CreateUser_TakenEmail_FailsSayingSoWithoutAskingForAPassword()
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();
            Passwords passwords = new Passwords(NewPassword);

            int exitCode = await RunAsync(passwords, logs, "--create-user", ServerTestHost.MemberEmail, "--name", "Again");

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Contains($"A user with the email '{ServerTestHost.MemberEmail}' already exists.", logs.Messages);
            Assert.Empty(passwords.AskedFor);
        }

        [Fact]
        public async Task CreateUser_BlankName_FailsWithoutAskingForAPassword()
        {
            Passwords passwords = new Passwords(NewPassword);

            int exitCode = await RunAsync(passwords, "--create-user", NewEmail, "--name", " ");

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Empty(passwords.AskedFor);
        }

        [Fact]
        public async Task CreateUser_PasswordTheRulesRefuse_FailsAndCreatesNoOne()
        {
            int exitCode = await RunAsync(new Passwords("short"), "--create-user", NewEmail, "--name", "New");

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _host.TrySignInAsync(NewEmail, "short")).StatusCode);
        }

        [Fact]
        public async Task CreateUser_NoPasswordComes_FailsSayingSoAndCreatesNoOne()
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(new Passwords(), logs, "--create-user", NewEmail, "--name", "New");

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Contains(Passwords.NoneCame, logs.Messages);
            using IServiceScope scope = _host.Services.CreateScope();
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IUserService>().GetUserByEmailAsync(NewEmail));
        }

        [Fact]
        public async Task ResetPassword_SetsTheNewOneAndEndsTheUsersSessions()
        {
            HttpResponseMessage before = await _host.TrySignInAsync(ServerTestHost.MemberEmail, ServerTestHost.Password);
            string refreshToken = (await before.Content.ReadFromJsonAsync<SessionTokens>())!.RefreshToken;

            int exitCode = await RunAsync(new Passwords(NewPassword), "--reset-password", ServerTestHost.MemberEmail);

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _host.TrySignInAsync(ServerTestHost.MemberEmail, ServerTestHost.Password)).StatusCode);
            await _host.CreateSignedInClientAsync(ServerTestHost.MemberEmail, NewPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _host.AnonymousClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode);
        }

        [Fact]
        public async Task ResetPassword_RefusesAnAccessTokenIssuedBeforeIt()
        {
            Assert.Equal(HttpStatusCode.OK, (await _host.MemberClient.GetAsync("/api/users/me")).StatusCode);

            int exitCode = await RunAsync(new Passwords(NewPassword), "--reset-password", ServerTestHost.MemberEmail);

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _host.MemberClient.GetAsync("/api/users/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _host.AdminClient.GetAsync("/api/users/me")).StatusCode);
        }

        [Fact]
        public async Task ResetPassword_UnknownEmail_FailsWithoutAskingForAPassword()
        {
            Passwords passwords = new Passwords(NewPassword);

            int exitCode = await RunAsync(passwords, "--reset-password", NewEmail);

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Empty(passwords.AskedFor);
        }

        [Fact]
        public async Task SetAdmin_MakesTheUserAnAdminOnTheirNextRequest()
        {
            HttpClient member = await _host.CreateSignedInClientAsync(ServerTestHost.MemberEmail);

            int exitCode = await RunAsync(new Passwords(), "--set-admin", ServerTestHost.MemberEmail);

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/users")).StatusCode);
        }

        [Fact]
        public async Task UnsetAdmin_WithAnotherAdmin_TakesAdminAway()
        {
            Assert.Equal(RootCommand.Done, await RunAsync(new Passwords(), "--set-admin", ServerTestHost.MemberEmail));

            int exitCode = await RunAsync(new Passwords(), "--unset-admin", ServerTestHost.AdminEmail);

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _host.AdminClient.GetAsync("/api/users")).StatusCode);
        }

        [Fact]
        public async Task UnsetAdmin_TheOnlyAdmin_FailsAndKeepsThem()
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(new Passwords(), logs, "--unset-admin", ServerTestHost.AdminEmail);

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Contains("This is the only admin; make someone else an admin first.", logs.Messages);
            Assert.Equal(HttpStatusCode.OK, (await _host.AdminClient.GetAsync("/api/users")).StatusCode);
        }

        [Theory]
        [InlineData("--set-admin", ServerTestHost.AdminEmail, $"{ServerTestHost.AdminEmail} is already an admin.")]
        [InlineData("--unset-admin", ServerTestHost.MemberEmail, $"{ServerTestHost.MemberEmail} is not an admin.")]
        public async Task SetOrUnsetAdmin_AlreadySo_SaysSoAndChangesNothing(string command, string email, string said)
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();
            long before = await LatestChangeAsync();

            int exitCode = await RunAsync(new Passwords(), logs, command, email);

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.Contains(said, logs.Messages);
            Assert.Empty(await ChangesAfterAsync(before));
        }

        [Theory]
        [InlineData("--set-admin")]
        [InlineData("--unset-admin")]
        public async Task SetOrUnsetAdmin_UnknownEmail_FailsSayingSo(string command)
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(new Passwords(), logs, command, NewEmail);

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Contains($"No user has the email '{NewEmail}'.", logs.Messages);
        }

        [Theory]
        [InlineData(CreateUser)]
        [InlineData(ResetPassword)]
        [InlineData(SetAdmin)]
        [InlineData(UnsetAdmin)]
        public async Task AnyCommand_OnAReplica_FailsWithoutAskingForAPasswordAndChangesNothing(string command)
        {
            await CreateOtherAdminAsync();
            await TestReplicas.MarkAsReplicaAsync(_host.Services);

            long before = await LatestChangeAsync();
            Passwords passwords = new Passwords(NewPassword);

            int exitCode = await RunAsync(passwords, command.Split(' '));

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Empty(passwords.AskedFor);
            Assert.Empty(await ChangesAfterAsync(before));
        }

        [Theory]
        [InlineData(CreateUser)]
        [InlineData(ResetPassword)]
        [InlineData(SetAdmin)]
        [InlineData(UnsetAdmin)]
        public async Task AnyCommand_WithTheServerStopped_ChangesTheRootAndLeavesNoWriteAheadLog(string command)
        {
            await CreateOtherAdminAsync();
            long before = await LatestChangeAsync();
            await _host.StopAsync();

            int exitCode = await RunAsync(new Passwords(NewPassword), command.Split(' '));

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.False(File.Exists(_host.TestRoot.Root.DatabasePath + "-wal"));
            await using ServiceProvider services = UserCommand.BuildServices(_host.TestRoot.Root, NullLoggerFactory.Instance);
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            IReadOnlyList<ChangeLogEntry> changes = await scope.ServiceProvider.GetRequiredService<IChangeLogRepository>().GetAfterAsync(before, 100);
            Assert.NotEmpty(changes);
            Assert.All(changes, change => Assert.Contains(change.EntityType, new[] { ChangeEntityType.User, ChangeEntityType.UserLogin }));
        }

        [Fact]
        public async Task AnyCommand_OnADatabaseAMigrationBehind_FailsWithoutMigratingIt()
        {
            const string CountApplied = "SELECT COUNT(*) FROM SchemaVersions";
            long applied = (long)(await ExecuteAsync(CountApplied))!;
            await ExecuteAsync("DELETE FROM SchemaVersions WHERE SchemaVersionID = (SELECT MAX(SchemaVersionID) FROM SchemaVersions)");
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(new Passwords(), logs, "--set-admin", ServerTestHost.MemberEmail);

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Contains(logs.Messages, message => message.Contains("--migrate-only", StringComparison.Ordinal));
            Assert.Equal(applied - 1, await ExecuteAsync(CountApplied));
        }

        [Fact]
        public async Task AnyCommand_OnAFolderWithoutADatabase_FailsSayingSo()
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();
            string folder = _host.TestRoot.Root.ObjectsPath;

            int exitCode = await UserCommand.RunAsync(["--set-admin", ServerTestHost.MemberEmail], folder, new Passwords(), logs);

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Contains($"There is no ByteShelf database in {folder}.", logs.Messages);
            Assert.False(RootFolder.HasDatabase(folder));
        }

        [Fact]
        public async Task CreateUser_OnAFolderNoServerHasStartedOn_MakesTheRootAndTheFirstAdmin()
        {
            string folder = Path.Combine(_host.TestRoot.Root.Path, "new-root");

            int exitCode = await UserCommand.RunAsync(["--create-user", NewEmail, "--name", "New", "--admin"], folder, new Passwords(NewPassword), new RecordingLoggerFactory());

            Assert.Equal(RootCommand.Done, exitCode);
            RootFolder root = RootFolder.OpenExisting(folder);
            Assert.False(File.Exists(root.DatabasePath + "-wal"));
            Assert.True(DatabaseMigrator.IsUpToDate(root.ConnectionString));
            await using ServiceProvider services = UserCommand.BuildServices(root, NullLoggerFactory.Instance);
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            User? created = await scope.ServiceProvider.GetRequiredService<IUserService>().GetUserByEmailAsync(NewEmail);
            Assert.Equal([UserRoles.Admin], created?.Roles);
        }

        [Fact]
        public async Task CreateUser_RefusedOnAFolderNoServerHasStartedOn_LeavesNoRoot()
        {
            string folder = Path.Combine(_host.TestRoot.Root.Path, "new-root");

            int exitCode = await UserCommand.RunAsync(["--create-user", "not-an-email", "--name", "New", "--admin"], folder, new Passwords(NewPassword), new RecordingLoggerFactory());

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.False(Directory.Exists(folder));
        }

        /// <summary>A folder made for the server beforehand stays, as it was: only what the command made goes.</summary>
        [Fact]
        public async Task CreateUser_RefusedOnAnEmptyFolderMadeForTheServer_LeavesItEmpty()
        {
            string folder = Path.Combine(_host.TestRoot.Root.Path, "new-root");
            Directory.CreateDirectory(folder);

            int exitCode = await UserCommand.RunAsync(["--create-user", "not-an-email", "--name", "New", "--admin"], folder, new Passwords(NewPassword), new RecordingLoggerFactory());

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        }

        [Theory]
        [InlineData(ResetPassword)]
        [InlineData(SetAdmin)]
        [InlineData(UnsetAdmin)]
        public async Task AnyOtherCommand_OnAFolderThatIsNotThere_FailsAndMakesNone(string command)
        {
            string folder = Path.Combine(_host.TestRoot.Root.Path, "new-root");
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await UserCommand.RunAsync(command.Split(' '), folder, new Passwords(NewPassword), logs);

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Equal($"There is no ByteShelf database in {folder}.", logs.Messages[^1]);
            Assert.False(Directory.Exists(folder));
        }

        [Fact]
        public async Task Run_WithoutARoot_IsAUsageError()
        {
            int exitCode = await UserCommand.RunAsync(["--set-admin", NewEmail], rootPath: null, new Passwords(), NullLoggerFactory.Instance);

            Assert.Equal(RootCommand.Usage, exitCode);
        }

        [Fact]
        public async Task Run_ArgumentsItCannotParse_IsAUsageError()
        {
            int exitCode = await RunAsync(new Passwords(), "--set-admin");

            Assert.Equal(RootCommand.Usage, exitCode);
        }

        [Theory]
        [InlineData("--create-user", NewEmail)]
        [InlineData("--create-user", NewEmail, "--name")]
        [InlineData("--create-user", "--name", "New")]
        [InlineData("--reset-password", NewEmail, "--admin")]
        [InlineData("--set-admin", NewEmail, "--name", "New")]
        [InlineData("--set-admin", NewEmail, "--unset-admin", NewEmail)]
        [InlineData("--set-admin", NewEmail, "extra")]
        [InlineData("--create-user", NewEmail, "--name", "New", "--password", NewPassword)]
        [InlineData("--create-user", NewEmail, "--create-user", "other@byteshelf.test", "--name", "New")]
        [InlineData("--create-user", NewEmail, "--name", "New", "--name", "Again")]
        [InlineData("--create-user", NewEmail, "--name", "New", "--admin", "--admin")]
        public void Parse_AnythingButOneWellFormedCommand_IsNull(params string[] args)
        {
            Assert.Null(UserCommand.Parse(args));
        }

        private async Task CreateOtherAdminAsync()
        {
            HttpResponseMessage created = await _host.AdminClient.PostAsJsonAsync("/api/users", new CreateUserRequest
            {
                Email = OtherAdminEmail,
                DisplayName = "Other admin",
                Password = ServerTestHost.Password,
                Roles = [UserRoles.Admin],
            });
            created.EnsureSuccessStatusCode();
        }

        private async Task<long> LatestChangeAsync()
        {
            using IServiceScope scope = _host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IChangeLogRepository>().GetLatestSequenceAsync();
        }

        private async Task<IReadOnlyList<ChangeLogEntry>> ChangesAfterAsync(long sequence)
        {
            using IServiceScope scope = _host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IChangeLogRepository>().GetAfterAsync(sequence, 100);
        }

        /// <summary>Runs <paramref name="sql"/> on the host's database and answers the first value it returns, if any.</summary>
        private async Task<object?> ExecuteAsync(string sql)
        {
            await using SqliteConnection connection = new SqliteConnection(_host.TestRoot.Root.ConnectionString);
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }

        private Task<int> RunAsync(Passwords passwords, params string[] args)
        {
            return RunAsync(passwords, new RecordingLoggerFactory(), args);
        }

        private Task<int> RunAsync(Passwords passwords, ILoggerFactory logs, params string[] args)
        {
            return UserCommand.RunAsync(args, _host.TestRoot.Root.Path, passwords, logs);
        }

        /// <summary>The passwords typed, in order, and the emails each was asked for.</summary>
        private sealed class Passwords : IPasswordReader
        {
            /// <summary>What a reader says once no password is left, as the real one does at the end of stdin.</summary>
            public const string NoneCame = "No password came on stdin.";

            private readonly Queue<string> _typed;

            public Passwords(params string[] typed)
            {
                _typed = new Queue<string>(typed);
            }

            public List<string> AskedFor { get; } = new List<string>();

            public string ReadNewPassword(string email)
            {
                AskedFor.Add(email);
                return _typed.TryDequeue(out string? password) ? password : throw new ArgumentException(NoneCame);
            }
        }
    }
}
