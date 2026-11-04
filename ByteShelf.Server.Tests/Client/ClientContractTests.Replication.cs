using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>Replication's admin surface, the same on both clients: codes, following, forgetting and promoting.</summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task GetReplicationAsync_AServerOnItsOwn_HasNoReplicasAndFollowsNone()
        {
            ReplicationStatus status = await Client.GetReplicationAsync();

            Assert.Empty(status.Replicas);
            Assert.Null(status.Source);
        }

        [Fact]
        public async Task FollowPrimaryAsync_ACodeFromThePrimary_ListsTheReplicaThereAndFollowsIt()
        {
            IByteShelfClient follower = await CreateServerToFollowAsync();

            ReplicationStatus followed = await follower.FollowPrimaryAsync(new PairWithPrimaryRequest { Code = (await Client.CreatePairingCodeAsync()).Code, Name = "backup" });

            Assert.Equal(PrimaryUrl, followed.Source?.PrimaryUrl);
            Assert.Equal(PrimaryUrl, (await follower.GetReplicationAsync()).Source?.PrimaryUrl);
            Assert.Equal("backup", Assert.Single((await Client.GetReplicationAsync()).Replicas).Name);
        }

        [Fact]
        public async Task FollowPrimaryAsync_Followed_RefusesWrites()
        {
            IByteShelfClient follower = await FollowAsync();

            Assert.Equal(ErrorCodes.ReadOnlyReplica, (await RefusalOf(follower.CreateShelfAsync("own"))).Code);
            Assert.Equal(ErrorCodes.ReadOnlyReplica, (await RefusalOf(follower.CreatePairingCodeAsync())).Code);
        }

        [Fact]
        public async Task FollowPrimaryAsync_ACodeUsedOnce_IsRefusedTheSecondTime()
        {
            string code = (await Client.CreatePairingCodeAsync()).Code;
            await (await CreateServerToFollowAsync()).FollowPrimaryAsync(new PairWithPrimaryRequest { Code = code, Name = "first" });

            ByteShelfException refusal = await RefusalOf((await CreateServerToFollowAsync()).FollowPrimaryAsync(new PairWithPrimaryRequest { Code = code, Name = "second" }));

            Assert.Equal(ErrorCodes.Conflict, refusal.Code);
            Assert.Equal("first", Assert.Single((await Client.GetReplicationAsync()).Replicas).Name);
        }

        [Fact]
        public async Task FollowPrimaryAsync_ItsOwnCode_IsRefused()
        {
            ByteShelfException refusal = await RefusalOf(Client.FollowPrimaryAsync(new PairWithPrimaryRequest { Code = (await Client.CreatePairingCodeAsync()).Code, Name = "self" }));

            Assert.Equal(ErrorCodes.Conflict, refusal.Code);
            Assert.Null((await Client.GetReplicationAsync()).Source);
        }

        [Fact]
        public async Task FollowPrimaryAsync_AServerWithShelves_IsRefusedAndTheCodeStillWorks()
        {
            IByteShelfClient follower = await CreateServerToFollowAsync();
            await follower.CreateShelfAsync("own");
            string code = (await Client.CreatePairingCodeAsync()).Code;

            ByteShelfException refusal = await RefusalOf(follower.FollowPrimaryAsync(new PairWithPrimaryRequest { Code = code, Name = "backup" }));

            Assert.Equal(ErrorCodes.Conflict, refusal.Code);
            await (await CreateServerToFollowAsync()).FollowPrimaryAsync(new PairWithPrimaryRequest { Code = code, Name = "backup" });
        }

        [Fact]
        public async Task FollowPrimaryAsync_NotACode_IsInvalid()
        {
            ByteShelfException refusal = await RefusalOf((await CreateServerToFollowAsync()).FollowPrimaryAsync(new PairWithPrimaryRequest { Code = "not-a-code", Name = "backup" }));

            Assert.Equal(ErrorCodes.InvalidRequest, refusal.Code);
        }

        [Fact]
        public async Task PromoteAsync_AReplica_TakesWritesAndThePrimaryNoLongerListsIt()
        {
            IByteShelfClient follower = await FollowAsync();

            PromoteResponse promoted = await follower.PromoteAsync();

            Assert.True(promoted.PrimaryTold);
            Assert.Null((await follower.GetReplicationAsync()).Source);
            Assert.Empty((await Client.GetReplicationAsync()).Replicas);
            await follower.CreateShelfAsync("own");
        }

        [Fact]
        public async Task PromoteAsync_NotAReplica_IsNotFound()
        {
            Assert.Equal(ErrorCodes.NotFound, (await RefusalOf(Client.PromoteAsync())).Code);
        }

        [Fact]
        public async Task RemoveReplicaAsync_AReplica_IsNoLongerListed()
        {
            await FollowAsync();
            ReplicaInfo replica = Assert.Single((await Client.GetReplicationAsync()).Replicas);

            await Client.RemoveReplicaAsync(replica.Id);

            Assert.Empty((await Client.GetReplicationAsync()).Replicas);
            Assert.Equal(ErrorCodes.NotFound, (await RefusalOf(Client.RemoveReplicaAsync(replica.Id))).Code);
        }

        /// <summary>The address <see cref="Client"/>'s server puts in its pairing codes, which its followers name it by.</summary>
        protected abstract string PrimaryUrl { get; }

        /// <summary>A client, as its admin, of a new server with no shelves, whose calls to a primary reach <see cref="Client"/>'s.</summary>
        protected abstract Task<IByteShelfClient> CreateServerToFollowAsync();

        /// <summary>A new server following <see cref="Client"/>'s, as <c>backup</c>.</summary>
        private async Task<IByteShelfClient> FollowAsync()
        {
            IByteShelfClient follower = await CreateServerToFollowAsync();
            await follower.FollowPrimaryAsync(new PairWithPrimaryRequest { Code = (await Client.CreatePairingCodeAsync()).Code, Name = "backup" });
            return follower;
        }
    }
}
