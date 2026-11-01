# ByteShelf CLI

`byteshelf` works with a ByteShelf server from a terminal, and with `--mcp` serves the same file
operations to an agent as an MCP server. It is a dotnet tool built on ByteShelfClient.

```bash
dotnet tool install -g ByteShelf.Cli
byteshelf login https://files.example.com/ --email me@example.com
byteshelf put receipt.pdf receipts:/2026/
byteshelf ls receipts:/2026
byteshelf get receipts:/2026/receipt.pdf ~/Downloads
```

`byteshelf help` lists every command. A place on a shelf is written `shelf:/path`; `shelf` alone is
its root. Commands take a path without saying whether it is a file or a directory, as a shell's do.
`--json` prints a result as the server's JSON, for scripts, and prints nothing for a command without
one (such as `restore`). `--` ends the options, for a path that starts with `--`.

`find` searches under a directory by text in a file's name or description, type, size, times and
SHA-256, a page at a time, and ends a page that has more after it with the `--after` to pass. Quote
a type family, as in `--type 'image/*'`, or the shell expands it. A time without an offset, such as
`2026-03-01`, is local time. `describe` sets a file's description and `describe --clear` removes
it; `put --description` stores one with the file. `share` prints a link that opens a file without
signing in, for 24 hours or the `--expires` given (`30m`, `12h`, up to `7d`); nothing takes it back
before then.

`tree` lists everything under a directory by path, directories ending in `/`, at any depth or down
to `--depth` levels, a page at a time as `find` does. `put` given a local folder stores it and
everything in it, empty folders included, and `get` given a directory (or a shelf alone) saves it as a
local folder, both keeping the structure and placing a folder as `get` and `put` place a file. A file
or folder that cannot be moved, such as a path already taken without `--overwrite` or a symbolic link
(never followed), is left out and listed, the rest go on, and the command then fails with exit code 1.

A file is typed by its extension when `--type` does not say otherwise, and `get` checks what it
saves against the file's SHA-256, writing to a side file first, so a failed download leaves nothing
behind and replaces nothing.

## Signing in

`byteshelf login <server> --email <email>` signs in as a user, and `byteshelf login <server> --key`
uses a shelf key. The password or key is read from the terminal without echo, or from the first line
of standard input when one is piped in; never from an argument, which would land in the shell's
history. `byteshelf logout` ends the session on the server and forgets the login.

The login is kept in `byteshelf/config.json` in the user's config folder (`$XDG_CONFIG_HOME`, else
`~/.config`, on Linux and macOS; `%APPDATA%` on Windows), readable only by its owner.
`BYTESHELF_CONFIG` names another file. `BYTESHELF_URL` and `BYTESHELF_KEY` override it, so a program
or an agent can use a key with no login: both together name a server and its key, and
`BYTESHELF_KEY` alone uses the key with the kept server.

A kept session is shared by every `byteshelf` process, MCP servers included. A refresh token works
once, so every renewal happens with the config file locked and first takes up tokens another
process renewed to. Each command and tool call reads the login afresh, so a running MCP server
follows a login or logout made elsewhere.

## As an MCP server

```bash
claude mcp add byteshelf -- byteshelf --mcp
```

It signs in as the CLI does, and its tools cover shelves, files, directories and the trash: listing a
directory or its whole tree, searching, uploading a local file or text given in the call or a whole
local folder, describing a file, sharing one by a link (not marked read-only, so a host asks first:
the link hands the file on), downloading a file to a local file or as text (UTF-8, up to 256 KiB) or
a whole directory to a local folder, moving, deleting, restoring and emptying the trash. Local paths
must be absolute, since the server's working folder is not the agent's. Signing in, users, grants
and keys stay on the command line, since they carry passwords and secrets an agent's transcript
should not hold.

## Exit codes

- `0`: done.
- `1`: the server refused or could not be reached, a download did not match its SHA-256, a local
  file could not be read or written, a folder transfer left anything out, or another command held
  the config for 30 seconds.
- `2`: the command was given wrongly or cannot run as things are: before signing in, a local file
  or folder that is missing, or a file that exists when `get` of that one file was not asked to
  overwrite it.
- `130`: interrupted with Ctrl+C. A second Ctrl+C ends the process at once.
