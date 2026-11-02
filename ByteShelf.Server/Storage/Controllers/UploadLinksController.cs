using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>Stores one file by a link from <see cref="EntriesController.CreateUploadLink"/>, without a token.</summary>
    [ApiController]
    [Route(UploadLinks.Route)]
    [AllowAnonymous]
    public class UploadLinksController : ControllerBase
    {
        private const string NotValid = "This upload link is not valid, or has run out.";

        private readonly UploadLinks _uploadLinks;
        private readonly UploadLinkAccess _uploadLinkAccess;
        private readonly IUploadService _uploadService;

        public UploadLinksController(UploadLinks uploadLinks, UploadLinkAccess uploadLinkAccess, IUploadService uploadService)
        {
            _uploadLinks = uploadLinks;
            _uploadLinkAccess = uploadLinkAccess;
            _uploadService = uploadService;
        }

        /// <summary>
        /// Stores the request body at the link's path, as a single <c>PUT content</c> by the link's maker
        /// would: <c>Content-Length</c> is needed and reserved against the quota first, and the maker must
        /// still be allowed to store there. The <c>Content-Type</c> is stored, or else the type the path's
        /// extension names. 404 once the link has run out or its maker is gone or has lost access, 403
        /// when the maker may no longer store there, 409 when the path is taken and the link may not
        /// overwrite, 507 over quota.
        /// </summary>
        [HttpPut("{token}")]
        [DisableRequestSizeLimit]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 411)]
        [ProducesResponseType(typeof(ErrorResponse), 507)]
        public async Task<IActionResult> Put(string token, CancellationToken cancellationToken)
        {
            if (!_uploadLinks.TryRead(token, out UploadLinkGrant? grant))
            {
                throw new NotFoundException(NotValid);
            }

            ShelfAccess access = await _uploadLinkAccess.ResolveAsync(grant, NotValid);
            StartUploadRequest request = SingleUpload.Of(
                Request, grant.Path, grant.Overwrite, grant.Description, "An upload link needs Content-Length: send a file, as curl -T does, not a stream of unknown length.");

            return Ok(await _uploadService.StoreAsync(access.Shelf, request, access.Caller.Id, Request.Body, cancellationToken));
        }
    }
}
