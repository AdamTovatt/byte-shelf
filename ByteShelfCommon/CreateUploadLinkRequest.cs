namespace ByteShelfCommon
{
    /// <summary>Asks for an <see cref="UploadLink"/>.</summary>
    public sealed class CreateUploadLinkRequest
    {
        /// <summary>Where the file sent to the link is stored. Missing parent directories are created then.</summary>
        public required string Path { get; init; }

        /// <summary>Whether a file already at <see cref="Path"/> is replaced (it goes to the trash). Needs write access.</summary>
        public bool Overwrite { get; init; }

        /// <summary>The stored file's description, at most <see cref="FileDescription.MaxLength"/> characters.</summary>
        public string? Description { get; init; }

        /// <summary>How long the link works, in minutes, within <see cref="UploadLink.Lifetime"/>: its default when left out.</summary>
        public int? ExpiresInMinutes { get; init; }

        /// <summary>The request with the rules applied (<see cref="NormalizedUploadLinkRequest.Create"/>).</summary>
        /// <exception cref="ArgumentException">A value the rules refuse.</exception>
        public NormalizedUploadLinkRequest Normalize()
        {
            return NormalizedUploadLinkRequest.Create(Path, Overwrite, Description, ExpiresInMinutes is int minutes ? TimeSpan.FromMinutes(minutes) : null);
        }
    }
}
