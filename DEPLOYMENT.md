# Deploying ByteShelf

ByteShelf is installed and updated with updaemon: `dotnet publish ByteShelf.Server` produces the
executable with its `updaemon.json`, which names it, and the web UI's build in `wwwroot/`. Publishing
builds the web UI, so it needs Node.js and npm. The running server needs one writable folder, the
root, and nothing else on disk. The publish carries SkiaSharp's native library, which `/mcp` makes
image previews with, for Linux x64 and arm64 (glibc, no system libraries needed), macOS and Windows
x64 (`Directory.Build.targets`). Elsewhere (musl, 32-bit arm, Windows on arm) the server runs, but
`/mcp`'s `download_file` fails on an image it would preview.

## Installing

1. Publish: `dotnet publish ByteShelf.Server -c Release -o <folder>`, and install that folder with
   updaemon (or run the `ByteShelf.Server` executable in it under any service manager).
2. Set `BYTESHELF_ROOT` to an empty folder the service user owns and `APP_BASE_URL` to the public URL
   (table below).
3. Choose where it listens with `ASPNETCORE_URLS`, such as `http://127.0.0.1:5000` behind a reverse
   proxy. Unset, Kestrel listens on `http://localhost:5000`.
4. Start it. The first start creates the root (unless step 5 ran first), migrates its database and
   generates its secrets, and logs that there are no users yet.
