using System.Globalization;
using System.Net;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// Emails this server's admins when replication has gone wrong for longer than
    /// <see cref="ReplicationOptions.AlertAfter"/>, and again when it recovers. A primary watches each
    /// replica: one not seen, or one following the log whose oldest change still to apply is older than
    /// that. A replica watches its primary: following it has failed every time for longer than that,
    /// which is also what a primary that is down looks like. One email when a problem starts, one when it
    /// clears, and while it lasts at most one a <see cref="RepeatInterval"/>; what was sent is kept in the
    /// database, so a restart sends nothing twice.
    /// </summary>
    public sealed class ReplicationAlerts
    {
        /// <summary>How often a problem that lasts is emailed again.</summary>
        public static readonly TimeSpan RepeatInterval = TimeSpan.FromDays(1);

        private readonly IReplicaRepository _replicaRepository;
        private readonly IReplicationSourceRepository _sourceRepository;
        private readonly IChangeLogRepository _changeLog;
        private readonly IReplicationAlertRepository _alertRepository;
        private readonly IUserService _userService;
        private readonly IEmailSender _emailSender;
        private readonly ReplicationOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ReplicationAlerts> _logger;

        public ReplicationAlerts(
            IReplicaRepository replicaRepository,
            IReplicationSourceRepository sourceRepository,
            IChangeLogRepository changeLog,
            IReplicationAlertRepository alertRepository,
            IUserService userService,
            IEmailSender emailSender,
            ReplicationOptions options,
            TimeProvider timeProvider,
            ILogger<ReplicationAlerts> logger)
        {
            _replicaRepository = replicaRepository;
            _sourceRepository = sourceRepository;
            _changeLog = changeLog;
            _alertRepository = alertRepository;
            _userService = userService;
            _emailSender = emailSender;
            _options = options;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <summary>Looks at replication as it stands and sends whatever alert that calls for; answers how many it sent.</summary>
        public async Task<int> CheckAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            IReadOnlyList<Watched> watched = await WatchedAsync(now);
            Dictionary<string, ReplicationAlert> open = (await _alertRepository.GetAllAsync()).ToDictionary(alert => alert.Subject, StringComparer.Ordinal);
            int alerted = 0;

            foreach (Watched subject in watched)
            {
                open.TryGetValue(subject.Subject, out ReplicationAlert? alert);
                if (subject.Problem is not Problem problem)
                {
                    if (alert != null)
                    {
                        await SendAsync(
                            $"Replication recovered: {subject.Concerns}",
                            $"{_options.PublicUrl} sees replication working again for {subject.Concerns}, as of {Format(now)}, after a problem since {Format(alert.Since)}. The problem was: {alert.Problem}",
                            cancellationToken);
                        await _alertRepository.DeleteAsync(subject.Subject);
                        alerted++;
                    }

                    continue;
                }

                DateTimeOffset lastSentAt;
                if (alert == null)
                {
                    await SendAsync(
                        $"Replication problem: {subject.Concerns}",
                        $"{_options.PublicUrl} noticed a replication problem. {problem.Description} You get another email when it clears, and a reminder once a day while it lasts.",
                        cancellationToken);
                    lastSentAt = now;
                    alerted++;
                }
                else if (now - alert.LastSentAt >= RepeatInterval)
                {
                    await SendAsync(
                        $"Replication problem continues: {subject.Concerns}",
                        $"{_options.PublicUrl} still sees a replication problem. {problem.Description}",
                        cancellationToken);
                    lastSentAt = now;
                    alerted++;
                }
                else if (!string.Equals(alert.Problem, problem.Description, StringComparison.Ordinal))
                {
                    // Kept current without an email, so the one saying it cleared quotes it as it last was.
                    lastSentAt = alert.LastSentAt;
                }
                else
                {
                    continue;
                }

                // A problem that changes kind (a replica unseen, then seen but behind) began with the first.
                DateTimeOffset since = alert != null && alert.Since < problem.Since ? alert.Since : problem.Since;
                await _alertRepository.SaveAsync(new ReplicationAlert { Subject = subject.Subject, Problem = problem.Description, Since = since, LastSentAt = lastSentAt });
            }

            // A replica removed, or a server no longer a replica, has nothing left to recover.
            HashSet<string> watchedSubjects = watched.Select(subject => subject.Subject).ToHashSet(StringComparer.Ordinal);
            foreach (string gone in open.Keys.Where(subject => !watchedSubjects.Contains(subject)))
            {
                _logger.LogInformation("Dropping the replication alert for {Subject}, which this server no longer watches.", gone);
                await _alertRepository.DeleteAsync(gone);
            }

            return alerted;
        }

        /// <summary>What this server watches, each with its problem now, if it has one.</summary>
        private async Task<IReadOnlyList<Watched>> WatchedAsync(DateTimeOffset now)
        {
            List<Watched> watched = new List<Watched>();
            DateTimeOffset longAgo = now - _options.AlertAfter;

            foreach (Replica replica in await _replicaRepository.GetAllAsync())
            {
                string concerns = $"replica '{replica.Name}'";
                watched.Add(new Watched(ReplicationAlert.ForReplica(replica.Id), concerns, await ReplicaProblemAsync(replica, concerns, longAgo)));
            }

            if (await _sourceRepository.GetAsync() is ReplicationSource source)
            {
                string concerns = $"the primary {source.PrimaryUrl}";
                watched.Add(new Watched(ReplicationAlert.Primary, concerns, PrimaryProblem(source, concerns, longAgo)));
            }

            return watched;
        }

        /// <summary>
        /// A replica's problem as its primary sees it: not seen since before <paramref name="longAgo"/>, or
        /// following the log but behind a change made before then. How far it had applied is as of its last
        /// request for changes, so a replica not seen is never also counted behind, nor is one taking a
        /// whole copy, which brings it every change made before the copy began.
        /// </summary>
        private async Task<Problem?> ReplicaProblemAsync(Replica replica, string concerns, DateTimeOffset longAgo)
        {
            DateTimeOffset seenAt = replica.LastSeenAt ?? replica.PairedAt;
            if (seenAt < longAgo)
            {
                return new Problem($"{Capitalized(concerns)} has not been in touch with this server since {Format(seenAt)}.", seenAt);
            }

            if (replica.Copying)
            {
                return null;
            }

            // The Replication page counts the same lag in changes rather than time: both are above zero
            // exactly when a change is unapplied. Compaction may have dropped older changes it has yet
            // to apply; the oldest left is then younger than the one it lacks, and it is counted behind a
            // little later than it is.
            IReadOnlyList<ChangeLogEntry> unapplied = await _changeLog.GetAfterAsync(replica.AppliedSequence, limit: 1);
            ChangeLogEntry? oldestUnapplied = unapplied.Count > 0 ? unapplied[0] : null;
            if (oldestUnapplied != null && oldestUnapplied.CreatedAt < longAgo)
            {
                return new Problem($"{Capitalized(concerns)} has not applied the changes made here since {Format(oldestUnapplied.CreatedAt)}.", oldestUnapplied.CreatedAt);
            }

            return null;
        }

        /// <summary>
        /// The primary's problem as a replica sees it: every step since before <paramref name="longAgo"/>
        /// has failed. A whole copy taking long without failing is not one.
        /// </summary>
        private static Problem? PrimaryProblem(ReplicationSource source, string concerns, DateTimeOffset longAgo)
        {
            if (source.FailingSince is not DateTimeOffset failingSince || failingSince >= longAgo)
            {
                return null;
            }

            return new Problem($"This server has been failing to follow {concerns} since {Format(failingSince)}. The last attempt failed: {source.LastError}", failingSince);
        }

        /// <summary>
        /// Logs the alert as a warning, for whoever watches the server's log, then sends it as one email
        /// to every admin. A send that fails is logged and the rest go on; what was sent is recorded
        /// either way, so a mail provider that is down does not have every check send it again.
        /// </summary>
        private async Task SendAsync(string subject, string text, CancellationToken cancellationToken)
        {
            _logger.LogWarning("Replication alert to the admins: {Subject}. {Text}", subject, text);

            List<User> admins = (await _userService.GetAllUsersAsync()).Where(user => user.Roles.Contains(UserRoles.Admin)).ToList();
            if (admins.Count == 0)
            {
                _logger.LogWarning("There is no admin to email this replication alert to.");
                return;
            }

            foreach (User admin in admins)
            {
                EmailMessage message = new EmailMessage
                {
                    To = admin.Email,
                    Subject = subject,
                    TextBody = text,
                    HtmlBody = $"<p>{WebUtility.HtmlEncode(text)}</p>",
                    Tag = "replication-alert",
                };

                EmailSendResult result = await _emailSender.SendAsync(message, cancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Could not email {Email} about replication ({Subject}): {Error}", admin.Email, subject, result.ErrorMessage);
                }
            }
        }

        private static string Format(DateTimeOffset at)
        {
            return at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }

        private static string Capitalized(string text)
        {
            return char.ToUpperInvariant(text[0]) + text[1..];
        }

        /// <summary>Something this server watches, by its alert's subject: what it concerns, in an email's words, and its problem now.</summary>
        private sealed record Watched(string Subject, string Concerns, Problem? Problem);

        /// <summary>What is wrong, as a sentence, and since when.</summary>
        private sealed record Problem(string Description, DateTimeOffset Since);
    }
}
