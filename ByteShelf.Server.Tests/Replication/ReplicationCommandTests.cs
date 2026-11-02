using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>
    /// The server's replication commands, run against the roots of two servers running in memory, and
    /// checked through their APIs. The commands' calls to a primary reach the primary in memory.
    /// </summary>
    public sealed class ReplicationCommandTests : IAsyncLifetime
    {
        private const string PrimaryUrl = "http://primary.test/";

        private readonly PrimaryHook _hook = new PrimaryHook();
        private readonly List<ServerTestHost> _moreReplicas = new List<ServerTestHost>();
        private ReplicationPair _pair = null!;

        public async Task InitializeAsync()
        {
            _pair = await ReplicationPair.StartAsync(wrapPrimary: primary => new HookHandler(_hook, primary));
            await _pair.CreateShelfAsync("docs");
        }

        public async Task DisposeAsync()
        {
            foreach (ServerTestHost replica in _moreReplicas)
            {
                await replica.DisposeAsync();
            }

            await _pair.DisposeAsync();
        }

        [Fact]
        public async Task ReplicationCode_ThenFollow_PairsTheServersAndTheReplicaCopiesThePrimary()
        {
            RecordingLoggerFactory codeLogs = new RecordingLoggerFactory();
            Assert.Equal(RootCommand.Done, await RunAsync(_pair.Primary, codeLogs, "--replication-code"));
            string code = Assert.Single(codeLogs.Messages).Split(Environment.NewLine)[0];
            RecordingLoggerFactory followLogs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(_pair.Replica, followLogs, "--follow", code, "--name", "backup");

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.Contains("sign in with an account of the primary's", followLogs.Messages[^1], StringComparison.Ordinal);
            Assert.Equal(PrimaryUrl, (await ReplicationPair.GetStatusAsync(_pair.Replica.AdminClient)).Source?.PrimaryUrl);
            Assert.Equal("backup", Assert.Single((await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas).Name);
            await _pair.SyncAsync();
            Assert.Equal(HttpStatusCode.OK, (await (await _pair.SignInOnReplicaAsync()).GetAsync("/api/shelves/docs")).StatusCode);
        }

        [Fact]
        public async Task ReplicationCode_WithoutAppBaseUrl_IsAUsageErrorNamingIt()
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await ReplicationCommand.RunAsync(["--replication-code"], _pair.Primary.TestRoot.Root.Path, appBaseUrl: null, logs);

            Assert.Equal(RootCommand.Usage, exitCode);
            Assert.Contains(logs.Messages, message => message.Contains("APP_BASE_URL", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ReplicationCode_OnAReplica_IsRefused()
        {
            await _pair.PairAsync();
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(_pair.Replica, logs, "--replication-code");

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Equal("This server is a replica and only reads; make the change on its primary.", Assert.Single(logs.Messages));
        }

        [Fact]
        public async Task ReplicationStatus_OnThePrimary_ListsEachReplicaWithHowFarBehindItIs()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();
            await _pair.PutOnPrimaryAsync("docs", "/late.txt", "late");
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(_pair.Primary, logs, "--replication-status");

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.Contains(logs.Messages, message => message.Contains("backup  1 changes behind", StringComparison.Ordinal));
        }

        /// <summary>
        /// The first copy removes whatever the primary does not have, so a shelf made on the running server
        /// while the primary takes the code must refuse the pairing, and the primary drops the replica again.
        /// </summary>
        [Fact]
        public async Task Follow_AShelfMadeWhileThePrimaryTakesTheCode_RefusesThePairing()
        {
            string code = await _pair.CreateCodeAsync();
            _hook.After("/feed/accept", async () => (await _pair.Replica.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "own" })).EnsureSuccessStatusCode());

            HttpResponseMessage response = await _pair.TryPairAsync(code);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Null((await ReplicationPair.GetStatusAsync(_pair.Replica.AdminClient)).Source);
            Assert.Empty((await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas);
        }

        [Fact]
        public async Task Follow_OnAFolderNoServerHasStartedOn_MakesTheRootAndPairsIt()
        {
            string folder = Path.Combine(_pair.Replica.TestRoot.Root.Path, "new-root");
            string code = await CreateCodeByCommandAsync();

            int exitCode = await RunAsync(folder, new RecordingLoggerFactory(), () => _pair.Primary.CreateHandler(), "--follow", code, "--name", "new");

            Assert.Equal(RootCommand.Done, exitCode);
            RootFolder root = RootFolder.OpenExisting(folder);
            Assert.False(File.Exists(root.DatabasePath + "-wal"));
            Assert.True(DatabaseMigrator.IsUpToDate(root.ConnectionString));
            RecordingLoggerFactory statusLogs = new RecordingLoggerFactory();
            Assert.Equal(RootCommand.Done, await RunAsync(folder, statusLogs, () => new Unreachable(), "--replication-status"));
            Assert.StartsWith($"A replica of {PrimaryUrl}", statusLogs.Messages[^1], StringComparison.Ordinal);
            Assert.Equal("new", Assert.Single((await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas).Name);
        }

        [Fact]
        public async Task Follow_OnAFolderNoServerHasStartedOn_WhenThePrimaryRefuses_LeavesNoRoot()
        {
            string folder = Path.Combine(_pair.Replica.TestRoot.Root.Path, "new-root");
            string code = await CreateCodeByCommandAsync();
            Assert.Equal(RootCommand.Done, await RunAsync(_pair.Replica, new RecordingLoggerFactory(), "--follow", code, "--name", "first"));

            int exitCode = await RunAsync(folder, new RecordingLoggerFactory(), () => _pair.Primary.CreateHandler(), "--follow", code, "--name", "new");

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.False(Directory.Exists(folder));
        }

        [Theory]
        [InlineData("--replication-status")]
        [InlineData("--replication-code")]
        [InlineData("--forget", "backup")]
        [InlineData("--promote")]
        public async Task AnyOtherCommand_OnAFolderWithoutADatabase_FailsAndMakesNone(params string[] args)
        {
            string folder = Path.Combine(_pair.Replica.TestRoot.Root.Path, "new-root");
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(folder, logs, () => new Unreachable(), args);

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Equal($"There is no ByteShelf database in {folder}.", logs.Messages[^1]);
            Assert.False(Directory.Exists(folder));
        }

        [Fact]
        public async Task Forget_ByName_UnpairsTheReplica()
        {
            await _pair.PairAsync();
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(_pair.Primary, logs, "--forget", "backup");

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.StartsWith("Removed the replica backup (", Assert.Single(logs.Messages), StringComparison.Ordinal);
            Assert.Empty((await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas);
        }

        [Fact]
        public async Task Forget_ById_WhenTwoShareTheName_UnpairsThatOneOnly()
        {
            await _pair.PairAsync();
            await PairAnotherAsync("backup");
            IReadOnlyList<ReplicaInfo> replicas = (await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas;

            int exitCode = await RunAsync(_pair.Primary, new RecordingLoggerFactory(), "--forget", replicas[0].Id.ToString());

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.Equal(replicas[1].Id, Assert.Single((await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas).Id);
        }

        [Fact]
        public async Task Forget_ANameTwoShare_FailsAskingForTheId()
        {
            await _pair.PairAsync();
            await PairAnotherAsync("backup");
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(_pair.Primary, logs, "--forget", "backup");

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Equal("2 replicas are called 'backup'; name one by its id, which --replication-status lists.", Assert.Single(logs.Messages));
            Assert.Equal(2, (await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas.Count);
        }

        [Fact]
        public async Task Forget_NoSuchReplica_FailsSayingSo()
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(_pair.Primary, logs, "--forget", "backup");

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Equal("No replica is called or has the id 'backup'; --replication-status lists them.", Assert.Single(logs.Messages));
        }

        [Fact]
        public async Task Promote_AReplica_TakesWritesAndThePrimaryNoLongerListsIt()
        {
            await _pair.PairAsync();
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(_pair.Replica, logs, "--promote");

            Assert.Equal(RootCommand.Done, exitCode);
            Assert.Contains("its old primary no longer lists it", logs.Messages[^1], StringComparison.Ordinal);
            Assert.Empty((await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas);
            (await _pair.Replica.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "own" })).EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Promote_NotAReplica_FailsSayingSo()
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            int exitCode = await RunAsync(_pair.Primary, logs, "--promote");

            Assert.Equal(RootCommand.Failed, exitCode);
            Assert.Equal("This server is not a replica.", Assert.Single(logs.Messages));
        }

        /// <summary>
        /// The command runs in a process of its own, so only the database tells the running server's step
        /// that it was promoted between fetching changes and applying them. The primary does not hear of
        /// it here, so the step still fetches the contents and gets as far as applying.
        /// </summary>
        [Fact]
        public async Task Promote_WhileTheRunningReplicaHasChangesInHand_TheyAreNotApplied()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();
            await _pair.PutOnPrimaryAsync("docs", "/late.txt", "late");
            int promoted = -1;
            _hook.After("/feed/changes", async () => promoted = await RunAsync(_pair.Replica.TestRoot.Root.Path, new RecordingLoggerFactory(), () => new Unreachable(), "--promote"));

            await _pair.SyncOnceAsync();

            Assert.Equal(RootCommand.Done, promoted);
            HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.Null((await ReplicationPair.GetStatusAsync(replica)).Source);
            Assert.Equal(HttpStatusCode.NotFound, (await replica.GetAsync("/api/shelves/docs/content?path=/late.txt")).StatusCode);
        }

        /// <summary>The same during a whole copy: nothing of the primary's is taken, and the pages read are dropped.</summary>
        [Fact]
        public async Task Promote_WhileTheRunningReplicaTakesAWholeCopy_NothingIsTaken()
        {
            await _pair.PairAsync();
            int promoted = -1;
            _hook.After("/feed/copy", async () => promoted = await RunAsync(_pair.Replica.TestRoot.Root.Path, new RecordingLoggerFactory(), () => new Unreachable(), "--promote"));

            await _pair.SyncOnceAsync();

            Assert.Equal(RootCommand.Done, promoted);
            Assert.Empty((await _pair.Replica.AdminClient.GetFromJsonAsync<List<ShelfInfo>>("/api/shelves"))!);
            await using SqliteConnection connection = new SqliteConnection(_pair.Replica.TestRoot.Root.ConnectionString);
            await connection.OpenAsync();
            await using SqliteCommand count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM replication_copy";
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }

        [Theory]
        [InlineData("--follow", "code")]
        [InlineData("--follow", "code", "--name")]
        [InlineData("--replication-status", "--name", "backup")]
        [InlineData("--forget")]
        [InlineData("--promote", "--replication-status")]
        [InlineData("--promote", "extra")]
        [InlineData("--follow", "code", "--name", "backup", "--forget", "other")]
        public void Parse_ArgumentsThatAreNotOneWholeCommand_AreRefused(params string[] args)
        {
            Assert.Null(ReplicationCommand.Parse(args));
        }

        /// <summary>A pairing code made on the primary by <c>--replication-code</c>, which names <see cref="PrimaryUrl"/>.</summary>
        private async Task<string> CreateCodeByCommandAsync()
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();
            Assert.Equal(RootCommand.Done, await RunAsync(_pair.Primary, logs, "--replication-code"));
            return Assert.Single(logs.Messages).Split(Environment.NewLine)[0];
        }

        /// <summary>Pairs one more server with the primary, under <paramref name="name"/>.</summary>
        private async Task PairAnotherAsync(string name)
        {
            ServerTestHost replica = await ReplicationPair.StartServerToFollowAsync(_pair.Primary);
            _moreReplicas.Add(replica);
            (await replica.AdminClient.PostAsJsonAsync("/api/replication/pair", new PairWithPrimaryRequest { Code = await _pair.CreateCodeAsync(), Name = name })).EnsureSuccessStatusCode();
        }

        private Task<int> RunAsync(ServerTestHost host, RecordingLoggerFactory logs, params string[] args)
        {
            return RunAsync(host.TestRoot.Root.Path, logs, () => _pair.Primary.CreateHandler(), args);
        }

        /// <param name="primary">The way the command reaches a primary.</param>
        private static Task<int> RunAsync(string rootPath, RecordingLoggerFactory logs, Func<HttpMessageHandler> primary, params string[] args)
        {
            return ReplicationCommand.RunAsync(
                args,
                rootPath,
                PrimaryUrl,
                logs,
                services => services.AddHttpClient(PrimaryClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(primary));
        }

        /// <summary>What runs once, the next time the replica's request to the primary ends in a path, once its answer has arrived.</summary>
        private sealed class PrimaryHook
        {
            private string? _path;
            private Func<Task>? _action;

            public void After(string path, Func<Task> action)
            {
                _path = path;
                _action = action;
            }

            public Func<Task>? Take(string requestPath)
            {
                if (_path == null || !requestPath.EndsWith(_path, StringComparison.Ordinal))
                {
                    return null;
                }

                Func<Task>? action = _action;
                _path = null;
                _action = null;
                return action;
            }
        }

        private sealed class HookHandler : DelegatingHandler
        {
            private readonly PrimaryHook _hook;

            public HookHandler(PrimaryHook hook, HttpMessageHandler inner)
                : base(inner)
            {
                _hook = hook;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                if (_hook.Take(request.RequestUri!.AbsolutePath) is Func<Task> after)
                {
                    await after();
                }

                return response;
            }
        }

        /// <summary>A primary that cannot be reached.</summary>
        private sealed class Unreachable : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new HttpRequestException("Unreachable in this test.");
            }
        }
    }
}
