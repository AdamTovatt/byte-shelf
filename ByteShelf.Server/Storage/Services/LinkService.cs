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
        private readonly TimeProvider _timeProvider;

        public LinkService(ILinkRepository linkRepository, TimeProvider timeProvider)
        {
            _linkRepository = linkRepository;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfLink>> ListAsync(ShelfAccess access)
        {
            IReadOnlyList<ListedLinkRecord> listed = await _linkRepository.GetLiveForShelfAsync(access.Shelf.Id, VisibleMaker(access), _timeProvider.GetUtcNow());
            return listed.Select(each => ToShelfLink(each, access.Caller.Reference)).ToList();
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

        private static ShelfLink ToShelfLink(ListedLinkRecord listed, ShelfCallerReference caller)
        {
            LinkRecord link = listed.Link;

            // What tells the kinds apart: a download link's file as it is now (no path while it is in the
            // trash, where its link does not reach it), or an upload link's place.
            (string? path, Guid? fileId, bool overwrite, string? description) = link.Target switch
            {
                DownloadLinkTarget download => (listed.LiveFilePath, download.FileId, false, (string?)null),
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
    }
}
