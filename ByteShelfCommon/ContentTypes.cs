using System.Net.Http.Headers;

namespace ByteShelfCommon
{
    /// <summary>The media type stored with a file.</summary>
    public static class ContentTypes
    {
        /// <summary>The type of a file stored without one.</summary>
        public const string Default = "application/octet-stream";

        private const int MaxLength = 255;

        /// <summary>The type to store: <see cref="Default"/> when none is given.</summary>
        /// <exception cref="ArgumentException">The value is not a media type.</exception>
        public static string Normalize(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return Default;
            }

            string trimmed = contentType.Trim();
            if (trimmed.Length > MaxLength || !MediaTypeHeaderValue.TryParse(trimmed, out _))
            {
                throw new ArgumentException($"'{contentType}' is not a media type.", nameof(contentType));
            }

            return trimmed;
        }
    }
}
