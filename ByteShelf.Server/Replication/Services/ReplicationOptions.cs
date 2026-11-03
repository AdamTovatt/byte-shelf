using ByteShelf.Server.Shared;
using EasyReasy.EnvironmentVariables;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>Where replicas reach this server, and the limits replication works within.</summary>
    public sealed class ReplicationOptions
    {
        private readonly TimeSpan _alertAfter = DefaultAlertAfter;

        /// <summary>This server's public URL (<c>APP_BASE_URL</c>), which a pairing code made here names.</summary>
        public required string PublicUrl { get; init; }

        /// <summary>
        /// How long a call to the primary may go without anything arriving, before its answer starts or
        /// between parts of it, before it is given up: a connection that died without closing never ends.
        /// </summary>
        public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// How many entries a replica asks its primary for, and applies, at a time; the primary may give
        /// fewer (at most <see cref="IReplicationFeed.MaxPageSize"/>).
        /// </summary>
        public int PageSize { get; init; } = 500;

        /// <summary>
        /// How long replication may go wrong before the admins are emailed (<see cref="ReplicationAlerts"/>),
        /// from <see cref="MinAlertAfterMinutes"/> to <see cref="MaxAlertAfterMinutes"/> minutes.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Set outside those bounds.</exception>
        public TimeSpan AlertAfter
        {
            get => _alertAfter;
            init => _alertAfter = value >= TimeSpan.FromMinutes(MinAlertAfterMinutes) && value <= TimeSpan.FromMinutes(MaxAlertAfterMinutes)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, $"An alert threshold is from {MinAlertAfterMinutes} to {MaxAlertAfterMinutes} minutes.");
        }

        /// <summary><see cref="AlertAfter"/> when <c>BYTESHELF_REPLICATION_ALERT_MINUTES</c> is not set.</summary>
        public static readonly TimeSpan DefaultAlertAfter = TimeSpan.FromMinutes(60);

        /// <summary>
        /// The fewest minutes <see cref="AlertAfter"/> may be: twice the longest a replica waits between
        /// failed attempts (<see cref="ReplicaSyncWorker.MaxRetryDelay"/>). A replica backing off still asks
        /// well inside it, and once the primary is back its next attempt works well inside it, so backing
        /// off alone never alerts on either side.
        /// </summary>
        public static readonly int MinAlertAfterMinutes = (int)Math.Ceiling(2 * ReplicaSyncWorker.MaxRetryDelay.TotalMinutes);

        /// <summary>The most minutes <see cref="AlertAfter"/> may be: 30 days.</summary>
        public const int MaxAlertAfterMinutes = 30 * 24 * 60;

        /// <summary>The options for a server at <paramref name="publicUrl"/>, <see cref="AlertAfter"/> read from the environment.</summary>
        /// <exception cref="InvalidOperationException"><c>BYTESHELF_REPLICATION_ALERT_MINUTES</c> is not a whole number of minutes from <see cref="MinAlertAfterMinutes"/> to <see cref="MaxAlertAfterMinutes"/>.</exception>
        public static ReplicationOptions FromEnvironment(string publicUrl)
        {
            return new ReplicationOptions { PublicUrl = publicUrl, AlertAfter = AlertAfterFrom(EnvironmentVariables.ReplicationAlertMinutes.GetValueOrDefault()) };
        }

        /// <summary><see cref="AlertAfter"/> as <c>BYTESHELF_REPLICATION_ALERT_MINUTES</c> gives it: null (unset or blank) for the default.</summary>
        /// <exception cref="InvalidOperationException">It is not a whole number of minutes from <see cref="MinAlertAfterMinutes"/> to <see cref="MaxAlertAfterMinutes"/>.</exception>
        public static TimeSpan AlertAfterFrom(string? minutes)
        {
            int? parsed = WholeNumberSetting.Parse(EnvironmentVariables.ReplicationAlertMinutes, minutes, "minutes", MinAlertAfterMinutes, MaxAlertAfterMinutes);
            return parsed is int value ? TimeSpan.FromMinutes(value) : DefaultAlertAfter;
        }
    }
}
