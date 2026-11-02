using System.Globalization;

namespace ByteShelfCommon
{
    /// <summary>
    /// A link to one file's contents that works without signing in until it runs out: for a browser to
    /// open or save, or to share with someone who has no access to the shelf. It names that file, so a
    /// file stored over it later is a new file it does not reach; while the file is in the trash the
    /// link answers not found, and once the file is restored it works again until it runs out.
    /// </summary>
    public sealed class DownloadLink
    {
        /// <summary>How long a link works when its maker does not say: long enough to pass on to someone.</summary>
        public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

        /// <summary>The shortest lifetime a link may be made with.</summary>
        public static readonly TimeSpan MinLifetime = TimeSpan.FromMinutes(1);

        /// <summary>The longest lifetime a link may be made with. Nothing takes a link back before it runs out.</summary>
        public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(7);

        /// <summary>
        /// The link. The server answers it on its public URL when it has one configured, and otherwise
        /// relative to its own root; <c>ByteShelfHttpClient</c> makes a relative one absolute on the
        /// address it reached the server at. Add <c>?download=true</c> to have a browser save the file
        /// rather than show it.
        /// </summary>
        public required string Url { get; init; }

        /// <summary>When the link stops working.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }

        /// <summary>The lifetime to make a link with: <see cref="DefaultLifetime"/> when none is given.</summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="lifetime"/> is not whole minutes, or is outside <see cref="MinLifetime"/> to <see cref="MaxLifetime"/>.
        /// </exception>
        public static TimeSpan NormalizeLifetime(TimeSpan? lifetime)
        {
            if (lifetime == null)
            {
                return DefaultLifetime;
            }

            if (lifetime.Value < MinLifetime || lifetime.Value > MaxLifetime || lifetime.Value.Ticks % TimeSpan.TicksPerMinute != 0)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"A link's lifetime must be whole minutes, from {MinLifetime.TotalMinutes} minute to {MaxLifetime.TotalDays} days."),
                    nameof(lifetime));
            }

            return lifetime.Value;
        }
    }
}
