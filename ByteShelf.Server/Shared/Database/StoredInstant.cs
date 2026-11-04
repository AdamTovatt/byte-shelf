using System.Globalization;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>
    /// How every timestamp column holds an instant: UTC, to the tick, as fixed-width ISO-8601 text
    /// ending in <c>Z</c>. One form for every writer means a text comparison in SQL orders values the
    /// way the instants are ordered. No column has a SQL default: every value comes from the injected
    /// <see cref="TimeProvider"/>.
    /// </summary>
    public static class StoredInstant
    {
        public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

        public static string ToText(DateTimeOffset instant)
        {
            return instant.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);
        }

        public static string ToText(DateTime instant)
        {
            DateTime utc = instant.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(instant, DateTimeKind.Utc)
                : instant.ToUniversalTime();
            return utc.ToString(Format, CultureInfo.InvariantCulture);
        }

        public static DateTimeOffset Parse(string text)
        {
            return DateTimeOffset.ParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }
    }
}
