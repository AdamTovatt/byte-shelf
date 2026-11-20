using ByteShelf.Tools;
using ByteShelf.Tools.Mcp;
using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Controllers;
using ByteShelf.Server.Version.Services;
using ByteShelfCommon;
using ModelContextProtocol.Server;

namespace ByteShelf.Server.Mcp
{
    /// <summary>
    /// What <c>/mcp</c> offers one call, made afresh for every request (it serves statelessly): the
    /// tools any of its key's grants allows, only the reading ones on a replica, never the admin ones or
    /// the ones that work on the agent's own files; and instructions naming the server's release and
    /// each of the key's shelves with its access. A tool not marked with <see cref="ToolNeedsAttribute"/>
    /// is offered to no one. The API the tools call checks every call again, per shelf; the offer only
    /// keeps an agent from seeing tools that could only fail.
    /// </summary>
    internal static class McpCallerSetup
    {
        public static async Task ConfigureAsync(HttpContext httpContext, McpServerOptions options, CancellationToken cancellationToken)
        {
            IServiceProvider services = httpContext.RequestServices;
            ShelfKey key = ShelfKeyAuthenticationHandler.GetKey(httpContext)
                ?? throw new InvalidOperationException("/mcp ran without the key its policy requires.");
            bool isReplica = !await services.GetRequiredService<ReplicationState>().AllowsWritesAsync();
            IReadOnlyList<ShelfAccess> shelves = await services.GetRequiredService<IShelfAccessResolver>().GetAccessesAsync(ShelfCaller.ForKey(key));
            if (shelves.Count == 0)
            {
                // A key works somewhere, but not at every moment: it lost its last shelf since signing in,
                // or a replica holds it between its own log entry and its grant's.
                throw new PermissionDeniedException("The key reaches no shelf.");
            }

            ToolHost host = services.GetRequiredService<ToolHost>();

            McpServerPrimitiveCollection<McpServerTool> offered = new McpServerPrimitiveCollection<McpServerTool>();
            foreach (McpServerTool tool in options.ToolCollection ?? new McpServerPrimitiveCollection<McpServerTool>())
            {
                if (tool.Metadata.OfType<ToolNeedsAttribute>().FirstOrDefault() is ToolNeedsAttribute needs
                    && shelves.Any(access => Offers(needs, access, isReplica, host)))
                {
                    offered.Add(tool);
                }
            }

            options.ToolCollection = offered;
            // The configured address, else the one this request came in at, where the agent reached the
            // server; CalledAt is the tools' own rule for where to call it, kept beside them in Mcp.
            Uri server = services.GetRequiredService<PublicAddress>().Configured ?? CallerKeyClientSource.CalledAt(httpContext.Request);
            string release = services.GetRequiredService<IVersionService>().GetRelease().Version;
            options.ServerInstructions = InstructionsFor(shelves, isReplica, server, release);
        }

        /// <summary>
        /// Whether to offer a tool for one shelf: the key's access there allows what it needs (asked of
        /// <see cref="ShelfAccess"/>, where the rule lives), it reaches the files it works on, and on a
        /// replica it only reads, since <c>ReplicaReadOnlyFilter</c> refuses anything else there.
        /// </summary>
        internal static bool Offers(ToolNeedsAttribute needs, ShelfAccess access, bool isReplica, ToolHost host)
        {
            if (needs.LocalFilesOnly && !host.ReachesLocalFiles)
            {
                return false;
            }

            ShelfOperation operation = OperationOf(needs.Need);
            return access.Allows(operation) && (!isReplica || operation == ShelfOperation.Read);
        }

        private static ShelfOperation OperationOf(ToolNeed need)
        {
            return need switch
            {
                ToolNeed.Read => ShelfOperation.Read,
                ToolNeed.Add => ShelfOperation.Add,
                ToolNeed.Write => ShelfOperation.Write,
                // Admins only, which no key is: creating a shelf is beyond any shelf's key.
                ToolNeed.Admin => ShelfOperation.Administer,
                _ => throw new InvalidOperationException($"No shelf operation is defined for a tool that needs {need}."),
            };
        }

        /// <param name="shelves">The key's access to each shelf it reaches (a key reaches at least one), whose permission the instructions name and which decides the ways in shown, as it decides the tools offered.</param>
        /// <param name="server">The server's root, ending in <c>/</c>, for the curl commands.</param>
        /// <param name="release">The release the server runs, which every caller here may know, since each holds a key.</param>
        internal static string InstructionsFor(IReadOnlyList<ShelfAccess> shelves, bool isReplica, Uri server, string release)
        {
            bool oneShelf = shelves.Count == 1;
            string reach = oneShelf
                ? $"""The key reaches one shelf, "{shelves[0].Shelf.Name}", which every tool works on when you leave shelf out."""
                : "The key reaches these shelves; give every tool the shelf to work on:\n"
                    + string.Join("\n", shelves.Select(access => $"""- "{access.Shelf.Name}": {PermissionOf(access)}"""));
            string offer = isReplica
                ? "This server is a replica, a read-only copy of another, so only the tools that need read access are offered: they read, and make, list and revoke this server's own links."
                : oneShelf
                    ? $"The key's access is {PermissionOf(shelves[0])}, and only the tools it allows are offered."
                    : "The tools any of those accesses allows are offered; a shelf whose access does not allow one refuses it (permission_denied).";

            ShelfAccess? storing = isReplica ? null : shelves.FirstOrDefault(access => access.AllowsStore(overwrite: false));
            string files = storing != null
                ? "The server cannot reach your files: give upload_file the contents as text or base64, and\nread with download_file or a share_file link."
                : "The server cannot reach your files: read with download_file or a share_file link.";

            string download = $"""  curl -fsS -H "Authorization: Bearer $BYTESHELF_KEY" -o q3.pdf "{ContentUrl(server, shelves[0].Shelf.Name)}" """.TrimEnd();
            string streaming;
            if (storing != null)
            {
                bool overwrites = storing.AllowsStore(overwrite: true);
                string onShelf = oneShelf ? string.Empty : $" on \"{storing.Shelf.Name}\"";
                streaming = $"""
                    {"  "}curl -fsS -H "Authorization: Bearer $BYTESHELF_KEY" -T ./q3.pdf "{ContentUrl(server, storing.Shelf.Name)}"
                    {download}
                    The first stores{onShelf} ({(overwrites ? "add &overwrite=true to replace a file" : "never over a file already there")}), the second saves.
                    Without the key, create_upload_link and share_file make links that need none.
                    """;
            }
            else
            {
                streaming = $"""
                    {download}
                    Without the key, share_file makes a link that needs none.
                    """;
            }

            return $"""
                {ToolInstructions.Text}

                This server is the ByteShelf server itself, release {release}, reached with a shelf key. {reach}
                {offer}
                {files}

                A shell that holds the key (here in $BYTESHELF_KEY) can stream a file of any size through
                the API instead, the path URL-encoded{(oneShelf ? string.Empty : ", on any of its shelves by name in the URL")}:
                {streaming}
                """;
        }

        private static string PermissionOf(ShelfAccess access)
        {
            // Every access here is a key's (ShelfAccess.ForKey), which always carries its permission.
            return PermissionNames.Of(access.KeyPermission!.Value);
        }

        private static string ContentUrl(Uri server, string shelf)
        {
            return $"{server.AbsoluteUri}{EntriesController.ShelfRoute.Replace("{shelf}", shelf, StringComparison.Ordinal)}/content?path=/reports/q3.pdf";
        }
    }
}
