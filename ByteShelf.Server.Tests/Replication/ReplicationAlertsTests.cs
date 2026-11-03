using System.Globalization;
using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.Replication
{
    public class ReplicationAlertsTests : ServiceTestBase
    {
        private const string FirstAdmin = "first-admin@example.com";
        private const string SecondAdmin = "second-admin@example.com";

        private static readonly TimeSpan JustOver = ReplicationOptions.DefaultAlertAfter + TimeSpan.FromMinutes(1);

        private static readonly TimeSpan JustUnder = ReplicationOptions.DefaultAlertAfter - TimeSpan.FromMinutes(1);

        private static readonly string[] BothAdmins = { FirstAdmin, SecondAdmin };

        private readonly RecordingSender _sender = new RecordingSender();

        private readonly RecordingLoggerFactory _logs = new RecordingLoggerFactory();

        private ReplicationAlerts Alerts => Services.GetRequiredService<ReplicationAlerts>();

        private IReplicaRepository Replicas => Services.GetRequiredService<IReplicaRepository>();

        private IReplicationSourceRepository Source => Services.GetRequiredService<IReplicationSourceRepository>();

        private IReplicationAlertRepository AlertRecords => Services.GetRequiredService<IReplicationAlertRepository>();

        private IEnumerable<string> Recipients => _sender.Sent.Select(message => message.To).Order();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await CreateUserAsync(FirstAdmin, UserRoles.Admin);
            await CreateUserAsync(SecondAdmin, UserRoles.Admin);
            await CreateUserAsync("user@example.com");
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddReplicationPairing(AppBaseUrl);
            services.AddReplicationAlerts();
            services.AddScoped<IReplicationFeed, ReplicationFeed>();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_sender);
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(_logs);
        }

        [Fact]
        public async Task CheckAsync_AProblem_IsLoggedWhole()
        {
            await PairReplicaAsync("backup");
            Clock.Advance(JustOver);

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Contains(_logs.Messages, message => message.Contains("Replication problem: replica 'backup'", StringComparison.Ordinal)
                && message.Contains($"has not been in touch with this server since {Shown(TestInstants.Now)}.", StringComparison.Ordinal));
        }

        [Fact]
        public async Task CheckAsync_AReplicaNotSeenForLongerThanTheThreshold_EmailsEveryAdminOnce()
        {
            await PairReplicaAsync("backup");
            Clock.Advance(JustOver);

            await Alerts.CheckAsync(CancellationToken.None);
            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Equal(BothAdmins, Recipients);
            EmailMessage message = _sender.Sent[0];
            Assert.Equal("Replication problem: replica 'backup'", message.Subject);
            Assert.Contains($"{AppBaseUrl} noticed a replication problem. Replica 'backup' has not been in touch with this server since {Shown(TestInstants.Now)}.", message.TextBody, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckAsync_AReplicaNotSeenForJustUnderTheThreshold_SendsNothing()
        {
            await PairReplicaAsync("backup");
            Clock.Advance(JustUnder);

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task CheckAsync_AReplicaBothNotSeenAndBehind_IsReportedOnlyAsNotInTouch()
        {
            await PairReplicaAsync("backup");
            await CreateShelfAsync("docs");
            Clock.Advance(JustOver);

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Contains("has not been in touch", _sender.Sent[0].TextBody, StringComparison.Ordinal);
            Assert.DoesNotContain("has not applied", _sender.Sent[0].TextBody, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckAsync_AReplicaTakingACopyLongerThanTheThresholdWhileAskingForPagesAndFiles_SendsNothing()
        {
            Replica replica = await PairReplicaAsync("backup");
            IReplicationFeed feed = Services.GetRequiredService<IReplicationFeed>();
            Clock.Advance(JustUnder);
            await feed.GetCopyAsync(replica, 0, limit: 100);
            Clock.Advance(JustUnder);
            await feed.GetObjectPathAsync(replica, Guid.NewGuid(), "not-a-file-here");
            Clock.Advance(JustUnder);

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task CheckAsync_AReplicaFollowingButBehindAChangeOlderThanTheThreshold_EmailsSinceThatChange()
        {
            Replica replica = await PairReplicaAsync("backup");
            long caughtUpTo = await ChangeLog.GetLatestSequenceAsync();
            Clock.Advance(TimeSpan.FromMinutes(5));
            DateTimeOffset changedAt = Clock.GetUtcNow();
            await CreateShelfAsync("docs");
            Clock.Advance(JustOver);
            await Replicas.RecordFollowingAsync(replica.Id, caughtUpTo, Clock.GetUtcNow());

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Contains($"Replica 'backup' has not applied the changes made here since {Shown(changedAt)}.", _sender.Sent[0].TextBody, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckAsync_AReplicaThatCopiedThenFollowsBehindAChangeOlderThanTheThreshold_EmailsThatItIsBehind()
        {
            Replica replica = await PairReplicaAsync("backup");
            IReplicationFeed feed = Services.GetRequiredService<IReplicationFeed>();
            ChangeFeedPage copy = await feed.GetCopyAsync(replica, 0, limit: 100);
            await CreateShelfAsync("docs");
            Clock.Advance(JustOver);
            await feed.GetChangesAsync(replica, copy.LatestSequence, null, limit: 100);

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Contains("Replica 'backup' has not applied the changes made here since", _sender.Sent[0].TextBody, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckAsync_AReplicaFollowingButBehindAChangeJustUnderTheThreshold_SendsNothing()
        {
            Replica replica = await PairReplicaAsync("backup");
            long caughtUpTo = await ChangeLog.GetLatestSequenceAsync();
            await CreateShelfAsync("docs");
            Clock.Advance(JustUnder);
            await Replicas.RecordFollowingAsync(replica.Id, caughtUpTo, Clock.GetUtcNow());

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task CheckAsync_AReplicaSeenAndCaughtUp_SendsNothing()
        {
            Replica replica = await PairReplicaAsync("backup");
            await CreateShelfAsync("docs");
            Clock.Advance(JustOver);
            await Replicas.RecordFollowingAsync(replica.Id, await ChangeLog.GetLatestSequenceAsync(), Clock.GetUtcNow());

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task CheckAsync_AProblemThatClears_EmailsEveryAdminOnceThatItRecovered()
        {
            Replica replica = await PairReplicaAsync("backup");
            Clock.Advance(JustOver);
            await Alerts.CheckAsync(CancellationToken.None);
            _sender.Sent.Clear();
            await Replicas.RecordFollowingAsync(replica.Id, await ChangeLog.GetLatestSequenceAsync(), Clock.GetUtcNow());

            await Alerts.CheckAsync(CancellationToken.None);
            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Equal(BothAdmins, Recipients);
            Assert.Equal("Replication recovered: replica 'backup'", _sender.Sent[0].Subject);
            Assert.Contains(
                $"working again for replica 'backup', as of {Shown(TestInstants.Now + JustOver)}, after a problem since {Shown(TestInstants.Now)}. The problem was: Replica 'backup' has not been in touch",
                _sender.Sent[0].TextBody,
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckAsync_AProblemThatChangesKind_IsQuotedAsItLastWasAndSinceItBeganWhenItClears()
        {
            Clock.Advance(TimeSpan.FromMinutes(10));
            Replica replica = await PairReplicaAsync("backup");
            Clock.Advance(JustOver);
            await Alerts.CheckAsync(CancellationToken.None);
            await Replicas.RecordFollowingAsync(replica.Id, 0, Clock.GetUtcNow());
            await Alerts.CheckAsync(CancellationToken.None);
            Assert.Equal(BothAdmins, Recipients);
            _sender.Sent.Clear();
            await Replicas.RecordFollowingAsync(replica.Id, await ChangeLog.GetLatestSequenceAsync(), Clock.GetUtcNow());

            await Alerts.CheckAsync(CancellationToken.None);

            // Unseen since it was paired, then behind the admins made before it was.
            Assert.Contains(
                $"after a problem since {Shown(TestInstants.Now)}. The problem was: Replica 'backup' has not applied the changes made here since {Shown(TestInstants.Now)}.",
                _sender.Sent[0].TextBody,
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckAsync_AProblemThatLastsJustUnderADay_IsNotEmailedAgain()
        {
            await PairReplicaAsync("backup");
            Clock.Advance(JustOver);
            await Alerts.CheckAsync(CancellationToken.None);
            _sender.Sent.Clear();
            Clock.Advance(ReplicationAlerts.RepeatInterval - TimeSpan.FromMinutes(1));

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task CheckAsync_AProblemThatLastsADay_EmailsEveryAdminOnceAgain()
        {
            await PairReplicaAsync("backup");
            Clock.Advance(JustOver);
            await Alerts.CheckAsync(CancellationToken.None);
            _sender.Sent.Clear();
            Clock.Advance(ReplicationAlerts.RepeatInterval);

            await Alerts.CheckAsync(CancellationToken.None);
            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Equal(BothAdmins, Recipients);
            Assert.All(_sender.Sent, message => Assert.Equal("Replication problem continues: replica 'backup'", message.Subject));
        }

        [Fact]
        public async Task CheckAsync_AsANewServiceAfterARestart_DoesNotEmailAProblemAgain()
        {
            await PairReplicaAsync("backup");
            Clock.Advance(JustOver);
            await Alerts.CheckAsync(CancellationToken.None);
            _sender.Sent.Clear();

            using (IServiceScope restarted = Services.CreateScope())
            {
                await restarted.ServiceProvider.GetRequiredService<ReplicationAlerts>().CheckAsync(CancellationToken.None);
            }

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task CheckAsync_ASendThatFails_IsLoggedTheOtherAdminsAreStillEmailedAndItIsNotSentAgain()
        {
            _sender.FailFor.Add(FirstAdmin);
            await PairReplicaAsync("backup");
            Clock.Advance(JustOver);

            await Alerts.CheckAsync(CancellationToken.None);
            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Equal(BothAdmins, Recipients);
            Assert.Contains(_logs.Messages, message => message.Contains($"Could not email {FirstAdmin} about replication", StringComparison.Ordinal));
        }

        [Fact]
        public async Task CheckAsync_OnAReplicaWithNoUsersBeforeItsFirstCopy_LogsTheProblemOnceAndRecordsIt()
        {
            // As a replica stands before its first copy brings it its primary's users.
            await using (IDbSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionAsync())
            {
                await session.Connection.ExecuteAsync("DELETE FROM user_account");
            }

            await TestReplicas.MarkAsReplicaAsync(Services, needsCopy: true, pairedAt: Clock.GetUtcNow());
            await Source.RecordSyncErrorAsync("The primary could not be reached.", Clock.GetUtcNow());
            Clock.Advance(JustOver);

            await Alerts.CheckAsync(CancellationToken.None);
            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
            Assert.Single(_logs.Messages, message => message.Contains("Replication problem: the primary", StringComparison.Ordinal));
            Assert.Contains(_logs.Messages, message => message.Contains("There is no admin to email", StringComparison.Ordinal));
            Assert.Single(await AlertRecords.GetAllAsync());
        }

        [Fact]
        public async Task CheckAsync_AReplicaRemovedWhileItHadAProblem_IsForgottenWithoutARecoveryEmail()
        {
            Replica replica = await PairReplicaAsync("backup");
            Clock.Advance(JustOver);
            await Alerts.CheckAsync(CancellationToken.None);
            _sender.Sent.Clear();
            await Replicas.DeleteAsync(replica.Id);

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
            Assert.Empty(await AlertRecords.GetAllAsync());
        }

        [Fact]
        public async Task CheckAsync_OnAReplicaWhoseStepsHaveFailedForLongerThanTheThreshold_EmailsWhyAndSinceTheFirstFailure()
        {
            await TestReplicas.MarkAsReplicaAsync(Services, needsCopy: true, pairedAt: Clock.GetUtcNow());
            Clock.Advance(TimeSpan.FromMinutes(1));
            DateTimeOffset firstFailure = Clock.GetUtcNow();
            await Source.RecordSyncErrorAsync("The primary could not be reached.", firstFailure);
            Clock.Advance(JustOver);
            await Source.RecordSyncErrorAsync("The primary answered 502.", Clock.GetUtcNow());

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Equal(BothAdmins, Recipients);
            EmailMessage message = _sender.Sent[0];
            Assert.Equal($"Replication problem: the primary {TestReplicas.PrimaryUrl}", message.Subject);
            Assert.Contains(
                $"This server has been failing to follow the primary {TestReplicas.PrimaryUrl} since {Shown(firstFailure)}. The last attempt failed: The primary answered 502.",
                message.TextBody,
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckAsync_OnAReplicaTakingLongOverACopyWithoutFailing_SendsNothing()
        {
            await TestReplicas.MarkAsReplicaAsync(Services, needsCopy: true, pairedAt: Clock.GetUtcNow());
            Clock.Advance(JustOver);

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task CheckAsync_OnAReplicaWhoseLongCopyFailedOnlyJustNow_SendsNothing()
        {
            await TestReplicas.MarkAsReplicaAsync(Services, needsCopy: true, pairedAt: Clock.GetUtcNow());
            Clock.Advance(JustOver + JustOver);
            await Source.RecordSyncErrorAsync("The primary could not be reached.", Clock.GetUtcNow());

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task CheckAsync_OnAReplicaWhoseStepWorksAgain_EmailsEveryAdminThatItRecovered()
        {
            await TestReplicas.MarkAsReplicaAsync(Services, needsCopy: true, pairedAt: Clock.GetUtcNow());
            await Source.RecordSyncErrorAsync("The primary could not be reached.", Clock.GetUtcNow());
            Clock.Advance(JustOver);
            await Alerts.CheckAsync(CancellationToken.None);
            _sender.Sent.Clear();
            await using (IDbTransactionSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync())
            {
                ReplicationSource source = (await Source.GetAsync(session))!;
                Assert.True(await Source.SetProgressAsync(source, 1, appliedFingerprint: null, Clock.GetUtcNow(), session));
                await session.CommitAsync();
            }

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Equal(BothAdmins, Recipients);
            Assert.Equal($"Replication recovered: the primary {TestReplicas.PrimaryUrl}", _sender.Sent[0].Subject);
        }

        [Fact]
        public async Task CheckAsync_OnAReplicaPromotedWhileItHadAProblem_IsForgottenWithoutARecoveryEmail()
        {
            await TestReplicas.MarkAsReplicaAsync(Services, needsCopy: true, pairedAt: Clock.GetUtcNow());
            await Source.RecordSyncErrorAsync("The primary could not be reached.", Clock.GetUtcNow());
            Clock.Advance(JustOver);
            await Alerts.CheckAsync(CancellationToken.None);
            _sender.Sent.Clear();
            await Source.DeleteAsync();

            await Alerts.CheckAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
            Assert.Empty(await AlertRecords.GetAllAsync());
        }

        [Theory]
        [InlineData(null, 60)]
        [InlineData("15", 15)]
        [InlineData("43200", 43200)]
        public void AlertAfterFrom_UnsetOrMinutes_IsThatLong(string? minutes, int expected)
        {
            Assert.Equal(TimeSpan.FromMinutes(expected), ReplicationOptions.AlertAfterFrom(minutes));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("1.5")]
        [InlineData("an hour")]
        [InlineData("43201")]
        [InlineData("2147483647")]
        public void AlertAfterFrom_NotAWholeNumberOfMinutesFromOneToThirtyDays_Throws(string minutes)
        {
            Assert.Throws<InvalidOperationException>(() => ReplicationOptions.AlertAfterFrom(minutes));
        }

        /// <summary>A time as the alerts give it.</summary>
        private static string Shown(DateTimeOffset at)
        {
            return at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }

        /// <summary>A replica paired now as pairing makes one: nothing applied, and not seen yet.</summary>
        private async Task<Replica> PairReplicaAsync(string name)
        {
            Replica replica = new Replica { Id = Guid.NewGuid(), Name = name, PairedAt = Clock.GetUtcNow(), AppliedSequence = 0 };
            await using IDbTransactionSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            await Replicas.InsertReplicaAsync(replica, TestHashes.Sha256Of(name), session);
            await session.CommitAsync();
            return replica;
        }

        /// <summary>Records every message it is given; a send to an address in <see cref="FailFor"/> fails.</summary>
        private sealed class RecordingSender : IEmailSender
        {
            public List<EmailMessage> Sent { get; } = new List<EmailMessage>();

            public HashSet<string> FailFor { get; } = new HashSet<string>(StringComparer.Ordinal);

            public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            {
                Sent.Add(message);
                return Task.FromResult(FailFor.Contains(message.To)
                    ? new EmailSendResult { IsSuccess = false, ErrorMessage = "The mail provider is down." }
                    : new EmailSendResult { IsSuccess = true });
            }
        }
    }
}
