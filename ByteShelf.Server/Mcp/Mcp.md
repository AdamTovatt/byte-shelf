# Mcp

The server's MCP endpoint and what it tells agents. The tools themselves are in `ByteShelf.Tools`
([its README](../../ByteShelf.Tools/README.md)), the one copy `byteshelf --mcp` serves too.

## `/mcp`

MCP over Streamable HTTP, served statelessly: every request stands alone, so any instance can answer
it and nothing is kept between calls. It is open to shelf keys only (the `McpCaller` policy runs the
key scheme and nothing else): a key as the bearer token, which is what MCP clients send, or in
`X-Api-Key`. No key, a key that is not one, or a user's session answer 401. There is no OAuth: a key
is made by an admin, in the web UI's shelf settings or with `byteshelf key create`.

For each request `McpCallerSetup` builds what the caller is offered:

- **Tools**: each is marked with what it needs (`ToolNeedsAttribute`): read, add, write or admin, and
  whether it works only on the agent's own files. The need maps to a `ShelfOperation`, and the key's
  `ShelfAccess` from `ShelfAccessResolver` decides, as it does for the API: a read key gets the
  reading tools, an add-only key those and the adding ones, a read-write key all three kinds, and no
  key the admin ones (creating shelves). A replica offers only reading tools. Local-file tools
  (`upload_folder`, `download_directory`) are offered only where `ToolHost` reaches the agent's
  files, which this server does not. A tool without the mark is offered to no one, and a test checks
  every tool has one.
- **Instructions**: what ByteShelf is and how the tools fit together (`ToolInstructions`), the key's
  shelf by name, its access, that the server cannot reach the agent's files, and `curl` commands
  that stream a file with the key, on `APP_BASE_URL` or else the address the agent called `/mcp` at:
  down for every key, up too for a key that may store and not on a replica. A test checks they name every tool offered.

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
never on the address the server called itself at: the API answers it on `APP_BASE_URL` when that is
set (`Shared/PublicAddress`), and otherwise (Development) `CallerKeyClientSource` puts it on the
address the agent called `/mcp` at, by its `Host` header as sent (`X-Forwarded-Host` is not
believed: nothing outside Development reads the host). Either way the person the agent hands it to
can open it.

The key reaches one shelf, so every tool's `shelf` may be left out and means that shelf (the tools
take the only shelf a caller reaches). A call missing an argument the tool requires is answered
naming it (`RequiredArguments`), not with the SDK's bare "An error occurred invoking".

A local path means nothing on the server, so a tool given one (`upload_file`'s `localPath`,
`download_file`'s) refuses it and says to give the contents instead: `upload_file` takes text or a
binary file as base64, each up to 256 KiB, a size an agent can carry in its own context.
A file larger than `upload_file` takes goes the other way through `create_upload_link`, whose link
the agent's shell (or a person) sends it to with `curl -T`; the link acts as the key, checked again
when it is used, and is put on the same address a `share_file` link is.

`download_file` answers text of up to 256 KiB, and a PNG, JPEG, GIF or WebP image of up to 1 MiB as
image content the agent sees. Any other file stored as an image (its stored type is `image/*`), of
up to 32 MiB, comes as a JPEG preview at most 1568 pixels on its long side, saying the image's full
size and to make a link with `share_file` and fetch it with `curl` for the whole file; for anything
else it says to make that link. `SkiaImagePreviews` makes previews with SkiaSharp (native; the
platforms kept are in `Directory.Build.targets`), upright by the image's EXIF orientation and on
white where it is transparent. It reads and makes one at a time, so calls waiting their turn hold
no image. An image gets no preview, only the pointer and why, when Skia cannot decode it (HEIC, say),
when its pixels would take more than `MaxDecodedBytes` (256 MiB, after the scaling a JPEG allows
while decoding, with the mipmaps drawing it down builds), or when its detail leaves even the
lowest-quality JPEG over 1 MiB.

A password never goes through a tool call, so the tools leave encrypted files alone: `download_file`
refuses one saying so, `download_directory` leaves them out, and the server refuses links to them and
plain files under an encrypted directory.

## `/llms.txt`

Open to anyone, built in from `Mcp/llms.txt`: what ByteShelf is, and how an agent reaches it over
MCP, the CLI and the API.
