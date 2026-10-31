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
        private readonly DownloadLinks _downloadLinks;
        private readonly IShelfService _shelfService;
        private readonly IFileService _fileService;

        public DownloadsController(DownloadLinks downloadLinks, IShelfService shelfService, IFileService fileService)
        {
            _downloadLinks = downloadLinks;
            _shelfService = shelfService;
            _fileService = fileService;
        }

        /// <summary>Streams the file the link names, as <see cref="FileContentResponse"/> describes; 404 once it has run out.</summary>
        [HttpGet("{token}")]
        [HttpHead("{token}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(206)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Get(string token, [FromQuery] bool download = false)
        {
            if (!_downloadLinks.TryRead(token, out Guid shelfId, out Guid fileId))
            {
                throw new NotFoundException("This download link is not valid, or has run out.");
            }

            Shelf shelf = await _shelfService.GetByIdAsync(shelfId);
            FileContent content = await _fileService.GetContentAsync(shelf, EntryReference.ForId(fileId));
            return FileContentResponse.Send(this, content, download);
        }
    }
}
