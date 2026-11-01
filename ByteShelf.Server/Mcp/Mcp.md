# Mcp

The server's MCP endpoint and what it tells agents. The tools themselves are in `ByteShelf.Tools`
([its README](../../ByteShelf.Tools/README.md)), the one copy `byteshelf --mcp` serves too.

## `/mcp`

MCP over Streamable HTTP, served statelessly: every request stands alone, so any instance can answer
it and nothing is kept between calls. It is open to shelf keys only (the `McpCaller` policy runs the
key scheme and nothing else): a key as the bearer token, which is what MCP clients send, or in
`X-Api-Key`. No key, a key that is not one, or a user's session answer 401. There is no OAuth: a key
is made by an admin, in the web UI's shelf settings or with `byteshelf key create`.

For each request `McpCallerOptions` builds what the caller is offered:

- **Tools**: each is marked with what it needs (`ToolNeedsAttribute`): read, add, write or admin, and
  whether it works only on the agent's own files. The need maps to a `ShelfOperation`, and the key's
  `ShelfAccess` from `ShelfAccessResolver` decides, as it does for the API: a read key gets the
  reading tools, an add-only key those and the adding ones, a read-write key all three kinds, and no
  key the admin ones (creating shelves). A replica offers only reading tools. Local-file tools
  (`upload_folder`, `download_directory`) are offered only where `ToolHost` reaches the agent's
  files, which this server does not. A tool without the mark is offered to no one, and a test checks
  every tool has one.
- **Instructions**: what ByteShelf is and how the tools fit together (`ToolInstructions`), the key's
  shelf by name, its access, and that the server cannot reach the agent's files. A test checks they
  name every tool offered.

## Acting through the API

A tool call runs `ByteShelfMcpTools`, which act through `IByteShelfClient` as they do in the CLI.
Here the client is `ByteShelfHttpClient` on this server's own REST API (`SelfApi`), sending the
caller's key. So every access check, the replica's read-only rule, quotas and the change log apply
to a tool call exactly as to the same request made directly, and there is no second path into the
services to keep in step. The server calls itself at the first plain-HTTP address Kestrel is bound
to, as bound: a server bound to a single interface's address is called there, since loopback may
not be listening. A listen-on-everything host (`+`, `*`, `0.0.0.0`, `[::]`) is called at loopback.
HTTPS is never used: the certificate would not name the address called. A server with no plain-HTTP
address (HTTPS or a Unix socket only) cannot run its tools, and says so. A link `share_file` makes is
put on the server's public address, `APP_BASE_URL`, not on the address it called itself at, so the
person the agent hands it to can open it.

A local path means nothing on the server, so a tool given one (`upload_file`'s `localPath`,
`download_file`'s) refuses it and says to give the contents instead: `upload_file` takes text or a
binary file as base64, each up to 256 KiB, a size an agent can carry in its own context.
`download_file` answers text of up to 256 KiB; for anything larger or binary, `share_file` makes a
link.

## `/llms.txt`

Open to anyone, built in from `Mcp/llms.txt`: what ByteShelf is, and how an agent reaches it over
MCP, the CLI and the API.
