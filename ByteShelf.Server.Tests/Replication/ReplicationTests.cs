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
using Microsoft.Extensions.DependencyInjection;

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
            await using ReplicationPair quiet = await ReplicationPair.StartAsync(
                primary => new StallingHandler(primary),
                idleTimeout: TimeSpan.FromMilliseconds(200));
            await quiet.CreateShelfAsync("docs");
            await quiet.PutOnPrimaryAsync("docs", "/a.txt", "never arrives");
            await quiet.PairAsync();

            await Assert.ThrowsAsync<PrimaryFailedException>(() => quiet.SyncAsync().WaitAsync(TimeSpan.FromSeconds(30)));
        }

        [Fact]
        public async Task Sync_FileThatArrivesSlowlyButSteadily_IsNotGivenUp()
        {
            await using ReplicationPair slow = await ReplicationPair.StartAsync(
                primary => new TricklingHandler(primary),
                idleTimeout: TimeSpan.FromMilliseconds(500));
            await slow.CreateShelfAsync("docs");
            await slow.PutOnPrimaryAsync("docs", "/a.txt", "one byte at a time");
            await slow.PairAsync();

            await slow.SyncAsync();

            using HttpClient replica = await slow.SignInOnReplicaAsync();
            Assert.Equal("one byte at a time", await replica.GetStringAsync("/api/shelves/docs/content?path=/a.txt"));
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
            await _pair.PutOnPrimaryAsync("docs", "/a.txt", "a");

            ReplicationStatus before = await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient);

            // Twice: the primary learns how far the replica got only from its next request.
            await _pair.SyncAsync();
            await _pair.SyncAsync();
            ReplicationStatus after = await ReplicationPair.GetStatusAsync(_pair.Primary.AdminClient);

            Assert.Equal("backup", Assert.Single(before.Replicas).Name);
            Assert.True(before.Replicas[0].ChangesBehind > 0);
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

            Assert.Equal(HttpStatusCode.Conflict, pairing.StatusCode);
        }

        [Fact]
        public async Task Pair_ACodeMadeOnTheSameServer_IsRefused()
        {
            HttpResponseMessage made = await _pair.Replica.AdminClient.PostAsync("/api/replication/codes", null);
            string ownCode = (await made.Content.ReadFromJsonAsync<PairingCodeResponse>())!.Code;

            HttpResponseMessage pairing = await _pair.TryPairAsync(ownCode);

            Assert.Equal(HttpStatusCode.Conflict, pairing.StatusCode);
        }

        [Fact]
        public async Task Pair_AServerThatIsAlreadyAReplica_IsRefused()
        {
            await _pair.PairAsync();

            HttpResponseMessage again = await _pair.TryPairAsync(await _pair.CreateCodeAsync());

            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }

        [Fact]
        public async Task Pair_AServerWithReplicasOfItsOwn_IsRefused()
        {
            HttpResponseMessage made = await _pair.Replica.AdminClient.PostAsync("/api/replication/codes", null);
            string secret = PairingCode.TryParse((await made.Content.ReadFromJsonAsync<PairingCodeResponse>())!.Code)!.Secret;
            (await _pair.Replica.AnonymousClient.PostAsJsonAsync("/api/replication/feed/accept", new AcceptPairingRequest { Secret = secret, Name = "its own" })).EnsureSuccessStatusCode();

            HttpResponseMessage pairing = await _pair.TryPairAsync(await _pair.CreateCodeAsync());

            Assert.Equal(HttpStatusCode.Conflict, pairing.StatusCode);
        }

        [Fact]
        public async Task Pair_ACodeThatRanOut_IsRefused()
        {
            string code = await _pair.CreateCodeAsync();
            await ExecuteOnAsync(_pair.Primary, "UPDATE replica_invite SET expires_at = '2000-01-01T00:00:00.0000000Z'");

            HttpResponseMessage pairing = await _pair.TryPairAsync(code);

            Assert.Equal(HttpStatusCode.Conflict, pairing.StatusCode);
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

        /// <summary>Hands the replica every file's bytes one at a time, 100 ms apart: longer in all than the idle timeout, never idle that long.</summary>
        private sealed class TricklingHandler : DelegatingHandler
        {
            public TricklingHandler(HttpMessageHandler primary)
                : base(primary)
            {
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                if (request.RequestUri!.AbsolutePath.EndsWith("/object", StringComparison.Ordinal) && response.IsSuccessStatusCode)
                {
                    response.Content = new StreamContent(new TricklingStream(await response.Content.ReadAsByteArrayAsync(cancellationToken)));
                }

                return response;
            }
        }

        private sealed class TricklingStream : MemoryStream
        {
            public TricklingStream(byte[] bytes)
                : base(bytes)
            {
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
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
                request.RequestUri = new Uri(request.RequestUri!.ToString().Replace($"limit={ReplicaSync.PageSize}", "limit=1", StringComparison.Ordinal));
                return base.SendAsync(request, cancellationToken);
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
