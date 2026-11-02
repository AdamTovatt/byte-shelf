using System.Globalization;
using System.Text;

namespace ByteShelfCommon
{
    /// <summary>
    /// What an upload link is made for, the rules applied once: a file's normalized path, its
    /// normalized description, and the lifetime, its default filled in. Only <see cref="Create"/> makes one.
    /// </summary>
    public sealed record NormalizedUploadLinkRequest
    {
        /// <summary>
        /// How many bytes of UTF-8 the path and the description may take together. A link carries both
        /// in its URL, and this keeps the whole URL under the 8 KiB request line common proxies accept.
        /// </summary>
        public const int MaxCarriedBytes = 4096;

        private NormalizedUploadLinkRequest(string path, bool overwrite, string? description, TimeSpan lifetime)
        {
            Path = path;
            Overwrite = overwrite;
            Description = description;
            Lifetime = lifetime;
        }

        /// <summary>The file's path, normalized; never the root.</summary>
        public string Path { get; }

        /// <summary>Whether a file already at <see cref="Path"/> is replaced.</summary>
        public bool Overwrite { get; }

        /// <summary>The stored file's description, normalized; null for none.</summary>
        public string? Description { get; }

        /// <summary>How long the link works, within <see cref="UploadLink.Lifetime"/>.</summary>
        public TimeSpan Lifetime { get; }

        /// <summary>The request with its path and description normalized and its lifetime within <see cref="UploadLink.Lifetime"/>.</summary>
        /// <exception cref="ArgumentException">
        /// The path, description or lifetime is one the rules refuse, the path is the root, or the path
        /// and description take more than <see cref="MaxCarriedBytes"/> together.
        /// </exception>
        public static NormalizedUploadLinkRequest Create(string path, bool overwrite, string? description, TimeSpan? lifetime)
        {
            string normalizedPath = ShelfPath.Normalize(path);
            if (normalizedPath == ShelfPath.Root)
            {
                throw new ArgumentException("An upload link stores one file, so it needs a file's path, not the root.", nameof(path));
            }

            string? normalizedDescription = FileDescription.Normalize(description);
            TimeSpan normalizedLifetime = UploadLink.Lifetime.Normalize(lifetime);

            int carried = Encoding.UTF8.GetByteCount(normalizedPath) + (normalizedDescription == null ? 0 : Encoding.UTF8.GetByteCount(normalizedDescription));
            if (carried > MaxCarriedBytes)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"An upload link carries its path and description in its URL, so together they take at most {MaxCarriedBytes} bytes of UTF-8; these take {carried}. Give a shorter description, or describe the file once it has arrived."),
                    nameof(description));
            }

            return new NormalizedUploadLinkRequest(normalizedPath, overwrite, normalizedDescription, normalizedLifetime);
        }

        /// <summary>The request to send for it.</summary>
        public CreateUploadLinkRequest ToRequest()
        {
            return new CreateUploadLinkRequest { Path = Path, Overwrite = Overwrite, Description = Description, ExpiresInMinutes = (int)Lifetime.TotalMinutes };
        }
    }
}
