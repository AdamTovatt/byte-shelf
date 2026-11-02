namespace ByteShelf.Server.Storage.Models
{
    /// <summary>An upload in progress; its bytes are in <c>staging/&lt;id&gt;</c>.</summary>
    public class UploadRecord
    {
        public required Guid Id { get; init; }
        public required Guid ShelfId { get; init; }

        /// <summary>Who started the upload, as the caller's id (<c>user:&lt;id&gt;</c> or <c>key:&lt;id&gt;</c>).</summary>
        public required string StartedBy { get; init; }

        /// <summary>Where the bytes go once the upload finishes.</summary>
        public required UploadTarget Target { get; init; }

        public required string ContentType { get; init; }
        public required long SizeBytes { get; init; }
        public required long ReceivedBytes { get; init; }

        /// <summary>Whether the bytes are an age file encrypted with a password, as the file will be stored.</summary>
        public required bool Encrypted { get; init; }

        /// <summary>False for a single PUT, whose upload cannot outlive the request that made it.</summary>
        public required bool IsResumable { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset LastActivityAt { get; init; }

        public bool IsComplete => ReceivedBytes == SizeBytes;
    }

    /// <summary>Where an upload's bytes go: a new file at a path, or an existing file's contents swapped in place.</summary>
    public abstract record UploadTarget;

    /// <summary>
    /// A new file at <paramref name="Path"/> (normalized), replacing a file there only when
    /// <paramref name="Overwrite"/>, described as <paramref name="Description"/>
    /// (<see cref="ByteShelfCommon.FileDescription.Normalize"/>d) or, when null, as the file it replaces.
    /// </summary>
    public sealed record StoreAtPath(string Path, bool Overwrite, string? Description) : UploadTarget;

    /// <summary>
    /// File <paramref name="FileId"/>'s contents, swapped in place so it keeps its id, while they are
    /// still <paramref name="Sha256"/>: a client encrypting or decrypting it.
    /// </summary>
    public sealed record ContentSwap(Guid FileId, string Sha256) : UploadTarget;
}
