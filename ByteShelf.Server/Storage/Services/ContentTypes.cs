using System.Net.Http.Headers;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>The MIME type stored with a file.</summary>
    public static class ContentTypes
    {
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
