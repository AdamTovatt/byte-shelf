# ByteShelf

A self-hosted file store shared by several apps and people. Apps keep their own records (which
receipt belongs to which transaction) and hand the bytes to a shelf; people browse the same shelves
in the web UI.

## Projects

| Project | What it is |
|---|---|
| `ByteShelf.Server` | The ASP.NET Core server (net10.0). |
| `ByteShelfCommon` | The shared contract (net8.0, AOT-compatible): request and response types, path and shelf-name rules, source-generated JSON. Published as the `ByteShelfCommon` NuGet package. |
| `ByteShelf.Server.Tests`, `ByteShelfCommon.Tests` | xUnit tests. |

## The root folder

One folder, named by `BYTESHELF_ROOT`, holds the whole instance:

```
<root>/
  byteshelf.db              the SQLite database: shelves, directories, files, users, the change log
  objects/ab/cd/<sha256>    file contents, stored whole and never modified
  staging/                  uploads in progress
  keys/                     key material the server generates for itself (the JWT secret)
```

Nothing lives outside it, so a zip of a stopped server's root is a complete copy. Every object is
named by its SHA-256, so `sha256sum` verifies it, and identical contents are stored once.

## Concepts

- A **shelf** holds files: one storage place with its own directories and quota, made per purpose.
- A **user** signs in with email and password and is given read or read-write access to shelves.
  Admins create shelves and users and reach every shelf.
- A **key** lets a program use one shelf: read, add-only (store new files, never overwrite or
  delete) or read-write. It is sent in the `X-Api-Key` header.
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

The tests need nothing installed: each makes a root folder of its own under the test output.

## Documentation

- [DEPLOYMENT.md](DEPLOYMENT.md): environment variables, reverse proxy, backups.
- Server domains: [Storage](ByteShelf.Server/Storage/Storage.md), [Shelves](ByteShelf.Server/Storage/Shelves.md),
  [Access](ByteShelf.Server/Access/Access.md), [Change log](ByteShelf.Server/ChangeLog/ChangeLog.md),
  [Users](ByteShelf.Server/Users/Users.md),
  [Jobs](ByteShelf.Server/Jobs/Jobs.md), [Email](ByteShelf.Server/Email/Email.md),
  [Invalidation](ByteShelf.Server/Invalidation/Invalidation.md), [Version](ByteShelf.Server/Version/Version.md).
- [ByteShelfCommon](ByteShelfCommon/README.md).

## License

MIT
