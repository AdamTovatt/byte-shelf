namespace ByteShelfCommon
{
    /// <summary>
    /// What an upload link is made for, the rules applied once: a file's normalized path, its
    /// normalized description, and the lifetime, its default filled in. Only <see cref="Create"/> makes one.
    /// </summary>
    public sealed record NormalizedUploadLinkRequest
    {
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
        /// <exception cref="ArgumentException">The path, description or lifetime is one the rules refuse, or the path is the root.</exception>
        public static NormalizedUploadLinkRequest Create(string path, bool overwrite, string? description, TimeSpan? lifetime)
        {
            string normalizedPath = ShelfPath.Normalize(path);
            if (normalizedPath == ShelfPath.Root)
            {
                throw new ArgumentException("An upload link stores one file, so it needs a file's path, not the root.", nameof(path));
            }

            string? normalizedDescription = FileDescription.Normalize(description);
            TimeSpan normalizedLifetime = UploadLink.Lifetime.Normalize(lifetime);

            return new NormalizedUploadLinkRequest(normalizedPath, overwrite, normalizedDescription, normalizedLifetime);
        }

        /// <summary>The request to send for it.</summary>
        public CreateUploadLinkRequest ToRequest()
        {
            return new CreateUploadLinkRequest { Path = Path, Overwrite = Overwrite, Description = Description, ExpiresInMinutes = (int)Lifetime.TotalMinutes };
        }
    }
}
