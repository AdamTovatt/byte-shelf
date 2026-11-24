using ByteShelf.Server.Storage.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// How a file's contents are sent, whichever route asked for them, and the headers (<see cref="SetHeaders"/>)
    /// of anything made from them. <c>Range</c> is supported, and the ETag is the contents' SHA-256, so
    /// <c>If-None-Match</c> and <c>If-Range</c> work as well. Whatever the stored type, the browser is told not
    /// to sniff it, and everything but a PDF is sandboxed so it runs nothing, so an uploaded page or image cannot act with this server's origin,
    /// even when a cookie another subdomain planted chose whose upload it is. No shared cache keeps it, and
    /// a browser that does asks before using it again: encrypting or decrypting a file swaps the contents
    /// at the same address.
    /// </summary>
    internal static class FileContentResponse
    {
        /// <summary>
        /// Chrome's PDF viewer does not run in a sandboxed document, nor under <c>default-src 'none'</c>
        /// (which covers <c>object-src</c>), so a PDF is sent without the policy. That is safe only with
        /// <c>nosniff</c>: the browser then shows a file labelled PDF only as a PDF, never as a page.
        /// </summary>
        private const string PdfMediaType = "application/pdf";

        /// <param name="controller">The controller answering.</param>
        /// <param name="content">The file and where its contents are.</param>
        /// <param name="download">Whether the browser should save it under its name rather than show it.</param>
        public static IActionResult Send(ControllerBase controller, FileContent content, bool download)
        {
            // Per caller (a session, a key, or a link's holder): a shared cache must never hand it to another.
            // Unchanged contents answer 304 by their ETag, so asking again costs no download.
            SetHeaders(controller.Response, content.File.ContentType, "private, no-cache");

            // The result disposes the stream once it is sent.
            return controller.File(
                content.Contents,
                content.File.ContentType,
                download ? content.File.Name : null,
                content.File.UpdatedAt,
                new EntityTagHeaderValue($"\"{content.File.Sha256}\""),
                enableRangeProcessing: true);
        }

        /// <summary>The headers of any response with bytes read from stored contents, of the type <paramref name="contentType"/>.</summary>
        public static void SetHeaders(HttpResponse response, string contentType, string cacheControl)
        {
            response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            response.Headers[HeaderNames.CacheControl] = cacheControl;
            if (!IsPdf(contentType))
            {
                response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox; default-src 'none'";
            }
        }

        private static bool IsPdf(string contentType)
        {
            return MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed)
                && string.Equals(parsed.MediaType.Value, PdfMediaType, StringComparison.OrdinalIgnoreCase);
        }
    }
}
