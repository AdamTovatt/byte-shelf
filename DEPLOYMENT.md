# Deploying ByteShelf

ByteShelf is installed and updated with updaemon: `dotnet publish ByteShelf.Server` produces the
executable with its `updaemon.json`, which names it, and the web UI's build in `wwwroot/`. Publishing
builds the web UI, so it needs Node.js and npm. The running server needs one writable folder, the
root, and nothing else on disk.

## Environment variables

| Variable | Required | Meaning |
|---|---|---|
| `BYTESHELF_ROOT` | yes | The root folder. Created on first start, with `objects/`, `staging/` and `keys/` inside. |
| `APP_BASE_URL` | outside Development | The public URL, e.g. `https://shelf.example.com`. Password-reset links point at it. |
| `INITIAL_ADMIN_EMAIL`, `INITIAL_ADMIN_PASSWORD` | first start | Create the first admin while there are no users; ignored afterwards and can be removed. |
| `INITIAL_ADMIN_DISPLAY_NAME` | no | The first admin's display name; the email's local part otherwise. |
| `BYTESHELF_RESTORE_WINDOW_DAYS` | no | How long a deletion can be restored before it is purged. 30 by default. |
| `BYTESHELF_TRUSTED_PROXIES` | behind a proxy | Comma-separated IPs or CIDR ranges of the reverse proxies in front of the server. Their `X-Forwarded-For` and `X-Forwarded-Proto` are believed, so rate limits and the sign-in delay count each client rather than the proxy. Loopback is always trusted. |
| `ASPNETCORE_URLS` | no | Where Kestrel listens. |

Two secrets are generated into `keys/` on first start, readable by the owner only: `jwt-secret`
signs sign-ins, `download-link-secret` signs download links: the web UI's five-minute ones and the
share links any key or user with read access can make for up to 7 days. They travel with the root,
so a copied or restored root keeps its sign-ins and its unexpired links, share links included. A
single link cannot be taken back; deleting `download-link-secret` and restarting ends every link at
once, and a new secret is generated.

## Behind a reverse proxy

- Set `BYTESHELF_TRUSTED_PROXIES` to the proxy's address.
- Allow large request bodies (single uploads have no size limit of their own; chunks of a resumable
  upload are at most 64 MiB). For nginx: `client_max_body_size 0;`.
- Do not buffer requests or responses, so uploads stream to disk and the live-update stream stays
  open. For nginx: `proxy_request_buffering off; proxy_buffering off;`. The live-update stream also
  sends `X-Accel-Buffering: no`.

## Replicas

A replica is a second server, installed the same way with a root of its own, that keeps a whole copy
of the primary and follows it ([Replication](ByteShelf.Server/Replication/Replication.md)). It needs to
reach the primary's `APP_BASE_URL`, which must be the address the primary answers at (the replica
follows no redirects); nothing needs to reach the replica but its own admins and readers.

1. Start the replica on a new, empty root with its own initial admin, and create no shelves on it. Not
   a copy of the primary's root: that would be the same instance, and pairing would be refused.
2. On the primary, as an admin, on the Replication page, make a pairing code. It works once, for
   15 minutes.
3. On the replica, as its admin, on the Replication page, choose Follow a primary, paste the code
   and give the replica a name, which the primary lists it by. The replica copies the primary, its
   users included: from then on its admins sign in with their accounts on the primary.

The same page shows on the primary each replica and how far behind it is, and on a replica the
primary it follows and the last error (`GET /api/replication` answers the same). A replica only reads.
A file or directory purged on the primary stays in the replica's trash until the replica's own restore
window has passed since it was deleted.

To make a replica the primary, promote it on its Replication page, then point clients and
`APP_BASE_URL` at it. Stop the old primary: other replicas keep following it, and pairing them with the
new one needs servers without shelves, in practice new roots.

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
and wrote nothing, such as for a damaged object; 2 that the zip's path after `--backup` or
`BYTESHELF_ROOT` is missing.

A zip of a stopped server's root is a complete copy too. Restoring either onto a primary makes its
replicas take a whole copy once they notice, which [Backup](ByteShelf.Server/Backup/Backup.md) explains.

## Background work

The server cleans up after itself: hourly it removes abandoned uploads, staging leftovers, the trash
past its restore window, and objects no file points at; daily it deletes expired sign-in and reset
tokens and compacts the change log (entries older than 30 days that something newer replaced, and
removals that old). Each
pass is recorded in the `job_run` table. A replica also follows its primary continuously, every two
seconds when caught up.
