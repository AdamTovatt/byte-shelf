using Microsoft.AspNetCore.StaticFiles;

namespace ByteShelf.Server.Shared.StaticFiles
{
    /// <summary>
    /// Cache-Control policy for the served static frontend bundle. Vite emits content-addressed
    /// hashes for everything under <c>/assets/</c>, so those bundles are immutable for the
    /// lifetime of the deployment and can be cached indefinitely. The entry document
    /// (<c>index.html</c>) and the other root-level static files (<c>manifest.json</c>, icons)
    /// keep stable URLs across releases, so they must revalidate on every request — iOS Safari's
    /// heuristic caching otherwise pins a stale <c>index.html</c> that still references
    /// prior-release asset hashes, and the version-poll reload banner can't recover from it.
    /// </summary>
    public static class StaticFileCachePolicy
    {
        public static void Apply(StaticFileResponseContext context)
        {
            if (context.Context.Request.Path.StartsWithSegments("/assets"))
            {
                context.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
            }
            else
            {
                ApplyNoCache(context.Context.Response);
            }
        }

        /// <summary>The web UI's entry document under <paramref name="webRootPath"/>; null when no web UI has been published there.</summary>
        public static string? FindIndexHtml(string? webRootPath)
        {
            string? indexPath = string.IsNullOrEmpty(webRootPath) ? null : Path.Combine(webRootPath, "index.html");
            return indexPath != null && File.Exists(indexPath) ? indexPath : null;
        }

        /// <summary>
        /// Applies the no-cache headers used for the SPA entry document.
        /// </summary>
        public static void ApplyNoCache(HttpResponse response)
        {
            response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Expires"] = "0";
        }

        /// <summary>
        /// Registers a fallback that serves <c>index.html</c> for an unmatched GET or HEAD outside
        /// <c>/api</c>, with <see cref="ApplyNoCache"/> applied, so a deep link into the web UI loads
        /// it. Anything else unmatched, an API typo above all, gets a 404 rather than the page.
        /// Replaces <c>MapFallbackToFile</c>, which would bypass the <see cref="Apply"/> callback
        /// registered on <c>UseStaticFiles</c> and serve the shell with no cache headers.
        /// </summary>
        public static void MapSpaFallback(WebApplication app)
        {
            app.MapFallback(async context =>
            {
                bool isPageRequest = (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                    && !context.Request.Path.StartsWithSegments("/api");

                // Resolved per request: WebRootPath is null when no web UI has been published.
                string? indexPath = FindIndexHtml(app.Environment.WebRootPath);

                if (!isPageRequest || indexPath == null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                ApplyNoCache(context.Response);
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(indexPath);
            });
        }
    }
}
