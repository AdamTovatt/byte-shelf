using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Storage.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ByteShelf.Server.Derived.Controllers
{
    /// <summary>
    /// How a derived file is sent: as contents are, with an ETag of everything that names its bytes. A browser may
    /// show it again for an hour without asking, so a listing's thumbnails load once.
    /// </summary>
    internal static class DerivedFileResponse
    {
        public static IActionResult Send(ControllerBase controller, DerivedFile derived)
        {
            FileContentResponse.SetHeaders(controller.Response, derived.MediaType, "private, max-age=3600");

            // The result disposes the stream once it is sent.
            return controller.File(
                derived.Contents,
                derived.MediaType,
                lastModified: null,
                new EntityTagHeaderValue($"\"{DerivedKinds.NameOf(derived.Kind)}-{derived.Sha256}-{derived.Tag}\""),
                enableRangeProcessing: true);
        }
    }
}
