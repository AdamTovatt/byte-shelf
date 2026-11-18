using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <inheritdoc />
    public class LinkService : ILinkService
    {
        private readonly ILinkRepository _linkRepository;
        private readonly IFileRepository _fileRepository;
        private readonly ShelfTree _shelfTree;
        private readonly TimeProvider _timeProvider;

        public LinkService(ILinkRepository linkRepository, IFileRepository fileRepository, ShelfTree shelfTree, TimeProvider timeProvider)
        {
            _linkRepository = linkRepository;
            _fileRepository = fileRepository;
            _shelfTree = shelfTree;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfLink>> ListAsync(ShelfAccess access)
        {
            IReadOnlyList<ListedLinkRecord> listed = await _linkRepository.GetLiveForShelfAsync(access.Shelf.Id, VisibleMaker(access), _timeProvider.GetUtcNow());

            List<ShelfLink> links = new List<ShelfLink>(listed.Count);
            foreach (ListedLinkRecord each in listed)
            {
                links.Add(await ToShelfLinkAsync(each, access.Caller.Reference));
            }

            return links;
        }

        /// <inheritdoc />
        public async Task RevokeAsync(ShelfAccess access, Guid linkId)
        {
            if (!await _linkRepository.DeleteAsync(access.Shelf.Id, linkId, VisibleMaker(access)))
            {
                throw new NotFoundException($"There is no link {linkId:D} on the shelf '{access.Shelf.Name}' that you may revoke.");
            }
        }

        /// <summary>Whose links the caller sees: everyone's (null) when it may write on the shelf, else its own. The one place the rule lives.</summary>
        private static ShelfCallerReference? VisibleMaker(ShelfAccess access)
        {
            return access.Allows(ShelfOperation.Write) ? null : access.Caller.Reference;
        }

        private async Task<ShelfLink> ToShelfLinkAsync(ListedLinkRecord listed, ShelfCallerReference caller)
        {
            LinkRecord link = listed.Link;

            // What tells the kinds apart: a download link's file as it is now, or an upload link's place.
            (string? path, Guid? fileId, bool overwrite, string? description) = link.Target switch
            {
                DownloadLinkTarget download => (await LivePathAsync(download.FileId), download.FileId, false, (string?)null),
                UploadLinkTarget upload => (upload.Path, (Guid?)null, upload.Overwrite, upload.Description),
                _ => throw new InvalidOperationException($"No listing is defined for a {link.Target.Kind} link."),
            };

            return new ShelfLink
            {
                Id = link.Id,
                Kind = link.Target.Kind,
                Path = path,
                FileId = fileId,
                Overwrite = overwrite,
                Description = description,
                CreatedBy = new LinkMaker
                {
                    Kind = link.CreatedBy.Kind == ShelfCallerKind.Key ? LinkMakerKind.Key : LinkMakerKind.User,
                    Id = link.CreatedBy.Id,
                    Name = listed.MakerName,
                    IsCaller = link.CreatedBy == caller,
                },
                CreatedAt = link.CreatedAt,
                ExpiresAt = link.ExpiresAt,
            };
        }

        /// <summary>The file's path while it is live; null while it is in the trash, where its link does not reach it.</summary>
        private async Task<string?> LivePathAsync(Guid fileId)
        {
            FileRecord? file = await _fileRepository.GetByIdAsync(fileId);
            return file is { DeletedAt: null } ? (await _shelfTree.ToShelfFileAsync(file)).Path : null;
        }
    }
}
