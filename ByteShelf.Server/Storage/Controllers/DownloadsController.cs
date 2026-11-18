using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>A file's contents by a link from <see cref="EntriesController.CreateDownloadLink"/>, without a token.</summary>
    [ApiController]
    [Route(DownloadLinks.Route)]
    [AllowAnonymous]
    public class DownloadsController : ControllerBase
    {
        private const string NotValid = "This download link is not valid, has run out or was revoked.";

        private readonly DownloadLinks _downloadLinks;
        private readonly IFileService _fileService;

        public DownloadsController(DownloadLinks downloadLinks, IFileService fileService)
        {
            _downloadLinks = downloadLinks;
            _fileService = fileService;
        }

        /// <summary>
        /// Streams the file the link names, as <see cref="FileContentResponse"/> describes, while its maker
        /// may still read the shelf. 404 once it has run out or been revoked, while its file is in the
        /// trash, or when its maker is gone or no longer has access; 409 while the file is encrypted.
        /// </summary>
        [HttpGet("{token}")]
        [HttpHead("{token}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(206)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Get(string token, [FromQuery] bool download = false)
        {
            (ShelfAccess access, DownloadLinkTarget target) = await _downloadLinks.OpenAsync(token, NotValid);
            FileContent content = await _fileService.GetContentAsync(access.Shelf, EntryReference.ForId(target.FileId));

            // The link names the file by id, which an in-place encryption keeps: refused while it is encrypted.
            if (content.File.Encrypted)
            {
                await content.DisposeAsync();
                ShelfEncryption.RequireDownloadLinkTarget(content.File);
            }

            return FileContentResponse.Send(this, content, download);
        }
    }
}
