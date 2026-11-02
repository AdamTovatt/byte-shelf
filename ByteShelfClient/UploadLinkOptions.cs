using ByteShelfCommon;

namespace ByteShelfClient
{
    /// <summary>How <see cref="IShelfClient.CreateUploadLinkAsync"/> makes a link.</summary>
    public sealed record UploadLinkOptions
    {
        /// <summary>Replace a file already at the path, moving it to the trash; needs write access.</summary>
        public bool Overwrite { get; init; }

        /// <summary>The stored file's description, at most <see cref="FileDescription.MaxLength"/> characters.</summary>
        public string? Description { get; init; }

        /// <summary>How long the link works, in whole minutes within <see cref="UploadLink.Lifetime"/>: its default when null.</summary>
        public TimeSpan? Lifetime { get; init; }

        /// <summary>The link these options ask for at <paramref name="path"/>, the rules applied.</summary>
        /// <exception cref="ArgumentException">A value the rules refuse.</exception>
        internal NormalizedUploadLinkRequest Normalize(string path)
        {
            return NormalizedUploadLinkRequest.Create(path, Overwrite, Description, Lifetime);
        }
    }
}