5. Create the first admin on the server's machine, as the service user, while it runs or before its
   first start, which this then does in its place for the root and its database:
   `BYTESHELF_ROOT=<root> ByteShelf.Server --create-user <email> --name "<name>" --admin`, which asks
   for the password (see [Users from the command line](#users-from-the-command-line)). Then sign in at
   `APP_BASE_URL`. Whoever runs it owns a root it creates, and the server opens only a root its own
   user owns, so never run it with `sudo` before the first start.

Setting `INITIAL_ADMIN_EMAIL` and `INITIAL_ADMIN_PASSWORD` before the first start creates that admin
instead of step 5; remove them once it exists.

Every start migrates the database first. `ByteShelf.Server --migrate-only` (with `BYTESHELF_ROOT`)
only migrates it and exits, for running migrations as their own step before a start.

## Users from the command line

For when no admin can sign in, the server's executable changes users of its root directly, with the
server running or stopped. Run it on the server's machine, as the user the server runs as, with
`BYTESHELF_ROOT` set to the root:

```
ByteShelf.Server --create-user <email> --name "<display name>" [--admin]
ByteShelf.Server --reset-password <email>
ByteShelf.Server --set-admin <email>
ByteShelf.Server --unset-admin <email>
```

A new password is never an argument, which other users of the machine could read: it is typed twice
at the terminal without being shown, or read as one line from stdin when stdin is redirected
(`< password-file`). The commands go through the same rules as the API: password length, a taken
email, never removing the last admin. A reset ends every session and reset link of that user: the
access tokens they hold are refused at once, and none of their sessions can be renewed. The change
log records each change, so replicas follow. A replica refuses them; run them on its primary.

They never migrate an existing database, which the running server may be using: a database an older
version left is refused until this version has started on it or `--migrate-only` has run. A root with
no database yet is refused too, except by `--create-user`, which creates it and its database first,
as the first start would, so the first admin can exist before the server first starts; it removes them
again if the user is not created. The others refuse such a root rather than create one at a mistyped
path. Exit codes: 0 done, 1 refused or failed (the reason is logged), 2 the arguments are wrong or
`BYTESHELF_ROOT` is not set.

## Environment variables

| Variable | Required | Meaning |
|---|---|---|
| `BYTESHELF_ROOT` | yes | The root folder. Created on first start, or by `--create-user` or `--follow` run before it, with `objects/`, `staging/` and `keys/` inside, owned by whoever created it. The server makes it reachable by its owner only on every start, so it must be owned by the user the server runs as. |
| `APP_BASE_URL` | outside Development | The public URL, e.g. `https://shelf.example.com`. Password-reset links, the download links the API answers (share links, the web UI's Copy link, MCP's `share_file`) and upload links point at it. |
| `POSTMARK_API_KEY` | to send email | A Postmark server token. Without it no email is sent (see [Email](#email)). |
| `EMAIL_FROM_ADDRESS` | with `POSTMARK_API_KEY` | The address mail is from, a sender confirmed in Postmark. The server refuses to start with a token and no address. |
| `EMAIL_FROM_NAME` | no | The name mail is from. `ByteShelf` by default. |
| `REMOTE_CLAUDE_DEV_MAIL_DIR` | no | A folder each email is written into as a file instead of being sent, in any environment, unless `POSTMARK_API_KEY` is set. Set by remote-claude for a dev server; see [Email](#email). |
| `INITIAL_ADMIN_EMAIL`, `INITIAL_ADMIN_PASSWORD` | no | Create the first admin while there are no users; ignored afterwards and can be removed. Without them, create it with `--create-user`. |
| `INITIAL_ADMIN_DISPLAY_NAME` | no | The first admin's display name; the email's local part otherwise. |
| `BYTESHELF_RESTORE_WINDOW_DAYS` | no | How long a deletion can be restored before it is purged. 30 by default. |
| `BYTESHELF_TRUSTED_PROXIES` | behind a proxy | Comma-separated IPs or CIDR ranges of the reverse proxies in front of the server. Their `X-Forwarded-For` and `X-Forwarded-Proto` are believed, so rate limits and the sign-in delay count each client rather than the proxy. Loopback is always trusted. |
| `ASPNETCORE_URLS` | no | Where Kestrel listens. |

Two secrets are generated into `keys/` on first start, readable by the owner only: `jwt-secret`
signs sign-ins, `link-secret` signs links that work without signing in: the share links any key or
user with read access can make for up to 7 days, and the upload links any key or user that may store can make for up to 24 hours. They travel with the root,
so a copied or restored root keeps its sign-ins and its unexpired links, share links included. A
single link cannot be taken back; deleting `link-secret` and restarting ends every link at once, and
a new secret is generated.

## Email

The server sends its email, such as password-reset links, through Postmark when `POSTMARK_API_KEY`
and `EMAIL_FROM_ADDRESS` are set. Open and link tracking are always off, whatever the Postmark
server's settings, so a link arrives as written. A send Postmark refuses is logged as an error.

Without a token no email is sent, and the server says so at startup. When `REMOTE_CLAUDE_DEV_MAIL_DIR`
is set, which remote-claude does for a dev server it starts, each email is written there as a file
for remote-claude to show ([Email](ByteShelf.Server/Email/Email.md)), in any environment. Without
either, a user who asks for a password reset gets the same 204, but no link reaches them: outside
Development the server logs only that a reset email was asked for, never the link. So a forgotten
password cannot be reset by email; on the server's machine `--reset-password` sets a new one
([Users from the command line](#users-from-the-command-line)). In Development the link is written to
the log instead. Reset links
wait in an outbox in memory that holds 100: when it is full a new link is dropped with a warning,
links still waiting are lost on a restart, and a failed send is not retried
([Users](ByteShelf.Server/Users/Users.md#password-reset)).

## Logging

The server logs to the console. `appsettings.json` beside the executable sets the levels: Information
for the server's own messages, Warning for ASP.NET Core's (`Microsoft.AspNetCore`). Keep ASP.NET
Core at Warning or above: at Information it logs every request's URL, and download links and reset
links carry their secret token in the URL.

## Rate limits

Every request counts against limits per caller and per minute. The caller is the signed-in user, or
the shelf key, or else the client's address, which behind a proxy needs `BYTESHELF_TRUSTED_PROXIES`.

| Tier | Requests per minute | Applies to |
|---|---|---|
| Global | 1,200 | every request, beside any tier below |
| Strict | 60 | signing in and renewing a session together (`/api/auth/login`, `/api/auth/refresh`); changing your own password |
| Very strict | 10 | asking for a password reset and completing one; a replica accepting a pairing code |

A request past a limit gets 429 with an error message, and the window starts over each minute.
Separately, failed sign-ins from one address are slowed down step by step until one succeeds, and
one user may hold 10 live-update streams at once. A replica's feed calls, which carry its
credential, are outside the limits.

## Behind a reverse proxy

- Set `BYTESHELF_TRUSTED_PROXIES` to the proxy's address.
- Allow large request bodies (single uploads have no size limit of their own; chunks of a resumable
  upload are at most 64 MiB). For nginx: `client_max_body_size 0;`.
- Do not buffer requests or responses, so uploads stream to disk and the live-update stream stays
  open. For nginx: `proxy_request_buffering off; proxy_buffering off;`. The live-update stream also
  sends `X-Accel-Buffering: no`.
- Pass `Authorization` through for `/mcp` and `/api` (nginx does by default): MCP clients send their
  shelf key there.
- Serve over HTTPS, at the server's own paths (no path prefix the proxy strips): the web UI opens and
  saves files under `/api/open` with the session's token in a `Secure` cookie scoped to that path,
  which a browser keeps only over HTTPS. Over plain HTTP it falls back to reading each file whole into
  the page, which works but holds large files in memory. Do not log request or response headers at
  the proxy: that cookie carries the token, as `Authorization` does.
- Allow long request lines: a path of up to 4096 characters travels in the query string,
  percent-encoded, and the server accepts lines of about 98 KB. For nginx:
  `large_client_header_buffers 4 128k;` (its default of 8k answers 414 for a long path).

## MCP

`/mcp` serves agents with a shelf key and needs no setting. Its tools call the server's own API at
the first plain-HTTP address Kestrel is bound to (from `ASPNETCORE_URLS`, `--urls` or Kestrel's own
settings), as bound: a server bound to a single interface's address calls itself there, and one on
all interfaces, such as `http://+:5000`, at `127.0.0.1`. So the server must listen on plain HTTP
somewhere; one on HTTPS or a Unix socket alone cannot run them. Those calls count against the key's
rate limit beside the MCP request itself.

## Replicas

A replica is a second server, installed the same way with a root of its own, that keeps a whole copy
of the primary and follows it ([Replication](ByteShelf.Server/Replication/Replication.md)). It needs to
reach the primary's `APP_BASE_URL`, which must be the address the primary answers at (the replica
follows no redirects); nothing needs to reach the replica but its own admins and readers.

1. Start the replica on a new, empty root with its own first admin (step 5 of
   [Installing](#installing)), and create no shelves on it. Not
   a copy of the primary's root: that would be the same instance, and pairing would be refused.
2. On the primary, as an admin, on the Replication page, make a pairing code. It works once, for
   15 minutes.
3. On the replica, as its admin, on the Replication page, choose Follow a primary, paste the code
   and give the replica a name, which the primary lists it by. The replica copies the primary, its
   users included: from then on its admins sign in with their accounts on the primary.

The same page shows on the primary each replica and how far behind it is, and on a replica the
primary it follows and the last error (`GET /api/replication` answers the same). A replica only reads.
A file or directory purged on the primary stays in the replica's trash until the replica's own restore
window has passed since it was deleted. The exception is a file's old contents when it is encrypted or
decrypted in place: they leave the replica as soon as it applies that change, as they left the primary.

To make a replica the primary, promote it on its Replication page, then point clients and
`APP_BASE_URL` at it. Stop the old primary: other replicas keep following it, and pairing them with the
new one needs servers without shelves, in practice new roots.

Every step above can be done without the web UI. An admin signed in with the `byteshelf` CLI runs
`byteshelf replication` (the status), `replication code`, `replication follow <code> --name <name>`,
`replication forget <replica>` and `replication promote`. On the server's own machine, with no
login, as the user the server runs as and with `BYTESHELF_ROOT` set, the server's executable does the
same to its root, with the server running or stopped:

```
ByteShelf.Server --replication-status
ByteShelf.Server --replication-code
ByteShelf.Server --follow <code> --name "<this server's name on the primary>"
ByteShelf.Server --forget <replica id or name>
ByteShelf.Server --promote
```

`--replication-code` also needs `APP_BASE_URL`, which the code names. These go through the same
rules as the API: a code works once for 15 minutes and not on a replica, and only a server without
shelves or replicas of its own can follow. A running server starts following within 2 seconds of
`--follow`; after `--promote` it applies nothing more from the old primary, even changes it had
already fetched. Like the user commands they never migrate an existing database. On a root with no
database yet, `--follow` creates the root and its database first, so a replica can be paired before
its first start (run it as the service user, as for `--create-user`), and removes them again if the
pairing fails; the others refuse such a root. Exit codes: 0 done, 1 refused or failed (the reason is
logged), 2 the arguments are wrong, or `BYTESHELF_ROOT` (or, for `--replication-code`,
`APP_BASE_URL`) is not set.

## Backups

While the server runs, with the same `BYTESHELF_ROOT` (the only variable it reads):

```bash
BYTESHELF_ROOT=/srv/byteshelf ./ByteShelf.Server --backup /backups/byteshelf-2026-03-01.zip
```

This writes a consistent zip ([Backup](ByteShelf.Server/Backup/Backup.md)): the database as of one
moment, the objects its files point at, and `keys/`. Unzipped, it is a root a server starts from.
The zip is readable by its owner only, since it holds password hashes and the server's secrets. It
must be written outside the root, to a folder with room for both the zip and a copy of the database,
which is written beside it while the backup runs. Exit code 0 means it was written; 1 that it failed
and wrote nothing, such as for a damaged object or a root without a database; 2 that the zip's path
after `--backup` is not given or `BYTESHELF_ROOT` is not set.

A zip of a stopped server's root is a complete copy too. Restoring either onto a primary makes its
replicas take a whole copy once they notice, which [Backup](ByteShelf.Server/Backup/Backup.md) explains.

## Background work

The server cleans up after itself: hourly it removes abandoned uploads, staging leftovers, the trash
past its restore window, and objects no file points at; daily it deletes expired sign-in and reset
tokens and compacts the change log (entries older than 30 days that something newer replaced, and
removals that old). A failed pass is logged as an error and the next one runs on schedule. A replica
also follows its primary continuously, every two seconds when caught up.
