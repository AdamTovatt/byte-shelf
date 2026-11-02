using System.Globalization;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>How long a link works, written as a person or an agent would: 30m, 12h, 7d.</summary>
    internal static class LinkLifetimes
    {
        public const string Examples = "minutes, hours or days such as 30m, 12h or 7d";

        // Far past any link's lifetime, and small enough that a count of days never overflows a TimeSpan;
        // anything up to it is a lifetime, and the rule then says whether a link may have it.
        private const int MaxCount = 1_000_000;

        /// <summary>The download link lifetime <paramref name="text"/> names, or null for none (the server's default).</summary>
        /// <exception cref="UsageException">It is not a lifetime, or one <see cref="DownloadLink.Lifetime"/> refuses.</exception>
        public static TimeSpan? ForDownload(string? text)
        {
            return Parse(text, DownloadLink.Lifetime);
        }

        /// <summary>The upload link lifetime <paramref name="text"/> names, or null for none (the server's default).</summary>
        /// <exception cref="UsageException">It is not a lifetime, or one <see cref="UploadLink.Lifetime"/> refuses.</exception>
        public static TimeSpan? ForUpload(string? text)
        {
            return Parse(text, UploadLink.Lifetime);
        }

        private static TimeSpan? Parse(string? text, LinkLifetime rule)
        {
            if (text == null)
            {
                return null;
            }

            string trimmed = text.Trim();
            if (trimmed.Length >= 2
                && int.TryParse(trimmed.AsSpan(0, trimmed.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                && count <= MaxCount)
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
                        return rule.Normalize(lifetime);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new UsageException($"'{text}' is not how long a link may work: {rule.Bounds}.", exception);
                    }
                }
            }

            throw new UsageException($"'{text}' is not a lifetime: give {Examples}.");
        }
    }
}
