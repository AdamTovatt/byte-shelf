using ByteShelf.Tools;
using ByteShelf.Tools.Mcp;
using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Storage.Controllers;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using ModelContextProtocol.Server;

namespace ByteShelf.Server.Mcp
{
    /// <summary>
    /// What <c>/mcp</c> offers one call, made afresh for every request (it serves statelessly): the
    /// tools its key's permission allows, only the reading ones on a replica, never the admin ones or the
    /// ones that work on the agent's own files; and instructions naming the key's shelf. A tool not
    /// marked with <see cref="ToolNeedsAttribute"/> is offered to no one. The API the tools call checks
    /// every call again; the offer only keeps an agent from seeing tools that could only fail.
    /// </summary>
    internal static class McpCallerSetup
    {
        public static async Task ConfigureAsync(HttpContext httpContext, McpServerOptions options, CancellationToken cancellationToken)
        {
            IServiceProvider services = httpContext.RequestServices;
            ShelfKey key = ShelfKeyAuthenticationHandler.GetKey(httpContext)
                ?? throw new InvalidOperationException("/mcp ran without the key its policy requires.");
            bool isReplica = !await services.GetRequiredService<ReplicationState>().AllowsWritesAsync();
            Shelf shelf = await services.GetRequiredService<IShelfService>().GetByIdAsync(key.ShelfId);
            ShelfAccess access = await services.GetRequiredService<IShelfAccessResolver>().ResolveAsync(ShelfCaller.ForKey(key), shelf);
            ToolHost host = services.GetRequiredService<ToolHost>();

            McpServerPrimitiveCollection<McpServerTool> offered = new McpServerPrimitiveCollection<McpServerTool>();
            foreach (McpServerTool tool in options.ToolCollection ?? new McpServerPrimitiveCollection<McpServerTool>())
            {
                if (tool.Metadata.OfType<ToolNeedsAttribute>().FirstOrDefault() is ToolNeedsAttribute needs && Offers(needs, access, isReplica, host))
                {
                    offered.Add(tool);
                }
            }

            options.ToolCollection = offered;
            // The configured address, else the one this request came in at, where the agent reached the
            // server; CalledAt is the tools' own rule for where to call it, kept beside them in Mcp.
            Uri server = services.GetRequiredService<PublicAddress>().Configured ?? CallerKeyClientSource.CalledAt(httpContext.Request);
            options.ServerInstructions = InstructionsFor(shelf.Name, key.Permission, access, isReplica, server);
        }

        /// <summary>
        /// Whether to offer a tool: the key's access allows what it needs (asked of
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

        /// <param name="permission">The key's permission, named in the instructions.</param>
        /// <param name="access">What the key may do on its shelf, which decides the ways in shown, as it decides the tools offered.</param>
        /// <param name="server">The server's root, ending in <c>/</c>, for the curl commands.</param>
        internal static string InstructionsFor(string shelf, ShelfKeyPermission permission, ShelfAccess access, bool isReplica, Uri server)
        {
            string reach = isReplica
                ? "This server is a replica, a read-only copy of another, so only reading tools are offered."
                : $"The key's access is {PermissionNames.Of(permission)}, and only the tools it allows are offered.";
            bool stores = !isReplica && access.AllowsStore(overwrite: false);
            bool overwrites = stores && access.AllowsStore(overwrite: true);
            string content = $"{server.AbsoluteUri}{EntriesController.ShelfRoute.Replace("{shelf}", shelf, StringComparison.Ordinal)}/content?path=/reports/q3.pdf";
            string download = $"""  curl -fsS -H "Authorization: Bearer $BYTESHELF_KEY" -o q3.pdf "{content}" """.TrimEnd();

            string files = stores
                ? "The server cannot reach your files: give upload_file the contents as text or base64, and\nread with download_file or a share_file link."
                : "The server cannot reach your files: read with download_file or a share_file link.";
            string streaming = stores
                ? $"""
                    {"  "}curl -fsS -H "Authorization: Bearer $BYTESHELF_KEY" -T ./q3.pdf "{content}"
                    {download}
                    The first stores ({(overwrites ? "add &overwrite=true to replace a file" : "never over a file already there")}), the second saves.
                    Without the key, create_upload_link and share_file make links that need none.
                    """
                : $"""
                    {download}
                    Without the key, share_file makes a link that needs none.
                    """;

            return $"""
                {ToolInstructions.Text}

                This server is the ByteShelf server itself, reached with a shelf key. The key reaches one
                shelf, "{shelf}", which every tool works on when you leave shelf out. {reach}
                {files}

                A shell that holds the key (here in $BYTESHELF_KEY) can stream a file of any size through
                the API instead, the path URL-encoded:
                {streaming}
                """;
        }
    }
}
