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
    /// database, so a restart sends nothing twice. Silence and failing count only from the server's own
    /// start, and an open problem clears only once replication is known to work.
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

        /// <summary>
        /// Looks at replication as it stands and sends whatever alert that calls for; answers how many it
        /// sent. Silence and failing count only from <paramref name="watchingSince"/>, when this server
        /// started: nothing could reach it, nor could it try anything, while it was down.
        /// </summary>
        public async Task<int> CheckAsync(DateTimeOffset watchingSince, CancellationToken cancellationToken)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            Dictionary<string, ReplicationAlert> open = (await _alertRepository.GetAllAsync()).ToDictionary(alert => alert.Subject, StringComparer.Ordinal);
            IReadOnlyList<Watched> watched = await WatchedAsync(now, watchingSince, open);
            int alerted = 0;

            foreach (Watched subject in watched)
            {
                open.TryGetValue(subject.Subject, out ReplicationAlert? alert);
                if (subject.Problem is not Problem problem)
                {
                    // Undecided (too short to count, or nothing heard since this server started) may be an
                    // open problem going on, so it does not clear it.
                    if (alert != null && subject.IsWorking)
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
        private async Task<IReadOnlyList<Watched>> WatchedAsync(DateTimeOffset now, DateTimeOffset watchingSince, IReadOnlyDictionary<string, ReplicationAlert> open)
        {
            List<Watched> watched = new List<Watched>();
            DateTimeOffset longAgo = now - _options.AlertAfter;

            foreach (Replica replica in await _replicaRepository.GetAllAsync())
            {
                watched.Add(await WatchReplicaAsync(replica, longAgo, watchingSince));
            }

            if (await _sourceRepository.GetAsync() is ReplicationSource source)
            {
                watched.Add(WatchPrimary(source, longAgo, watchingSince, open.GetValueOrDefault(ReplicationAlert.Primary)));
            }

            return watched;
        }

        /// <summary>
        /// A replica as its primary sees it. Its problem: silent for longer than the threshold, counted
        /// from its last request or from <paramref name="watchingSince"/>, whichever is later; or following
        /// the log but behind a change made before <paramref name="longAgo"/>. How far it had applied is as
        /// of its last request for changes, so a replica not seen since this server started, or for long,
        /// is never also counted behind, nor is one taking a whole copy, which brings it every change made
        /// before the copy began. Working once it has been seen since this server started without one.
        /// </summary>
        private async Task<Watched> WatchReplicaAsync(Replica replica, DateTimeOffset longAgo, DateTimeOffset watchingSince)
        {
            string subject = ReplicationAlert.ForReplica(replica.Id);
            string concerns = $"replica '{replica.Name}'";
            DateTimeOffset seenAt = replica.LastSeenAt ?? replica.PairedAt;
            if (CountedFrom(seenAt, watchingSince) < longAgo)
            {
                return Watched.Failing(subject, concerns, new Problem($"{Capitalized(concerns)} has not been in touch with this server since {Format(seenAt)}.", seenAt));
            }

            if (seenAt < watchingSince)
            {
                return Watched.Undecided(subject, concerns);
            }

            Problem? lag = replica.Copying ? null : await LagAsync(replica, concerns, longAgo);
            return lag == null ? Watched.Working(subject, concerns) : Watched.Failing(subject, concerns, lag);
        }

        /// <summary>A replica's problem when it is behind a change made before <paramref name="longAgo"/>.</summary>
        private async Task<Problem?> LagAsync(Replica replica, string concerns, DateTimeOffset longAgo)
        {
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
        /// The primary as a replica sees it. Its problem: every step has failed for longer than the
        /// threshold, counted from the first failure or from <paramref name="watchingSince"/>, whichever
        /// is later. A whole copy taking long without failing is not one. Working once a step has worked
        /// since the failing <paramref name="openAlert"/> is about began, even when later steps failed
        /// again before this check: that is a new run of failures, its own problem once long enough.
        /// </summary>
        private static Watched WatchPrimary(ReplicationSource source, DateTimeOffset longAgo, DateTimeOffset watchingSince, ReplicationAlert? openAlert)
        {
            string concerns = $"the primary {source.PrimaryUrl}";
            if (openAlert != null && source.LastSyncedAt > openAlert.Since)
            {
                return Watched.Working(ReplicationAlert.Primary, concerns);
            }

            if (source.FailingSince is not DateTimeOffset failingSince)
            {
                return Watched.Working(ReplicationAlert.Primary, concerns);
            }

            if (CountedFrom(failingSince, watchingSince) >= longAgo)
            {
                return Watched.Undecided(ReplicationAlert.Primary, concerns);
            }

            return Watched.Failing(
                ReplicationAlert.Primary,
                concerns,
                new Problem($"This server has been failing to follow {concerns} since {Format(failingSince)}. The last attempt failed: {source.LastError}", failingSince));
        }

        /// <summary>
        /// When silence or failing that began at <paramref name="began"/> counts from: then, or when this
        /// server started watching, whichever is later.
        /// </summary>
        private static DateTimeOffset CountedFrom(DateTimeOffset began, DateTimeOffset watchingSince)
        {
            return began > watchingSince ? began : watchingSince;
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

        /// <summary>
        /// Something this server watches, by its alert's subject, and what it concerns in an email's words.
        /// It stands one of three ways: failing, with a problem long enough to count; working, which clears
        /// an open problem; or undecided, neither.
        /// </summary>
        private sealed class Watched
        {
            private Watched(string subject, string concerns, Problem? problem, bool isWorking)
            {
                Subject = subject;
                Concerns = concerns;
                Problem = problem;
                IsWorking = isWorking;
            }

            public string Subject { get; }

            public string Concerns { get; }

            /// <summary>Its problem, when it is failing.</summary>
            public Problem? Problem { get; }

            /// <summary>Whether replication is known to work for it.</summary>
            public bool IsWorking { get; }

            public static Watched Failing(string subject, string concerns, Problem problem)
            {
                return new Watched(subject, concerns, problem, isWorking: false);
            }

            public static Watched Working(string subject, string concerns)
            {
                return new Watched(subject, concerns, problem: null, isWorking: true);
            }

            /// <summary>A problem too short to count, or nothing heard since this server started.</summary>
            public static Watched Undecided(string subject, string concerns)
            {
                return new Watched(subject, concerns, problem: null, isWorking: false);
            }
        }

        /// <summary>What is wrong, as a sentence, and since when.</summary>
        private sealed record Problem(string Description, DateTimeOffset Since);
    }
}
