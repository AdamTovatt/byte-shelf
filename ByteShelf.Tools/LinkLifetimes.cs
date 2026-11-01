using System.Globalization;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>How long a shared link works, written as a person or an agent would: 30m, 12h, 7d.</summary>
    internal static class LinkLifetimes
    {
        public const string Examples = "minutes, hours or days such as 30m, 12h or 7d";

        /// <summary>The lifetime <paramref name="text"/> names, or null for none (the server's default).</summary>
        /// <exception cref="UsageException">It is not a lifetime, or one <see cref="DownloadLink.NormalizeLifetime"/> refuses.</exception>
        public static TimeSpan? Parse(string? text)
        {
            if (text == null)
            {
                return null;
            }

            string trimmed = text.Trim();
            if (trimmed.Length >= 2
                && int.TryParse(trimmed.AsSpan(0, trimmed.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                && count <= DownloadLink.MaxLifetime.TotalMinutes)
            {
                TimeSpan? lifetime = char.ToLowerInvariant(trimmed[^1]) switch
                {
                    'm' => TimeSpan.FromMinutes(count),
                    'h' => TimeSpan.FromHours(count),
                    'd' => TimeSpan.FromDays(count),
                    _ => null,
                };

                if (lifetime != null)
                {
                    try
                    {
                        return DownloadLink.NormalizeLifetime(lifetime);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new UsageException(
                            string.Create(CultureInfo.InvariantCulture, $"'{text}' is not how long a link may work: from {DownloadLink.MinLifetime.TotalMinutes} minute to {DownloadLink.MaxLifetime.TotalDays} days."),
                            exception);
                    }
                }
            }

            throw new UsageException($"'{text}' is not a lifetime: give {Examples}.");
        }
    }
}
