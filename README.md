# ByteShelf

A self-hosted file store shared by several apps and people. Apps keep their own records (which
receipt belongs to which transaction) and hand the bytes to a shelf; people browse the same shelves
in the web UI.

## Projects

| Project | What it is |
|---|---|
| `ByteShelf.Server` | The ASP.NET Core server (net10.0). |
| `ByteShelfCommon` | The shared contract (net8.0, AOT-compatible): request and response types, path and shelf-name rules, source-generated JSON. Published as the `ByteShelfCommon` NuGet package. |
| `ByteShelfClient` | The client library (net8.0, AOT-compatible): `IByteShelfClient` over HTTP, and in memory for an app's tests. Published as the `ByteShelfClient` NuGet package. |
| `byteshelf.client` | The web UI (React, TypeScript, Vite): browse and upload to the shelves you reach; for admins, shelves, users, access, keys and replication. The server serves its build. |
| `ByteShelf.Cli` | The `byteshelf` dotnet tool (net10.0): the command line, and with `--mcp` an MCP server for agents. |
| `ByteShelf.Tools` | The file operations the CLI's commands and the MCP tools share, and the MCP tools themselves (net10.0), one copy for both MCP servers: the CLI's over stdio and the server's at `/mcp`. Built into both, not published on its own. |
| `ByteShelf.Server.Tests`, `ByteShelfCommon.Tests`, `ByteShelfClient.Tests`, `ByteShelf.Cli.Tests` | xUnit tests; the server's include the client contract, run against both clients, and the CLI's login against the server. |

## The root folder

One folder, named by `BYTESHELF_ROOT`, holds the whole instance:

```
<root>/
  byteshelf.db              the SQLite database: shelves, directories, files, users, the change log
  objects/ab/cd/<sha256>    file contents, stored whole and never modified
  staging/                  uploads in progress
  keys/                     secrets the server generates for itself (the JWT and download-link keys)
```

Nothing lives outside it, so a zip of a stopped server's root is a complete copy; `--backup` writes a
consistent one from a running server, leaving out uploads in progress. Every object is named by its
SHA-256, so `sha256sum` verifies it, and identical contents are stored once.

## Concepts

- A **shelf** holds files: one storage place with its own directories and quota, made per purpose.
- A **user** signs in with email and password and is given read or read-write access to shelves.
  Admins create shelves and users and reach every shelf.
- A **key** lets a program use one shelf: read, add-only (store new files, never overwrite or
  delete) or read-write. It is sent in the `X-Api-Key` header or as a bearer token.
- Files live in real **directories** and are addressed by path (`/invoices/2026/march.pdf`) or id.
- A delete moves things to the **trash**, restorable for 30 days by default, then purged; anyone
  with write access can empty a shelf's trash sooner.

## Running it

```bash
dotnet build ByteShelf.slnx
dotnet test ByteShelf.slnx
BYTESHELF_ROOT=./dev-root INITIAL_ADMIN_EMAIL=admin@example.com INITIAL_ADMIN_PASSWORD=change-me \
  ASPNETCORE_ENVIRONMENT=Development dotnet run --project ByteShelf.Server
```

Building the server restores the web UI's packages, so it needs Node.js and npm as well as the
.NET SDK. The .NET tests need nothing more: each makes a root folder of its own under the test
output. The web UI's own tests are in [byteshelf.client](byteshelf.client/README.md).

For the web UI while developing, copy `ByteShelf.Server/Properties/launchSettings_git.json` to
`launchSettings.json` beside it and `dotnet run --project ByteShelf.Server`: the server starts the
Vite dev server and forwards to it. A published server serves the UI's production build itself.
remote-claude's dev server button runs the same thing from `remote-claude.json`, so it needs
`launchSettings.json` copied first too.

The CLI runs from source the same way, against a running server. With the `dotnet run` above the
server is at `http://localhost:5000/` and the password is `change-me`; with `launchSettings.json`
copied it is at `http://localhost:5190/` and the password is `changemenow`:

```bash
dotnet run --project ByteShelf.Cli -- login http://localhost:5000/ --email admin@example.com
dotnet run --project ByteShelf.Cli -- shelves
```

## For agents

- **MCP on the server**: `https://<server>/mcp`, Streamable HTTP, with a shelf key as the bearer
  token. It offers the key's shelf and only the tools its permission allows
  ([Mcp](ByteShelf.Server/Mcp/Mcp.md)). For example, `claude mcp add --transport http byteshelf
  https://files.example.com/mcp --header "Authorization: Bearer bsk_..."`.
- **MCP on the agent's machine**: `byteshelf --mcp`, the same tools over stdio, plus local files and
  folders ([the CLI](ByteShelf.Cli/README.md)).
- **`/llms.txt`** on every server says what ByteShelf is and how to reach it.
- **[skills/byteshelf/SKILL.md](skills/byteshelf/SKILL.md)** teaches an agent the `byteshelf` command
  line.

## Documentation

- [DEPLOYMENT.md](DEPLOYMENT.md): installing, environment variables, reverse proxy, replicas, backups.
- Server domains: [Storage](ByteShelf.Server/Storage/Storage.md), [Shelves](ByteShelf.Server/Storage/Shelves.md),
  [Access](ByteShelf.Server/Access/Access.md), [Change log](ByteShelf.Server/ChangeLog/ChangeLog.md),
  [Users](ByteShelf.Server/Users/Users.md),
  [Jobs](ByteShelf.Server/Jobs/Jobs.md), [Email](ByteShelf.Server/Email/Email.md),
  [Invalidation](ByteShelf.Server/Invalidation/Invalidation.md), [Version](ByteShelf.Server/Version/Version.md),
  [Replication](ByteShelf.Server/Replication/Replication.md), [Backup](ByteShelf.Server/Backup/Backup.md),
  [Mcp](ByteShelf.Server/Mcp/Mcp.md).
- [ByteShelfCommon](ByteShelfCommon/README.md), [ByteShelfClient](ByteShelfClient/README.md),
  [the CLI and MCP server](ByteShelf.Cli/README.md), [the shared operations and MCP tools](ByteShelf.Tools/README.md),
  [the web UI](byteshelf.client/README.md).

## License

The server, the CLI, the MCP tools and the web UI are GPL-3.0-only ([LICENSE](LICENSE)). The libraries an app
builds against, ByteShelfClient and ByteShelfCommon, are MIT
([ByteShelfClient/LICENSE](ByteShelfClient/LICENSE), [ByteShelfCommon/LICENSE](ByteShelfCommon/LICENSE)).
