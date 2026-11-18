using ByteShelf.Server.Access.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Models
{
    /// <summary>What a link reaches: a <see cref="DownloadLinkTarget"/> or an <see cref="UploadLinkTarget"/>.</summary>
    public abstract record LinkTarget
    {
        private protected LinkTarget()
        {
        }

        public abstract ShelfLinkKind Kind { get; }
    }

    /// <summary>A download link's target: one file, by id.</summary>
    public sealed record DownloadLinkTarget(Guid FileId) : LinkTarget
    {
        public override ShelfLinkKind Kind => ShelfLinkKind.Download;
    }

    /// <summary>An upload link's target: where it stores its one file, whether it replaces one there, and the description it stores.</summary>
    public sealed record UploadLinkTarget(string Path, bool Overwrite, string? Description) : LinkTarget
    {
        public override ShelfLinkKind Kind => ShelfLinkKind.Upload;
    }

    /// <summary>A <c>shelf_link</c> row: a link on a shelf, made by <see cref="CreatedBy"/>, whose access it works with.</summary>
    public sealed record LinkRecord
    {
        public required Guid Id { get; init; }
        public required Guid ShelfId { get; init; }
        public required LinkTarget Target { get; init; }
        public required ShelfCallerReference CreatedBy { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }

    /// <summary>A link as listed: its row, and the name of the user or key that made it.</summary>
    public sealed record ListedLinkRecord(LinkRecord Link, string MakerName);
}
