using System.Net.Http.Headers;

namespace ByteShelfCommon
{
    /// <summary>The media type stored with a file.</summary>
    public static class ContentTypes
    {
        /// <summary>The type of a file stored without one.</summary>
        public const string Default = "application/octet-stream";

        private const int MaxLength = 255;

        /// <summary>Common types by file extension, so a file stored without a type still opens rightly in a browser.</summary>
        private static readonly Dictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".zip"] = "application/zip",
            [".gz"] = "application/gzip",
            [".tar"] = "application/x-tar",
            [".txt"] = "text/plain",
            [".md"] = "text/markdown",
            [".csv"] = "text/csv",
            [".html"] = "text/html",
            [".css"] = "text/css",
            [".js"] = "text/javascript",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".heic"] = "image/heic",
            [".mp3"] = "audio/mpeg",
            [".wav"] = "audio/wav",
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
        };

        /// <summary>The type of a common file by its name's extension, or null when it is not one of them.</summary>
        public static string? ForFileName(string fileName)
        {
            return ByExtension.GetValueOrDefault(Path.GetExtension(fileName));
        }

        /// <summary>A stored type as it is compared: lowercase, without parameters such as <c>; charset=utf-8</c>.</summary>
        public static string MediaTypeOf(string contentType)
        {
            int parameters = contentType.IndexOf(';', StringComparison.Ordinal);
            return (parameters < 0 ? contentType : contentType[..parameters]).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// The type to store a file at <paramref name="path"/> with: <paramref name="contentType"/>, or
        /// when none is given the type its extension names (<see cref="ForFileName"/>), or else
        /// <see cref="Default"/>. Every upload is typed by this, on the server and in every client.
        /// </summary>
        /// <exception cref="ArgumentException">The value given is not a media type.</exception>
        public static string ForUpload(string? contentType, string path)
        {
            return Normalize(string.IsNullOrWhiteSpace(contentType) ? ForFileName(path) : contentType);
        }

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
