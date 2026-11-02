using ByteShelf.Tools;
using ByteShelf.Tools.Mcp;
using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.Replication.Services;
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
    internal static class McpCallerOptions
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
            options.ServerInstructions = InstructionsFor(shelf.Name, key.Permission, isReplica);
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

        internal static string InstructionsFor(string shelf, ShelfKeyPermission permission, bool isReplica)
        {
            string reach = isReplica
                ? "This server is a replica, a read-only copy of another, so only reading tools are offered."
                : $"The key's access is {PermissionNames.Of(permission)}, and only the tools it allows are offered.";

            return $"""
                {ToolInstructions.Text}

                This server is the ByteShelf server itself, reached with a shelf key. The key reaches one
                shelf, "{shelf}", which every tool works on when you leave shelf out. {reach}
                The server cannot reach your files: give upload_file the contents as text or base64, and
                read with download_file or a share_file link.
                """;
        }
    }
}
