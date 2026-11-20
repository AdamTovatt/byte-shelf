using System.Diagnostics.CodeAnalysis;

namespace ByteShelfCommon
{
    /// <summary>The rule for a file's SHA-256 named in a request: 64 hex digits, in either case.</summary>
    public static class ContentHash
    {
        /// <summary>The SHA-256 as the server stores and answers it: trimmed and lowercase.</summary>
        /// <exception cref="ArgumentException">It is not 64 hex digits.</exception>
        public static string Normalize(string sha256)
        {
            return TryNormalize(sha256, out string? normalized)
                ? normalized
                : throw new ArgumentException($"'{sha256}' is not a SHA-256: 64 hex digits.", nameof(sha256));
        }

        /// <summary>The SHA-256 as <see cref="Normalize"/> answers it, or false when it is not 64 hex digits.</summary>
        public static bool TryNormalize(string sha256, [NotNullWhen(true)] out string? normalized)
        {
            ArgumentNullException.ThrowIfNull(sha256);

            string trimmed = sha256.Trim().ToLowerInvariant();
            bool valid = trimmed.Length == 64 && trimmed.All(char.IsAsciiHexDigit);
            normalized = valid ? trimmed : null;
            return valid;
        }
    }
}
