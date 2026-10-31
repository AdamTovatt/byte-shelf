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
signs sign-ins, `download-link-secret` signs the web UI's short-lived download links. They travel
with the root, so a copied or restored root keeps its sign-ins and links.

## Behind a reverse proxy

- Set `BYTESHELF_TRUSTED_PROXIES` to the proxy's address.
- Allow large request bodies (single uploads have no size limit of their own; chunks of a resumable
  upload are at most 64 MiB). For nginx: `client_max_body_size 0;`.
- Do not buffer requests or responses, so uploads stream to disk and the live-update stream stays
  open. For nginx: `proxy_request_buffering off; proxy_buffering off;`. The live-update stream also
  sends `X-Accel-Buffering: no`.

## Backups

Stop the server and zip the root folder; the zip is a complete copy.

## Background work

The server cleans up after itself: hourly it removes abandoned uploads, staging leftovers, the trash
past its restore window, and objects no file points at; daily it deletes expired sign-in and reset
tokens. Each pass is recorded in the `job_run` table.
