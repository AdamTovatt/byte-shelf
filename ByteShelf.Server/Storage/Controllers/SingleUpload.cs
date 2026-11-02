using ByteShelf.Server.Shared.Errors;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>A file sent as one request's body, as <c>PUT content</c> and an upload link take it.</summary>
    internal static class SingleUpload
    {
        /// <summary>
        /// What storing <paramref name="request"/>'s body at <paramref name="path"/> asks for: its
        /// <c>Content-Length</c> as the size, reserved before the body is read, and its <c>Content-Type</c>.
        /// </summary>
        /// <param name="whenLengthMissing">What the 411 says to do instead.</param>
        /// <exception cref="LengthRequiredException">The request has no <c>Content-Length</c>.</exception>
        public static StartUploadRequest Of(HttpRequest request, string path, bool overwrite, string? description, string whenLengthMissing)
        {
            long length = request.ContentLength ?? throw new LengthRequiredException(whenLengthMissing);
            return new StartUploadRequest { Path = path, SizeBytes = length, ContentType = request.ContentType, Overwrite = overwrite, Description = description };
        }
    }
}
