using ByteShelf.Cli.Commands;
using ByteShelf.Cli.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Cli.Tests
{
    /// <summary>The replication commands, between two in-memory servers: one the CLI runs against as the primary, one as the replica.</summary>
    public sealed class CliReplicationTests
    {
        private readonly InMemoryClientSource _primary = new InMemoryClientSource();
        private readonly InMemoryClientSource _replica = new InMemoryClientSource();

        [Fact]
        public async Task CodeThenFollow_PairsTheServersAndEachSaysSo()
        {
            CliRun code = await CliRun.RunAsync(_primary, ["replication", "code"]);
            Assert.Equal(CliApp.Succeeded, code.ExitCode);

            CliRun followed = await CliRun.RunAsync(_replica, ["replication", "follow", code.Output.Split(Environment.NewLine)[0], "--name", "backup"]);

            Assert.Equal(CliApp.Succeeded, followed.ExitCode);
            Assert.StartsWith($"A replica of {_primary.Client.Address}, paired ", followed.Output, StringComparison.Ordinal);
            Assert.EndsWith("sign in with an account of the primary's from then on.", followed.Output, StringComparison.Ordinal);
            CliRun status = await CliRun.RunAsync(_primary, ["replication"]);
            Assert.Contains("backup  0 changes behind, last seen ", status.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Follow_WithoutAName_IsAUsageError()
        {
            CliRun run = await CliRun.RunAsync(_replica, ["replication", "follow", (await _primary.Client.CreatePairingCodeAsync()).Code]);

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("--name", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Forget_ByName_UnpairsTheReplica()
        {
            await FollowAsync();

            CliRun run = await CliRun.RunAsync(_primary, ["replication", "forget", "backup"]);

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Empty((await _primary.Client.GetReplicationAsync()).Replicas);
        }

        [Fact]
        public async Task Forget_NoSuchReplica_FailsNamingWhereTheyAreListed()
        {
            CliRun run = await CliRun.RunAsync(_primary, ["replication", "forget", "backup"]);

            Assert.Equal(CliApp.Failed, run.ExitCode);
            Assert.Contains("'byteshelf replication' lists them", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Promote_AReplica_TakesWritesAndSaysThePrimaryHeard()
        {
            await FollowAsync();

            CliRun run = await CliRun.RunAsync(_replica, ["replication", "promote"]);

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Contains("its old primary no longer lists it", run.Output, StringComparison.Ordinal);
            await _replica.Client.CreateShelfAsync("own");
        }

        private async Task FollowAsync()
        {
            PairingCodeResponse code = await _primary.Client.CreatePairingCodeAsync();
            await _replica.Client.FollowPrimaryAsync(new PairWithPrimaryRequest { Code = code.Code, Name = "backup" });
        }
    }
}
