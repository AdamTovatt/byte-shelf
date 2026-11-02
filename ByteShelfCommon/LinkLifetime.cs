using System.Globalization;

namespace ByteShelfCommon
{
    /// <summary>
    /// How long one kind of link may work: <see cref="DownloadLink.Lifetime"/> and
    /// <see cref="UploadLink.Lifetime"/>. Whole minutes, from <see cref="Min"/> to <see cref="Max"/>.
    /// </summary>
    public sealed class LinkLifetime
    {
        internal LinkLifetime(TimeSpan @default, TimeSpan max)
        {
            Default = @default;
            Max = max;
        }

        /// <summary>How long a link works when its maker does not say.</summary>
        public TimeSpan Default { get; }

        /// <summary>The shortest lifetime a link may be made with, the same for every kind.</summary>
        public TimeSpan Min { get; } = TimeSpan.FromMinutes(1);

        /// <summary>The longest lifetime a link may be made with. Nothing takes a link back before it runs out.</summary>
        public TimeSpan Max { get; }

        /// <summary>The bounds as a sentence ends them: "from 1 minute to 7 days".</summary>
        public string Bounds => string.Create(CultureInfo.InvariantCulture, $"from {Min.TotalMinutes} minute to {Describe(Max)}");

        /// <summary>The lifetime to make a link with: <see cref="Default"/> when none is given.</summary>
        /// <exception cref="ArgumentException"><paramref name="lifetime"/> is not whole minutes, or is outside the bounds.</exception>
        public TimeSpan Normalize(TimeSpan? lifetime)
        {
            if (lifetime == null)
            {
                return Default;
            }

            if (lifetime.Value < Min || lifetime.Value > Max || lifetime.Value.Ticks % TimeSpan.TicksPerMinute != 0)
            {
                throw new ArgumentException($"A link's lifetime must be whole minutes, {Bounds}.", nameof(lifetime));
            }

            return lifetime.Value;
        }

        private static string Describe(TimeSpan span)
        {
            return span.TotalDays > 1 && span.Ticks % TimeSpan.TicksPerDay == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{span.TotalDays} days")
                : string.Create(CultureInfo.InvariantCulture, $"{span.TotalHours} hours");
        }
    }
}
