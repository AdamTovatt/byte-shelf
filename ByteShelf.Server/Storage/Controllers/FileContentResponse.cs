using ByteShelf.Server.Storage.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// How a file's contents are sent, whichever route asked for them. <c>Range</c> is supported, and
    /// the ETag is the contents' SHA-256, so <c>If-None-Match</c> and <c>If-Range</c> work as well.
    /// Whatever the stored type, the browser is told not to sniff it, and everything but a PDF is
    /// sandboxed so it runs nothing, so an uploaded page or image cannot act with this server's origin,
    /// even when a cookie another subdomain planted chose whose upload it is. No shared cache keeps it.
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
            controller.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

            // Per caller (a session, a key, or a link's holder): a shared cache must never hand it to another.
            controller.Response.Headers[HeaderNames.CacheControl] = "private";
            if (!IsPdf(content.File.ContentType))
            {
                controller.Response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox; default-src 'none'";
            }

            // The result disposes the stream once it is sent.
            return controller.File(
                content.Contents,
                content.File.ContentType,
                download ? content.File.Name : null,
                content.File.UpdatedAt,
                new EntityTagHeaderValue($"\"{content.File.Sha256}\""),
                enableRangeProcessing: true);
        }

        private static bool IsPdf(string contentType)
        {
            return MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed)
                && string.Equals(parsed.MediaType.Value, PdfMediaType, StringComparison.OrdinalIgnoreCase);
        }
    }
}
