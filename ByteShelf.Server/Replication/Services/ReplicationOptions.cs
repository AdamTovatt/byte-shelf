using ByteShelf.Server.Shared;
using EasyReasy.EnvironmentVariables;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>Where replicas reach this server, and the limits replication works within.</summary>
    public sealed class ReplicationOptions
    {
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

        /// <summary>How long replication may go wrong before the admins are emailed (<see cref="ReplicationAlerts"/>).</summary>
        public TimeSpan AlertAfter { get; init; } = DefaultAlertAfter;

        /// <summary><see cref="AlertAfter"/> when <c>BYTESHELF_REPLICATION_ALERT_MINUTES</c> is not set.</summary>
        public static readonly TimeSpan DefaultAlertAfter = TimeSpan.FromMinutes(60);

        /// <summary>The most minutes <c>BYTESHELF_REPLICATION_ALERT_MINUTES</c> may hold: 30 days.</summary>
        public const int MaxAlertAfterMinutes = 30 * 24 * 60;

        /// <summary>The options for a server at <paramref name="publicUrl"/>, <see cref="AlertAfter"/> read from the environment.</summary>
        /// <exception cref="InvalidOperationException"><c>BYTESHELF_REPLICATION_ALERT_MINUTES</c> is not a whole number of minutes from 1 to <see cref="MaxAlertAfterMinutes"/>.</exception>
        public static ReplicationOptions FromEnvironment(string publicUrl)
        {
            return new ReplicationOptions { PublicUrl = publicUrl, AlertAfter = AlertAfterFrom(EnvironmentVariables.ReplicationAlertMinutes.GetValueOrDefault()) };
        }

        /// <summary><see cref="AlertAfter"/> as <c>BYTESHELF_REPLICATION_ALERT_MINUTES</c> gives it: null (unset or blank) for the default.</summary>
        /// <exception cref="InvalidOperationException">It is not a whole number of minutes from 1 to <see cref="MaxAlertAfterMinutes"/>.</exception>
        public static TimeSpan AlertAfterFrom(string? minutes)
        {
            int? parsed = WholeNumberSetting.Parse(EnvironmentVariables.ReplicationAlertMinutes, minutes, "minutes", MaxAlertAfterMinutes);
            return parsed is int value ? TimeSpan.FromMinutes(value) : DefaultAlertAfter;
        }
    }
}
