# ByteShelf.Tools

What the `byteshelf` command line and both MCP servers do with a shelf, written once on
ByteShelfClient. Built into `ByteShelf.Cli` and `ByteShelf.Server`, not published on its own.

## The operations

Shared by the CLI's commands and the MCP tools, so a person and an agent get the same behaviour and
the same words:

| Type | What it does |
|---|---|
| `ShelfLocation` | A place on a shelf, `shelf:/path` or `shelf` alone for its root. |
| `Entries` | A file or directory by path, whichever is there. |
| `Transfers` | Moving one file between a local file or stream and a shelf; a download is checked against its SHA-256 and written to a side file first. |
| `FolderTransfers` | Moving a whole local folder or shelf directory. An entry that cannot move is left out and listed; a refusal that would fail every entry (signed out, no permission, over quota, a replica, the server out of reach) stops the transfer. Symbolic links are never followed. |
| `LinkLifetimes` | A link's lifetime as written: `30m`, `12h`, `7d`. |
| `PermissionNames` | The words for access and key permissions. |
| `TextFormat` | How results read as text, in the terminal and in answers to an agent. |
| `Failure` | What to say when something fails, ending with the server's error code. |
| `UsageException` | A command or tool call given wrongly, or one that cannot run as things are. |
| `IShelfClientSource` | Where an operation gets its client: the CLI's login, or on the server the caller's key. |

## The MCP tools (`ByteShelf.Tools.Mcp`)

`ByteShelfMcpTools` is the one set of tools: `byteshelf --mcp` serves it over stdio, the server at
`/mcp` over HTTP ([Mcp](../ByteShelf.Server/Mcp/Mcp.md)).

- Each tool is marked with `ToolNeedsAttribute`: whether it reads, adds, writes or needs an admin,
  and whether it works only on the agent's own files. The server offers a key only the tools its
  access allows, and a tool without the mark to no one.
- `ToolHost` says where the tools run. On the agent's machine a tool takes a local path; on the
  server it refuses one, since the server cannot reach the agent's files.
- `upload_file` takes text or base64 of up to `MaxInlineBytes` (256 KiB), and `download_file`
  answers text up to the same size: what an agent can carry in its own context. It answers a PNG,
  JPEG, GIF or WebP of up to `MaxInlineImageBytes` (1 MiB) as image content the model sees.
  Anything else moves as a local file, through the API, or by a `share_file` link.
- Every tool working on one shelf takes `shelf` last and optional: left out, it is the only shelf
  the caller reaches, which a shelf key always is. Several reachable shelves ask for a name.
- `RequiredArguments` is a call-tool filter both servers add, so a call missing a required argument
  is answered naming it rather than with the SDK's bare "An error occurred invoking".
- `ToolInstructions` is what both servers tell an agent when it connects. Tests check what each
  server actually sends: the stdio server's instructions name every tool except the admin-only
  `create_shelf`, and `/mcp`'s name every tool it offers a read-write key. Other tests check that the
  limits the instructions and the tool descriptions state match the constants they come from.
- A failure reaches the agent as the tool's error text, ending with the error code in parentheses.
