namespace ByteShelf.Server.Storage.Models
{
    /// <summary>An upload in progress; its bytes are in <c>staging/&lt;id&gt;</c>.</summary>
    public class UploadRecord
    {
        public required Guid Id { get; init; }
        public required Guid ShelfId { get; init; }

        /// <summary>Who started the upload, as the caller's id (<c>user:&lt;id&gt;</c> or <c>key:&lt;id&gt;</c>).</summary>
        public required string StartedBy { get; init; }

        /// <summary>The normalized path the file will be stored at.</summary>
        public required string Path { get; init; }

        public required string ContentType { get; init; }
        public required long SizeBytes { get; init; }
        public required long ReceivedBytes { get; init; }
        public required bool Overwrite { get; init; }

        /// <summary>The description the file is stored with, as <see cref="ByteShelfCommon.FileDescription.Normalize"/> left it.</summary>
        public string? Description { get; init; }

        /// <summary>Whether the bytes are an age file encrypted with a password, as the file will be stored.</summary>
        public bool Encrypted { get; init; }

        /// <summary>False for a single PUT, whose upload cannot outlive the request that made it.</summary>
        public required bool IsResumable { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset LastActivityAt { get; init; }

        public bool IsComplete => ReceivedBytes == SizeBytes;
    }
}
