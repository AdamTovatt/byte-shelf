namespace ByteShelf.Server.Shared
{
    /// <summary>
    /// The server's public URL (<c>APP_BASE_URL</c>), the one place it is read and the links on it are
    /// made. It is required outside Development. Two kinds of link are made on it:
    /// <list type="bullet">
    /// <item><see cref="LinkTo"/>, for a link the API answers to a caller: on the public URL when one is
    /// configured, else (Development) from the server's root, which each caller puts on the address it
    /// reached the server at: the web UI on its page's origin, a client library on its base address, and
    /// the MCP tools on the address the agent called them at.</item>
    /// <item><see cref="AbsoluteLinkTo"/>, for a link that leaves without a caller to put it anywhere, such
    /// as a reset email or a pairing code: on the public URL, or in Development without one on
    /// <see cref="DevelopmentUrl"/>.</item>
    /// </list>
    /// </summary>
    public sealed class PublicAddress
    {
        /// <summary>Where absolute links point in Development when <c>APP_BASE_URL</c> is not set.</summary>
        public const string DevelopmentUrl = "http://localhost:5190/";

        /// <param name="appBaseUrl">The configured <c>APP_BASE_URL</c>; null or blank when unset.</param>
        public PublicAddress(string? appBaseUrl)
        {
            Configured = string.IsNullOrWhiteSpace(appBaseUrl) ? null : new Uri(appBaseUrl.EndsWith('/') ? appBaseUrl : appBaseUrl + "/");
            Absolute = Configured ?? new Uri(DevelopmentUrl);
        }

        /// <summary>The configured public URL, ending in <c>/</c>; null when none is.</summary>
        public Uri? Configured { get; }

        /// <summary>The URL absolute links are made on, ending in <c>/</c>.</summary>
        public Uri Absolute { get; }

        /// <summary>
        /// The address <c>APP_BASE_URL</c> names. A link to localhost in a real user's inbox is a broken link,
        /// so outside Development it must be set.
        /// </summary>
        /// <exception cref="InvalidOperationException">It is unset outside Development.</exception>
        public static PublicAddress Resolve(string? appBaseUrl, IHostEnvironment environment)
        {
            if (string.IsNullOrWhiteSpace(appBaseUrl) && !environment.IsDevelopment())
            {
                throw new InvalidOperationException("APP_BASE_URL must be set to the server's public URL outside Development.");
            }

            return new PublicAddress(appBaseUrl);
        }

        /// <summary>
        /// The link the API answers to <paramref name="rootRelativePath"/> (such as <c>/api/downloads/…</c>):
        /// absolute on <see cref="Configured"/>, below any path it has, or the path itself without one.
        /// </summary>
        public string LinkTo(string rootRelativePath)
        {
            return Configured == null ? rootRelativePath : On(Configured, rootRelativePath);
        }

        /// <summary>The absolute link to <paramref name="rootRelativePath"/>, on <see cref="Absolute"/>.</summary>
        public string AbsoluteLinkTo(string rootRelativePath)
        {
            return On(Absolute, rootRelativePath);
        }

        private static string On(Uri baseAddress, string rootRelativePath)
        {
            return new Uri(baseAddress, rootRelativePath.TrimStart('/')).AbsoluteUri;
        }
    }
}
