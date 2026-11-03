using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>A replica paired with a primary, following it, refusing writes, and promoted.</summary>
    public sealed class ReplicationTests : IAsyncLifetime
    {
        private ReplicationPair _pair = null!;

        public async Task InitializeAsync()
        {
            _pair = await ReplicationPair.StartAsync();
            await _pair.CreateShelfAsync("docs");
        }

        public async Task DisposeAsync()
        {
            await _pair.DisposeAsync();
        }

        [Fact]
        public async Task Pair_TakesTheWholePrimary_ItsUsersInsteadOfItsOwn()
        {
            await _pair.PutOnPrimaryAsync("docs", "/notes/a.txt", "from the primary");

            await _pair.PairAsync();
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.Equal("from the primary", await replica.GetStringAsync("/api/shelves/docs/content?path=/notes/a.txt"));
            Assert.Equal(await UserIdsAsync(_pair.Primary.AdminClient), await UserIdsAsync(replica));
        }

        [Fact]
        public async Task Pair_TheReplicasOwnUsers_CanNoLongerRenewTheirSessions()
        {
            HttpResponseMessage login = await _pair.Replica.AnonymousClient.PostAsJsonAsync("/api/auth/login", new { username = ServerTestHost.AdminEmail, password = ServerTestHost.Password });
            string refreshToken = (await login.Content.ReadFromJsonAsync<SessionTokens>())!.RefreshToken;
            Guid ownAdmin = (await _pair.Replica.AdminClient.GetFromJsonAsync<UserInfo>("/api/users/me"))!.Id;

            await _pair.PairAsync();
            await _pair.SyncAsync();

            HttpResponseMessage renewal = await _pair.Replica.AnonymousClient.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest { RefreshToken = refreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, renewal.StatusCode);

            // Refused already because the user is gone; their stored tokens are dropped too.
            Assert.Equal(0, await QueryOnAsync<long>(_pair.Replica, $"SELECT COUNT(*) FROM refresh_token WHERE subject = '{ownAdmin:D}' AND is_invalidated = 0"));
        }

        [Fact]
        public async Task Sync_FollowsWhatChangesOnThePrimaryAfterPairing()
        {
            await _pair.PutOnPrimaryAsync("docs", "/a.txt", "first");
            await _pair.PairAsync();
            await _pair.SyncAsync();

            await _pair.PutOnPrimaryAsync("docs", "/b.txt", "second");
            (await _pair.Primary.AdminClient.PostAsJsonAsync("/api/shelves/docs/files/move?path=/a.txt", new MoveRequest { ToPath = "/moved/a.txt" })).EnsureSuccessStatusCode();
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.Equal("second", await replica.GetStringAsync("/api/shelves/docs/content?path=/b.txt"));
            Assert.Equal("first", await replica.GetStringAsync("/api/shelves/docs/content?path=/moved/a.txt"));
            Assert.Equal(HttpStatusCode.NotFound, (await replica.GetAsync("/api/shelves/docs/content?path=/a.txt")).StatusCode);
        }

        [Fact]
        public async Task Sync_DescriptionSetOnThePrimary_ReachesTheReplicaAndItsSearch()
        {
            await _pair.PutOnPrimaryAsync("docs", "/a.txt", "first");
            await _pair.PairAsync();
            await _pair.SyncAsync();

            (await _pair.Primary.AdminClient.PutAsJsonAsync("/api/shelves/docs/files/description?path=/a.txt", new SetDescriptionRequest { Description = "Signed copy" })).EnsureSuccessStatusCode();
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.Equal("Signed copy", (await replica.GetFromJsonAsync<ShelfFile>("/api/shelves/docs/files?path=/a.txt"))!.Description);
            FileSearchResults found = (await replica.GetFromJsonAsync<FileSearchResults>("/api/shelves/docs/search?text=signed"))!;
            Assert.Equal("/a.txt", Assert.Single(found.Files).Path);
        }

        [Fact]
        public async Task Sync_EncryptedDirectoryAndFile_ReachTheReplicaMarkedSo()
        {
            await _pair.PairAsync();
            (await _pair.Primary.AdminClient.PostAsJsonAsync("/api/shelves/docs/directories", new CreateDirectoryRequest { Path = "/secret" })).EnsureSuccessStatusCode();
            // The server checks only that a check or an encrypted file starts as an age file.
            string check = Convert.ToBase64String(Encoding.ASCII.GetBytes("age-encryption.org/v1\nthe check"));
            (await _pair.Primary.AdminClient.PutAsJsonAsync("/api/shelves/docs/directories/encryption?path=/secret", new SetFolderEncryptionRequest { Check = check })).EnsureSuccessStatusCode();
            (await _pair.Primary.AdminClient.PutAsync("/api/shelves/docs/content?path=/secret/a.txt&encrypted=true", new StringContent("age-encryption.org/v1\nthe file"))).EnsureSuccessStatusCode();

            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            DirectoryListing listing = (await replica.GetFromJsonAsync<DirectoryListing>("/api/shelves/docs/entries?path=/secret"))!;
            Assert.Equal(("/secret", check, true), (listing.EncryptedBy?.Directory.Path, listing.EncryptedBy?.Check, Assert.Single(listing.Files).Encrypted));
        }

        [Fact]
        public async Task Sync_ContentsSwappedOnThePrimary_RemovesTheOldOnesOnTheReplicaAtOnce()
        {
            await _pair.PairAsync();
            ShelfFile plain = await _pair.PutOnPrimaryAsync("docs", "/a.txt", "plain contents");
            await _pair.SyncAsync();
            ObjectStore replicaObjects = _pair.Replica.Services.GetRequiredService<ObjectStore>();
            Assert.True(replicaObjects.Exists(plain.Sha256));

            await _pair.SwapOnPrimaryAsync("docs", plain, "the file");
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            ShelfFile there = (await replica.GetFromJsonAsync<ShelfFile>("/api/shelves/docs/files?path=/a.txt"))!;
            Assert.Equal((plain.Id, true), (there.Id, there.Encrypted));
            Assert.False(replicaObjects.Exists(plain.Sha256));
            Assert.True(replicaObjects.Exists(there.Sha256));
            Assert.Empty((await replica.GetFromJsonAsync<List<TrashEntry>>("/api/shelves/docs/trash"))!);
        }

        [Fact]
        public async Task Sync_FileStoredThenSwappedBeforeTheReplicaSawIt_TakesItsNewContents()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();

            // The entry that stored it names contents the primary no longer holds for it.
            ShelfFile plain = await _pair.PutOnPrimaryAsync("docs", "/a.txt", "plain contents");
            ShelfFile swapped = await _pair.SwapOnPrimaryAsync("docs", plain, "the file");
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            ShelfFile there = (await replica.GetFromJsonAsync<ShelfFile>("/api/shelves/docs/files?path=/a.txt"))!;
            Assert.Equal((plain.Id, swapped.Sha256, true), (there.Id, there.Sha256, there.Encrypted));
        }

        [Fact]
        public async Task Sync_SwappedOutContentsAnotherFileHolds_KeepsThemOnTheReplica()
        {
            await _pair.PairAsync();
            ShelfFile plain = await _pair.PutOnPrimaryAsync("docs", "/one.txt", "same");
            await _pair.PutOnPrimaryAsync("docs", "/two.txt", "same");
            await _pair.SyncAsync();

            await _pair.SwapOnPrimaryAsync("docs", plain, "the file");
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.True(_pair.Replica.Services.GetRequiredService<ObjectStore>().Exists(plain.Sha256));
            Assert.Equal("same", await replica.GetStringAsync("/api/shelves/docs/content?path=/two.txt"));
        }

        [Fact]
        public async Task Sync_ContentsSwappedThenAWholeCopy_RemovesTheOldOnesOnTheReplicaAtOnce()
        {
            await _pair.PairAsync();
            ShelfFile plain = await _pair.PutOnPrimaryAsync("docs", "/a.txt", "plain contents");
            await _pair.SyncAsync();
            ObjectStore replicaObjects = _pair.Replica.Services.GetRequiredService<ObjectStore>();

            await _pair.SwapOnPrimaryAsync("docs", plain, "the file");
            // A move supersedes the swap's entry before the replica saw it, so compacting drops that and
            // the replica, behind what was dropped, takes a whole copy.
            (await _pair.Primary.AdminClient.PostAsJsonAsync("/api/shelves/docs/files/move?path=/a.txt", new MoveRequest { ToPath = "/b.txt" })).EnsureSuccessStatusCode();
            await CompactPrimaryAsync();
            await _pair.SyncOnceAsync();
            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.True((await ReplicationPair.GetStatusAsync(replica)).Source!.Copying);
            await _pair.SyncAsync();

            Assert.True((await replica.GetFromJsonAsync<ShelfFile>("/api/shelves/docs/files?path=/b.txt"))!.Encrypted);
            Assert.False(replicaObjects.Exists(plain.Sha256));
        }

        [Fact]
        public async Task Sync_FilePurgedOnThePrimary_StaysInTheReplicasTrash()
        {
            await _pair.PairAsync();
            await _pair.PutOnPrimaryAsync("docs", "/a.txt", "kept for the window");
            await _pair.SyncAsync();

            await _pair.PurgeOnPrimaryAsync("docs", "/a.txt");
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            List<TrashEntry> trash = (await replica.GetFromJsonAsync<List<TrashEntry>>("/api/shelves/docs/trash"))!;
            Assert.Equal("/a.txt", Assert.Single(trash).Path);
        }

        /// <summary>How a replica gives back a file purged on its primary: a read, so the read-only replica answers it.</summary>
        [Fact]
        public async Task FilePurgedOnThePrimary_IsReadFromTheReplicasTrash()
        {
            await _pair.PairAsync();
            await _pair.PutOnPrimaryAsync("docs", "/a.txt", "kept for the window");
            await _pair.SyncAsync();
            await _pair.PurgeOnPrimaryAsync("docs", "/a.txt");
            await _pair.SyncAsync();
            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            TrashEntry deleted = Assert.Single((await replica.GetFromJsonAsync<List<TrashEntry>>("/api/shelves/docs/trash"))!);

            DeletionTree listed = (await replica.GetFromJsonAsync<DeletionTree>($"/api/shelves/docs/trash/{deleted.DeletionId}/tree"))!;
            string contents = await replica.GetStringAsync($"/api/shelves/docs/trash/{deleted.DeletionId}/content?id={Assert.Single(listed.Tree.Files).Id}");

            Assert.Equal("kept for the window", contents);
        }

        [Fact]
        public async Task Sync_ShelfDeletedOnThePrimary_GoesWithEverythingItHeld()
        {
            await _pair.CreateShelfAsync("gone");
            await _pair.PutOnPrimaryAsync("gone", "/a.txt", "a");
            await _pair.PairAsync();
            await _pair.SyncAsync();

            await _pair.PurgeOnPrimaryAsync("gone", "/a.txt");
            (await _pair.Primary.AdminClient.DeleteAsync("/api/shelves/gone")).EnsureSuccessStatusCode();
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            List<ShelfInfo> shelves = (await replica.GetFromJsonAsync<List<ShelfInfo>>("/api/shelves"))!;
            Assert.Equal(new[] { "docs" }, shelves.Select(shelf => shelf.Name));
        }

        [Fact]
        public async Task Sync_BehindARemovalTheLogNoLongerHolds_TakesAWholeCopyWithoutIt()
        {
            await _pair.PairAsync();
            HttpResponseMessage created = await _pair.Primary.AdminClient.PostAsJsonAsync("/api/shelves/docs/keys", new CreateShelfKeyRequest { Name = "backup", Permission = ShelfKeyPermission.Read });
            Guid keyId = (await created.Content.ReadFromJsonAsync<CreatedShelfKey>())!.Key.Id;
            await _pair.SyncAsync();

            (await _pair.Primary.AdminClient.DeleteAsync($"/api/shelves/docs/keys/{keyId}")).EnsureSuccessStatusCode();
            await CompactPrimaryAsync();
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.Empty((await replica.GetFromJsonAsync<List<ShelfKeyInfo>>("/api/shelves/docs/keys"))!);
            Assert.Equal(await LatestSequenceAsync(_pair.Primary), (await ReplicationPair.GetStatusAsync(replica)).Source!.AppliedSequence);
        }

        [Fact]
        public async Task Sync_BehindAnEntryCompactionDropped_TakesAWholeCopy()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();

            // The file's first entry goes: a newer one supersedes it. Replaying what is left could meet a
            // row before its parent's only entry, so the replica copies instead.
            await _pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            (await _pair.Primary.AdminClient.PostAsJsonAsync("/api/shelves/docs/files/move?path=/a.txt", new MoveRequest { ToPath = "/b.txt" })).EnsureSuccessStatusCode();
            await CompactPrimaryAsync();
            await _pair.SyncOnceAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.True((await ReplicationPair.GetStatusAsync(replica)).Source!.Copying);
        }

        [Fact]
        public async Task Sync_PrimaryRestoredFromABackup_IsNoticedOnceItReusesTheNumbers()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();
            long before = await LatestSequenceAsync(_pair.Primary);
            await _pair.PutOnPrimaryAsync("docs", "/lost.txt", "only in the log the backup did not have");
            await _pair.SyncAsync();

            // As if the primary went back to a backup taken before that, then went on: the next entry gets
            // the number the replica already applied, for another change.
            await RewindPrimaryLogAsync(before);
            await _pair.PutOnPrimaryAsync("docs", "/after.txt", "after the restore");
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.Equal("after the restore", await replica.GetStringAsync("/api/shelves/docs/content?path=/after.txt"));
        }

        [Fact]
        public async Task Sync_PrimaryGivingSmallerPagesThanAsked_StillTakesEverything()
        {
            await using ReplicationPair small = await ReplicationPair.StartAsync(primary => new SmallPagesHandler(primary));
            await small.CreateShelfAsync("docs");
            for (int index = 0; index < 3; index++)
            {
                await small.PutOnPrimaryAsync("docs", $"/{index}.txt", $"file {index}");
            }

            await small.PairAsync();
            await small.SyncAsync();

            using HttpClient replica = await small.SignInOnReplicaAsync();
            for (int index = 0; index < 3; index++)
            {
                Assert.Equal($"file {index}", await replica.GetStringAsync($"/api/shelves/docs/content?path=/{index}.txt"));
            }
        }

        [Fact]
        public async Task Sync_ACopyAgain_RemovesWhatThePrimaryNoLongerHas()
        {
            await _pair.PutOnPrimaryAsync("docs", "/d/a.txt", "a");
            (await _pair.Primary.AdminClient.PutAsJsonAsync($"/api/shelves/docs/access/{_pair.Primary.MemberId}", new SetShelfAccessRequest { Permission = ShelfAccessPermission.Read })).EnsureSuccessStatusCode();
            await _pair.PairAsync();
            await _pair.SyncAsync();

            (await _pair.Primary.AdminClient.DeleteAsync("/api/shelves/docs/directories?path=/d")).EnsureSuccessStatusCode();
            (await _pair.Primary.AdminClient.DeleteAsync("/api/shelves/docs/trash")).EnsureSuccessStatusCode();
            (await _pair.Primary.AdminClient.DeleteAsync($"/api/users/{_pair.Primary.MemberId}")).EnsureSuccessStatusCode();
            await CompactPrimaryAsync();
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            List<TrashEntry> trash = (await replica.GetFromJsonAsync<List<TrashEntry>>("/api/shelves/docs/trash"))!;
            Assert.Equal(new[] { "/d", "/d/a.txt" }, trash.Select(entry => entry.Path).Order());
            Assert.DoesNotContain(_pair.Primary.MemberId, await UserIdsAsync(replica));
            Assert.Empty((await replica.GetFromJsonAsync<List<ShelfAccessGrant>>("/api/shelves/docs/access"))!);
        }

        [Fact]
        public async Task Sync_FilePurgedBeforeTheReplicaFetchedIt_IsPassedOver()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();

            await _pair.PutOnPrimaryAsync("docs", "/a.txt", "gone before it was fetched");
            await _pair.PurgeOnPrimaryAsync("docs", "/a.txt");
            await _pair.SyncAsync();

            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            Assert.Empty((await replica.GetFromJsonAsync<List<TrashEntry>>("/api/shelves/docs/trash"))!);
        }

        [Fact]
        public async Task Sync_ACopyMissingOneFilesContents_StillTakesALaterFileWithTheSameContents()
        {
            Guid missing = Guid.Empty;
            await using ReplicationPair pair = await ReplicationPair.StartAsync(primary => new MissingContentsHandler(primary, () => missing), pageSize: 1);
            await pair.CreateShelfAsync("docs");
            missing = (await pair.PutOnPrimaryAsync("docs", "/a.txt", "the same bytes")).Id;
            await pair.PutOnPrimaryAsync("docs", "/b.txt", "the same bytes");
            await pair.PairAsync();

            await pair.SyncAsync();

            using HttpClient replica = await pair.SignInOnReplicaAsync();
            Assert.Equal("the same bytes", await replica.GetStringAsync("/api/shelves/docs/content?path=/b.txt"));
        }

        [Fact]
        public async Task Sync_BytesThatDoNotMatchTheirHash_AreNotKept()
        {
            await using ReplicationPair tampered = await ReplicationPair.StartAsync(primary => new TamperingHandler(primary));
            await tampered.CreateShelfAsync("docs");
            ShelfFile file = await tampered.PutOnPrimaryAsync("docs", "/a.txt", "the real bytes");
            await tampered.PairAsync();

            await Assert.ThrowsAsync<PrimaryFailedException>(tampered.SyncAsync);

            using HttpClient replica = await tampered.SignInOnReplicaAsync();
            Assert.False(tampered.Replica.Services.GetRequiredService<ObjectStore>().Exists(file.Sha256));
            Assert.Empty(tampered.Replica.Services.GetRequiredService<StagingArea>().List());
            Assert.True((await ReplicationPair.GetStatusAsync(replica)).Source!.Copying);
        }

        [Fact]
        public async Task Sync_PrimaryThatGoesQuietPartWay_IsGivenUpAfterTheIdleTimeout()
        {
            StallingHandler? stalling = null;
            await using ReplicationPair quiet = await ReplicationPair.StartAsync(
                primary => stalling = new StallingHandler(primary),
                idleTimeout: TimeSpan.FromMilliseconds(200));
            await quiet.CreateShelfAsync("docs");
            await quiet.PutOnPrimaryAsync("docs", "/a.txt", "never arrives");
            await quiet.PairAsync();

            PrimaryFailedException failed = await Assert.ThrowsAsync<PrimaryFailedException>(() => quiet.SyncAsync().WaitAsync(TimeSpan.FromSeconds(30)));

            // Given up on the file's bytes that stalled, not on anything before them.
            Assert.True(stalling!.Stalled.IsCompleted);
            Assert.Contains("sent nothing for", failed.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Sync_FileThatArrivesSlowlyButSteadily_IsNotGivenUp()
        {
            // Each byte comes 100 ms after the last on the replica's clock: 1.8 seconds in all, never 500 ms apart.
            FakeTimeProvider clock = new FakeTimeProvider();
            await using ReplicationPair slow = await ReplicationPair.StartAsync(
                primary => new TricklingHandler(primary, clock),
                idleTimeout: TimeSpan.FromMilliseconds(500),
                primaryClock: clock);
            await slow.CreateShelfAsync("docs");
            await slow.PutOnPrimaryAsync("docs", "/a.txt", "one byte at a time");
            await slow.PairAsync();

            await slow.SyncAsync();

            using HttpClient replica = await slow.SignInOnReplicaAsync();
            Assert.Equal("one byte at a time", await replica.GetStringAsync("/api/shelves/docs/content?path=/a.txt"));
        }

        [Fact]
        public async Task Sync_PrimaryChangedBetweenCopyPages_CopiesWhatItHoldsAtTheEnd()
        {
            ChangeBetweenCopyPages between = new ChangeBetweenCopyPages();
            await using ReplicationPair pair = await ReplicationPair.StartAsync(primary => new BetweenCopyPagesHandler(primary, between), pageSize: 1);
            await pair.CreateShelfAsync("docs");
            await pair.PutOnPrimaryAsync("docs", "/d/a.txt", "a");
            between.CopiedThrough = await LatestSequenceAsync(pair.Primary);

            // The directory's newest entry now comes after the file's, so the copy takes the file a page
            // before the directory. Both are gone before the replica asks for the directory's page.
            (await pair.Primary.AdminClient.PostAsJsonAsync("/api/shelves/docs/directories/move?path=/d", new MoveRequest { ToPath = "/e" })).EnsureSuccessStatusCode();
            between.Change = async () =>
            {
                (await pair.Primary.AdminClient.DeleteAsync("/api/shelves/docs/directories?path=/e")).EnsureSuccessStatusCode();
                (await pair.Primary.AdminClient.DeleteAsync("/api/shelves/docs/trash")).EnsureSuccessStatusCode();
            };
            await pair.PairAsync();

            await pair.SyncAsync();

            // The copy left out what the primary removed meanwhile; the log after it brought both back as
            // they stood when removed, in the trash, where a replica keeps what the primary purges.
            Assert.True(between.Made);
            using HttpClient replica = await pair.SignInOnReplicaAsync();
            Assert.Empty((await replica.GetFromJsonAsync<DirectoryListing>("/api/shelves/docs/entries"))!.Directories);
            TrashEntry trashed = Assert.Single((await replica.GetFromJsonAsync<List<TrashEntry>>("/api/shelves/docs/trash"))!);
            Assert.Equal(("/e", 1), (trashed.Path, trashed.FileCount));
            Assert.Equal(await LatestSequenceAsync(pair.Primary), (await ReplicationPair.GetStatusAsync(replica)).Source!.AppliedSequence);
        }

        [Fact]
        public async Task Promote_DuringACopy_StopsItRatherThanWaiting()
        {
            StallingHandler? stalling = null;
            await using ReplicationPair copying = await ReplicationPair.StartAsync(primary => stalling = new StallingHandler(primary));
            await copying.CreateShelfAsync("docs");
            await copying.PutOnPrimaryAsync("docs", "/a.txt", "never arrives");
            await copying.PairAsync();
            Task<bool> step = copying.SyncOnceAsync();
            await stalling!.Stalled.WaitAsync(TimeSpan.FromSeconds(30));

            using HttpClient replica = await copying.SignInOnReplicaAsync();
            HttpResponseMessage promoted = await replica.PostAsync("/api/replication/promote", null).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
            Assert.True(await step.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Null((await ReplicationPair.GetStatusAsync(replica)).Source);
        }

        [Fact]
        public async Task Replica_RefusesWritesButNotDownloadLinks()
        {
            await _pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            await _pair.PairAsync();
            await _pair.SyncAsync();
            using HttpClient replica = await _pair.SignInOnReplicaAsync();

            using StringContent content = new StringContent("b", Encoding.UTF8, "text/plain");
            HttpResponseMessage write = await replica.PutAsync("/api/shelves/docs/content?path=/b.txt", content);
            HttpResponseMessage link = await replica.PostAsync("/api/shelves/docs/download-link?path=/a.txt", null);

            Assert.Equal(HttpStatusCode.Conflict, write.StatusCode);
            Assert.Equal(ErrorCodes.ReadOnlyReplica, (await write.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        }

        [Fact]
        public async Task Primary_ListsTheReplicaWithHowFarBehindItIs()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();
            long caughtUpTo = await LatestSequenceAsync(_pair.Primary);
            await _pair.PutOnPrimaryAsync("docs", "/a.txt", "a");
            long latest = await LatestSequenceAsync(_pair.Primary);

            ReplicationStatus before = await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient);

            // Twice: the primary learns how far the replica got only from its next request.
            await _pair.SyncAsync();
            await _pair.SyncAsync();
            ReplicationStatus after = await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient);

            Assert.Equal("backup", Assert.Single(before.Replicas).Name);
            Assert.Equal(latest - caughtUpTo, before.Replicas[0].ChangesBehind);
            Assert.Equal(0, Assert.Single(after.Replicas).ChangesBehind);
        }

        [Fact]
        public async Task Promote_StopsFollowingTellsThePrimaryAndTakesWrites()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();
            using HttpClient replica = await _pair.SignInOnReplicaAsync();

            HttpResponseMessage promoted = await replica.PostAsync("/api/replication/promote", null);
            using StringContent content = new StringContent("b", Encoding.UTF8, "text/plain");
            HttpResponseMessage write = await replica.PutAsync("/api/shelves/docs/content?path=/b.txt", content);

            Assert.True((await promoted.Content.ReadFromJsonAsync<PromoteResponse>())!.PrimaryTold);
            Assert.Equal(HttpStatusCode.OK, write.StatusCode);
            Assert.Empty((await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas);
        }

        [Fact]
        public async Task Promote_WithThePrimaryDown_PromotesAndSaysThePrimaryWasNotTold()
        {
            UnreachableHandler? link = null;
            await using ReplicationPair down = await ReplicationPair.StartAsync(primary => link = new UnreachableHandler(primary));
            await down.PairAsync();
            await down.SyncAsync();
            using HttpClient replica = await down.SignInOnReplicaAsync();

            link!.Down = true;
            HttpResponseMessage promoted = await replica.PostAsync("/api/replication/promote", null);

            Assert.False((await promoted.Content.ReadFromJsonAsync<PromoteResponse>())!.PrimaryTold);
            Assert.Null((await ReplicationPair.GetStatusAsync(replica)).Source);
        }

        [Fact]
        public async Task Unpair_OnThePrimary_ShutsTheReplicaOut()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();
            Guid replicaId = Assert.Single((await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas).Id;

            (await _pair.Primary.AdminClient.DeleteAsync($"/api/replication/replicas/{replicaId}")).EnsureSuccessStatusCode();

            PrimaryFailedException refused = await Assert.ThrowsAsync<PrimaryFailedException>(() => _pair.SyncOnceAsync());
            Assert.Contains("no longer recognises this replica", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Sync_PageFromAnotherInstance_IsRefused()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();
            await ExecuteOnAsync(_pair.Replica, "UPDATE replication_source SET primary_instance_id = 'another'");

            PrimaryFailedException refused = await Assert.ThrowsAsync<PrimaryFailedException>(() => _pair.SyncOnceAsync());
            Assert.Contains("no longer the primary", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Sync_AChangeOfAnUnknownType_StopsReplicationWithoutSkippingIt()
        {
            await _pair.PairAsync();
            await _pair.SyncAsync();
            using HttpClient replica = await _pair.SignInOnReplicaAsync();
            long applied = (await ReplicationPair.GetStatusAsync(replica)).Source!.AppliedSequence;
            await ExecuteOnAsync(
                _pair.Primary,
                $"INSERT INTO change_log (entity_type, entity_id, action, payload, created_at) VALUES ('gadget', '{Guid.NewGuid():D}', 'added', '{{}}', '2026-01-01T00:00:00.0000000Z')");

            await Assert.ThrowsAsync<ReplicaOutdatedException>(() => _pair.SyncOnceAsync());

            Assert.Equal(applied, (await ReplicationPair.GetStatusAsync(replica)).Source!.AppliedSequence);
        }

        [Fact]
        public async Task Pair_CodeUsedOnce_IsRefusedTheSecondTime()
        {
            string code = await _pair.CreateCodeAsync();
            AcceptPairingRequest accept = new AcceptPairingRequest { Secret = PairingCode.TryParse(code)!.Secret, Name = "one" };

            HttpResponseMessage first = await _pair.Primary.AnonymousClient.PostAsJsonAsync("/api/replication/feed/accept", accept);
            HttpResponseMessage second = await _pair.Primary.AnonymousClient.PostAsJsonAsync("/api/replication/feed/accept", accept);

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (first.StatusCode, second.StatusCode));
        }

        [Fact]
        public async Task Pair_ServerWithShelves_IsRefused()
        {
            (await _pair.Replica.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "mine" })).EnsureSuccessStatusCode();

            HttpResponseMessage pairing = await _pair.TryPairAsync(await _pair.CreateCodeAsync());

            await AssertRefusedAsync(pairing, "Only a server without shelves can become a replica");
            Assert.Null((await ReplicationPair.GetStatusAsync(_pair.Replica.AdminClient)).Source);
        }

        [Fact]
        public async Task Pair_ACodeMadeOnTheSameServer_IsRefused()
        {
            HttpResponseMessage made = await _pair.Replica.AdminClient.PostAsync("/api/replication/codes", null);
            string ownCode = (await made.Content.ReadFromJsonAsync<PairingCodeResponse>())!.Code;

            HttpResponseMessage pairing = await _pair.TryPairAsync(ownCode);

            await AssertRefusedAsync(pairing, "That code was made on this server");
            Assert.Null((await ReplicationPair.GetStatusAsync(_pair.Replica.AdminClient)).Source);
        }

        [Fact]
        public async Task Pair_AServerThatIsAlreadyAReplica_IsRefused()
        {
            await _pair.PairAsync();

            HttpResponseMessage again = await _pair.TryPairAsync(await _pair.CreateCodeAsync());

            // Refused as a write on a replica, before the second code was presented: the primary paired
            // only the first.
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal(ErrorCodes.ReadOnlyReplica, (await again.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
            Assert.Single((await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient)).Replicas);
        }

        [Fact]
        public async Task Pair_AServerWithReplicasOfItsOwn_IsRefused()
        {
            HttpResponseMessage made = await _pair.Replica.AdminClient.PostAsync("/api/replication/codes", null);
            string secret = PairingCode.TryParse((await made.Content.ReadFromJsonAsync<PairingCodeResponse>())!.Code)!.Secret;
            (await _pair.Replica.AnonymousClient.PostAsJsonAsync("/api/replication/feed/accept", new AcceptPairingRequest { Secret = secret, Name = "its own" })).EnsureSuccessStatusCode();

            HttpResponseMessage pairing = await _pair.TryPairAsync(await _pair.CreateCodeAsync());

            await AssertRefusedAsync(pairing, "This server has replicas of its own");
            Assert.Null((await ReplicationPair.GetStatusAsync(_pair.Replica.AdminClient)).Source);
        }

        [Fact]
        public async Task Pair_ACodeThatRanOut_IsRefused()
        {
            string code = await _pair.CreateCodeAsync();
            await ExecuteOnAsync(_pair.Primary, "UPDATE replica_invite SET expires_at = '2000-01-01T00:00:00.0000000Z'");

            HttpResponseMessage pairing = await _pair.TryPairAsync(code);

            await AssertRefusedAsync(pairing, "The primary did not accept the code");
            Assert.Null((await ReplicationPair.GetStatusAsync(_pair.Replica.AdminClient)).Source);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("bsr_not-a-credential")]
        public async Task Feed_WithoutAReplicasCredential_IsUnauthorized(string? credential)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/replication/feed/changes?after=0");
            if (credential != null)
            {
                request.Headers.Add(ReplicaCredentials.HeaderName, credential);
            }

            HttpResponseMessage response = await _pair.Primary.AnonymousClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public void PrimaryClient_FollowsNoRedirects()
        {
            HttpMessageHandler handler = _pair.Primary.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(PrimaryClient.HttpClientName);
            while (handler is DelegatingHandler delegating)
            {
                handler = delegating.InnerHandler!;
            }

            Assert.False(Assert.IsType<SocketsHttpHandler>(handler).AllowAutoRedirect);
        }

        /// <summary>Hands the replica other bytes than the primary sent for every file it fetches.</summary>
        private sealed class TamperingHandler : DelegatingHandler
        {
            public TamperingHandler(HttpMessageHandler primary)
                : base(primary)
            {
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                if (request.RequestUri!.AbsolutePath.EndsWith("/object", StringComparison.Ordinal))
                {
                    response.Content = new StringContent("other bytes");
                }

                return response;
            }
        }

        /// <summary>Answers 404 for one file's bytes, as the primary does once that file no longer holds them.</summary>
        private sealed class MissingContentsHandler : DelegatingHandler
        {
            private readonly Func<Guid> _missing;

            public MissingContentsHandler(HttpMessageHandler primary, Func<Guid> missing)
                : base(primary)
            {
                _missing = missing;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith($"/files/{_missing():D}/object", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return await base.SendAsync(request, cancellationToken);
            }
        }

        /// <summary>Answers every file's bytes with headers and then nothing, like a connection that died without closing.</summary>
        private sealed class StallingHandler : DelegatingHandler
        {
            private readonly TaskCompletionSource _stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            public StallingHandler(HttpMessageHandler primary)
                : base(primary)
            {
            }

            /// <summary>Completes once a file's bytes have stalled.</summary>
            public Task Stalled => _stalled.Task;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                if (request.RequestUri!.AbsolutePath.EndsWith("/object", StringComparison.Ordinal))
                {
                    response.Content = new StreamContent(new SilentStream(_stalled));
                }

                return response;
            }
        }

        private sealed class SilentStream : MemoryStream
        {
            private readonly TaskCompletionSource _reading;

            public SilentStream(TaskCompletionSource reading)
            {
                _reading = reading;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                _reading.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }
        }

        /// <summary>
        /// Hands the replica every file's bytes one at a time, moving <paramref name="clock"/> on 100 ms
        /// before each: longer in all than the idle timeout, never idle that long.
        /// </summary>
        private sealed class TricklingHandler : DelegatingHandler
        {
            private readonly FakeTimeProvider _clock;

            public TricklingHandler(HttpMessageHandler primary, FakeTimeProvider clock)
                : base(primary)
            {
                _clock = clock;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                if (request.RequestUri!.AbsolutePath.EndsWith("/object", StringComparison.Ordinal) && response.IsSuccessStatusCode)
                {
                    response.Content = new StreamContent(new TricklingStream(await response.Content.ReadAsByteArrayAsync(cancellationToken), _clock));
                }

                return response;
            }
        }

        private sealed class TricklingStream : MemoryStream
        {
            private readonly FakeTimeProvider _clock;

            public TricklingStream(byte[] bytes, FakeTimeProvider clock)
                : base(bytes)
            {
                _clock = clock;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                _clock.Advance(TimeSpan.FromMilliseconds(100));
                return base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }
        }

        /// <summary>Asks the primary for one entry a page, whatever the replica asked for.</summary>
        private sealed class SmallPagesHandler : DelegatingHandler
        {
            public SmallPagesHandler(HttpMessageHandler primary)
                : base(primary)
            {
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
                if (query.ContainsKey("limit"))
                {
                    query["limit"] = "1";
                    request.RequestUri = new Uri(QueryHelpers.AddQueryString(request.RequestUri.GetLeftPart(UriPartial.Path), query));
                }

                return base.SendAsync(request, cancellationToken);
            }
        }

        /// <summary>
        /// A change to make on the primary once, just before the replica asks for the page of a whole copy
        /// after <see cref="CopiedThrough"/>: by then the pages up to it are copied and their files fetched.
        /// </summary>
        private sealed class ChangeBetweenCopyPages
        {
            public long CopiedThrough { get; set; } = long.MaxValue;

            public Func<Task>? Change { get; set; }

            public bool Made { get; set; }
        }

        /// <summary>Makes <see cref="ChangeBetweenCopyPages"/>'s change when its page is asked for.</summary>
        private sealed class BetweenCopyPagesHandler : DelegatingHandler
        {
            private readonly ChangeBetweenCopyPages _between;

            public BetweenCopyPagesHandler(HttpMessageHandler primary, ChangeBetweenCopyPages between)
                : base(primary)
            {
                _between = between;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                bool copyPage = request.RequestUri!.AbsolutePath.EndsWith("/feed/copy", StringComparison.Ordinal);
                if (copyPage && _between.Change != null && !_between.Made
                    && long.Parse(QueryHelpers.ParseQuery(request.RequestUri.Query)["after"].ToString(), CultureInfo.InvariantCulture) >= _between.CopiedThrough)
                {
                    _between.Made = true;
                    await _between.Change();
                }

                return await base.SendAsync(request, cancellationToken);
            }
        }

        /// <summary>The way to the primary, until <see cref="Down"/> is set.</summary>
        private sealed class UnreachableHandler : DelegatingHandler
        {
            public UnreachableHandler(HttpMessageHandler primary)
                : base(primary)
            {
            }

            public bool Down { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Down
                    ? Task.FromException<HttpResponseMessage>(new HttpRequestException("The primary is down."))
                    : base.SendAsync(request, cancellationToken);
            }
        }

        /// <summary>Asserts a 409 whose message starts with <paramref name="reason"/>, so each refusal is told apart.</summary>
        private static async Task AssertRefusedAsync(HttpResponseMessage response, string reason)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.StartsWith(reason, (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Message, StringComparison.Ordinal);
        }

        private static async Task<List<Guid>> UserIdsAsync(HttpClient client)
        {
            List<UserInfo> users = (await client.GetFromJsonAsync<List<UserInfo>>("/api/users"))!;
            return users.Select(user => user.Id).Order().ToList();
        }

        private static async Task<long> LatestSequenceAsync(ServerTestHost host)
        {
            using IServiceScope scope = host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IChangeLogRepository>().GetLatestSequenceAsync();
        }

        private static async Task ExecuteOnAsync(ServerTestHost host, string sql)
        {
            using IServiceScope scope = host.Services.CreateScope();
            await using IDbTransactionSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            await session.Connection.ExecuteAsync(sql, transaction: session.Transaction);
            await session.CommitAsync();
        }

        private static async Task<T> QueryOnAsync<T>(ServerTestHost host, string sql)
        {
            using IServiceScope scope = host.Services.CreateScope();
            await using IDbSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionAsync();
            return await session.Connection.QuerySingleAsync<T>(sql, transaction: session.Transaction);
        }

        /// <summary>Drops the primary's log after <paramref name="sequence"/> and numbers on from there, as a restored backup would.</summary>
        private async Task RewindPrimaryLogAsync(long sequence)
        {
            await ExecuteOnAsync(_pair.Primary, $"DELETE FROM change_log WHERE sequence > {sequence}; UPDATE sqlite_sequence SET seq = {sequence} WHERE name = 'change_log'");
        }

        /// <summary>Compacts the primary's log as if every entry were older than the retention; this host runs on the real clock.</summary>
        private async Task CompactPrimaryAsync()
        {
            using IServiceScope scope = _pair.Primary.Services.CreateScope();
            await using IDbTransactionSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IChangeLogRepository>().CompactAsync(DateTimeOffset.UtcNow.AddYears(1), session);
            await session.CommitAsync();
        }
    }
}
