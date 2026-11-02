# Storage

Shelves, the files and directories on them, the bytes behind them, uploads, the trash and the
cleanup. Shelves themselves are described in [Shelves.md](Shelves.md).

## Where things are

- **Objects** (`ObjectStore`): every distinct content is one plain file at
  `objects/ab/cd/<sha256>`, written once and never modified. `sha256sum` verifies any of them,
  and identical contents are stored once however many files point at them.
- **Staging** (`StagingArea`): the bytes of an upload in progress, at `staging/<upload id>`, and on
  a replica the bytes of a file being fetched from the primary until their hash is checked. It is in
  the root next to `objects/`, so placing an object is a rename.
- **Rows**: `directory`, `file` and `upload` in the database. A file row points at its object by
  hash. Files and directories share one namespace per directory. Names are stored in Unicode
  normalization form C.

## Endpoints

All under `/api/shelves/{shelf}`, open to whoever may do the operation there; what each endpoint
needs is in the table in [Access.md](../Access/Access.md). An existing file or directory is named by
exactly one of the `path` and `id` query parameters. A new path is in the JSON body for
`POST directories`, `POST uploads`, `POST upload-link` and the `toPath` of a move, and in the `path`
query parameter for `PUT content`. Errors answer an `ErrorResponse` with a `code` from `ByteShelfCommon.ErrorCodes`; a
path that is not allowed is a 400.

| Method and route | What it does |
|---|---|
| `GET entries?path=` | Lists a directory (the root without `path` or `id`). Each of its `directories` is the `directory` with the `sizeBytes`, `fileCount` and `encryptedFileCount` of the live files under it at any depth (`ListedDirectory`), summed in the same query (a recursive walk per child), so listing the root walks the whole shelf. `encryptedBy` is the encrypted directory it is or is inside, null for none. |
| `GET tree?under=&depth=&limit=&after=` | One page of the live directories and files under `under` (the root by default), at any depth or down to `depth` levels (1: what it holds directly), together by path in code point order: `limit` (1 to 1000, 100 by default) entries, and `nextCursor` to pass as `after` for the next page. 400 for a depth below 1 or a limit out of range, 404 when `under` is missing or names a file. |
| `GET files?path=` | A file's record. |
| `DELETE files?path=` | Moves a file to the trash; answers the deletion. |
| `POST files/move?path=` | Moves or renames a file to the body's `toPath`; missing directories above it are created. 400 for `/`, 409 when the path is taken. |
| `PUT files/description?path=` | Sets the body's `description`, or removes it when null or blank; 400 over `FileDescription.MaxLength` characters. |
| `GET search?under=&text=&…` | One page of the live files under `under` (the root by default) that match every criterion given: `text`, `contentType`, `minSizeBytes`, `maxSizeBytes`, `createdFrom`, `createdBefore`, `updatedFrom`, `updatedBefore`, `sha256`, `limit` (1 to 1000, 100 by default) and `after` (the previous page's `nextCursor`). Sizes are inclusive; a time range includes its start and excludes its end. 400 for a criterion `FileSearch.Normalize` refuses, 404 when `under` is missing or names a file. See "Descriptions and search" below. |
| `POST directories` | Creates the directory at the body's `path` and any missing one above it; an existing one is answered as it is. |
| `POST directories/move?path=` | Moves or renames a directory with everything in it to the body's `toPath`. 400 for the root, a destination of `/` or inside itself, or one that would make a path under it longer than `ShelfPath.MaxPathLength`; 409 when the path is taken. |
| `DELETE directories?path=` | Moves a directory and everything live under it to the trash as one deletion; 400 for the root. |
| `GET directories/encryption?path=` | The encrypted directory a file at `path` (existing or not) would be under, with its `check`; `encryptedBy` is null for none. See "Encryption" below. |
| `PUT directories/encryption?path=` | Encrypts a directory with the body's `check` (base64 of an age file of at most `FileEncryption.MaxCheckBytes`); 400 for a check that is not one, 409 when it is encrypted already or inside or above an encrypted directory. |
| `DELETE directories/encryption?path=` | Stops encrypting a directory; 409 when it is not encrypted itself. |
| `GET content?path=` (and `HEAD`) | Streams the contents (`HEAD`: only the headers); `Range`, `If-None-Match` and `If-Range` work, the ETag is the SHA-256. Served with `X-Content-Type-Options: nosniff`, `Cache-Control: private` and, except for a PDF (Chrome's viewer does not run sandboxed), a sandboxing `Content-Security-Policy`. `download=true` adds a file name. Also served at `/api/open/{shelf}/content`, where a signed-in browser's token may come in a cookie (see "Opening in a browser" below). |
| `POST download-link?path=&expiresInMinutes=` | A link to the contents that works without a token for `expiresInMinutes` (1 to 10080, 1440 by default), to share the file (see below). Making one is a read; 400 for a lifetime outside those bounds, 409 for an encrypted file. |
| `PUT content?path=&overwrite=&encrypted=` | Stores the body in one request. Needs `Content-Length` (411 without); the `Content-Type` is stored, or without one the type the path's extension names (`ContentTypes.ForUpload`), so `curl -T` stores a PDF as one. `encrypted=true` marks the body as an age file (400 when it does not start as one). 409 when the path is taken and `overwrite` is not set, or for a plain file under an encrypted directory; 507 over quota. |
| `POST upload-link` | A link that stores one file sent to it without a token, from the body's `path`, `overwrite`, `description` and `expiresInMinutes` (1 to 1440, 15 by default); see "Upload links" below. Needs `Add`, or `Write` with `overwrite`; 400 for the root or a request `CreateUploadLinkRequest.Normalize` refuses, 409 for a path under an encrypted directory or over an encrypted file. |
| `POST uploads` | Starts a resumable upload from the body's `path`, `sizeBytes`, `contentType` (optional, typed as `PUT content` is), `overwrite` (false by default), `encrypted` (as on `PUT content`, checked when it completes) and the file's `description` if it has one (a single `PUT` has no body to carry one); reserves the size against the quota. |
| `PUT content/swap?id=&sha256=&encrypted=` | Swaps the contents of the file `id` in place for the body, in one request (`Content-Length` needed): encrypting a plain file (`encrypted=true`, an age file) or decrypting an encrypted one. Needs write access. 400 when sent as encrypted but not an age file; 404 for no such live file; 409 when its contents are no longer `sha256`, when the swap would not flip its encryption, or for a plain file under an encrypted directory; 411 without `Content-Length`; 507 when what it adds to the file's size does not fit the quota. See "Encryption" below. |
| `POST uploads/swap` | Starts a resumable upload that swaps a file's contents when it completes, from the body's `fileId`, `sha256`, `sizeBytes` and `encrypted`, refused as `PUT content/swap` is. Its chunks, progress, completion and abort are an upload's. |
| `GET uploads/{id}` | Progress: `receivedBytes` is where the next chunk starts. |
| `PUT uploads/{id}?offset=` | Appends a chunk of at most `UploadLimits.MaxChunkBytes` (64 MiB, 413 above); 409 when `offset` is not `receivedBytes`. |
| `POST uploads/{id}/complete` | Stores the file once every byte has arrived. 409 when the path was taken meanwhile, or for a swap when the file's contents changed or the swap no longer flips its encryption; 404 for a swap whose file is gone. The upload is kept. |
| `DELETE uploads/{id}` | Drops the upload and releases its reservation. |
| `GET trash` | Deletions still inside the restore window, newest first. |
| `POST trash/{deletionId}/restore` | Puts a deletion back where it was. |
| `DELETE trash` | Empties the trash: everything in it can no longer be restored, and is purged now with its objects (see below). |

## Opening in a browser (`AuthSetup.CookieTokenPath`)

A new tab or a save is a plain navigation, which cannot send an `Authorization` header. So the web UI
opens and saves a file at `/api/open/{shelf}/content`, the one path where the session's access token
may instead come in the `__Secure-access_token` cookie (EasyReasy.Auth's `CookieTokenPaths`; its README's
"Opening Files in a New Tab (token in a cookie)"). The built-in sign-in and renewal set the cookie, `HttpOnly`, `Secure`,
`SameSite=Strict`, host-only, with `Path=/api/open` and the token's own lifetime; a failed renewal
and signing out clear it. It is read only on `GET` and `HEAD`, only without an `Authorization`
header, and not when the request carries the cookie twice, so a copied URL is useless to anyone else and no public link
is made. A link to it followed from another site arrives without the cookie and answers 401.
Everything under `/api/open` only reads. A browser keeps a `Secure` cookie only over HTTPS or on
localhost; elsewhere the web UI reads the contents with the header instead (see its README).

## Download links (`DownloadLinks`)

A link shares a file with someone who has no access, for the caller's pick of lifetime: a day by
default, at most 7 days (`DownloadLink.Lifetime.Normalize`). `GET` or `HEAD /api/downloads/{token}` serves the contents as `GET content`
does, `download=true` included, to anyone holding the token until it runs out. The token names the
shelf and the file by id and when it runs out, signed by `LinkTokens` (below);
an altered or expired one, or one whose file is in the trash, answers 404; a restored file keeps its
id, so its unexpired links work again. A file stored over the linked one is a new file, so the link no
longer reaches it; one encrypted or decrypted in place keeps its id, so the link serves its contents
as they are then, and answers 409 while it is encrypted. Creating or serving a link is recorded nowhere. A link keeps working until it
runs out even if the access it was made with is taken away meanwhile; nothing takes one link back,
but removing the secret file and restarting ends them all. `DownloadLinks` makes the link with
`PublicAddress.LinkTo`: absolute on `APP_BASE_URL` when that is set, which clients keep as it is;
otherwise (Development only) a path from the server's root, which each client makes absolute against
the address it reaches the server by.

## Upload links (`UploadLinks`)

A link that lets someone without access, or a program without the key, store one file: an agent
hands it on, or uses it to move a file too large for its own context. `PUT /api/upload-links/{token}`
(`UploadLinksController`) stores the body as a single `PUT content` by the link's maker would, with
`Content-Length` reserved against the quota first, typed the same way, and with the description the
link carries. The token carries what the link grants (`UploadLinkGrant`): the shelf by id, who made it
(a key or a user, by id), the path, whether it may overwrite and the description, so nothing is
stored when one is made. The token is signed, not encrypted: whoever holds the link can read the path
and description in it. Since the URL carries the path and the description, together they may take
at most 4096 bytes of UTF-8 (`NormalizedUploadLinkRequest.MaxCarriedBytes`), which keeps a link under the
8 KiB request line common proxies accept.

Unlike a download link, the maker's access is checked again on every use: the key or user is looked
up (`ShelfCallers`) and asked `ShelfAccessResolver` as for any request, so a deleted key or a user
whose grant was taken away or lowered stops its links at once (404 when the maker is gone or has no
access, 403 when it may no longer store there). A link that may not overwrite is refused (409) once
its path is taken, so it stores at most one file; one that may overwrite replaces the file on every
use until it runs out, 15 minutes by default and at most 24 hours (`UploadLink.Lifetime.Normalize`).
A replica refuses the `PUT`, as it refuses every write. A link sends a plain file, so it is refused
(409) under an encrypted directory or over an encrypted file, when it is made and again when it is
used, so a directory encrypted after the link was made stops it too.

## Link tokens (`Shared/Auth/LinkTokens`)

Download and upload links are both a token after their route: the time the link runs out, what it
grants, and an HMAC-SHA256 over both. One secret, `keys/link-secret`, signs them all, through a key
derived from it for each kind of link (`HMAC(secret, kind)`), so a download link's token never reads
as an upload link's. An altered or expired token answers 404. Removing the secret and restarting ends
every link at once.

## Uploads (`UploadService`)

1. Start: the path, content type and description are checked and the whole declared size is
   reserved against the shelf's quota, in one write transaction (SQLite has one writer at a time, so two uploads can
   never both take the same free space). A single `PUT` uses `Content-Length` as the size. An upload
   either stores a new file at a path or swaps a file's contents (`UploadTarget`: `StoreAtPath` or
   `ContentSwap`, held apart by the `upload` table's check); a swap has no path, overwrite or
   description, and reserves only what it adds to its file's size, since finishing replaces one with
   the other (`ShelfRepository`'s usage query counts it so). The
   upload records who started it (`started_by`), the only caller besides read-write ones who may use
   it afterwards (see [Access.md](../Access/Access.md)).
2. Bytes go to staging at the offset the upload has reached; anything past the declared size is
   refused as it arrives.
3. Finish: the server hashes the staged bytes itself, then in one transaction creates missing
   directories, moves an overwritten file to the trash, inserts the file row and removes the upload.
   The object is placed just before the commit. A swap instead checks its file again as it stands,
   points the row at the new object, and removes the old object once that commits (see
   "Encryption"); a removal that fails is logged and left to maintenance, as the swap stands.

The client never supplies a hash the server trusts.

## Deletes, restores and the purge (`TrashService`)

A delete marks rows with a `deletion_id` and `deleted_at`; a deletion of a directory marks its
whole live subtree with one id, which is what a restore brings back. Anything under it already in
the trash keeps its own deletion. A restore is refused when the name has been taken since or the
containing directory is itself in the trash. Deletions stay listed and restorable for the restore
window (`BYTESHELF_RESTORE_WINDOW_DAYS`, 30 by default) and count against the quota until the purge
removes them; one past its window is neither listed nor restorable, even if the purge has not
reached all of it yet. Emptying a shelf's trash first marks everything in it as past its window, in
one transaction, so none of it can be listed or restored from then on, and then purges it in batches.
What a crash or a cancelled request leaves unpurged is already past its window, so the next
maintenance pass purges it. A deletion made while emptying runs is kept.

## Descriptions and search (`FileService`)

A file has at most one description, free text of up to 2,000 characters (code points, as SQLite's
`length()` counts them), trimmed, with blank meaning none. It is part of the file's row, so the change
log carries it to replicas. Setting it moves the file's `updatedAt` and is logged as an update;
setting the text it already has changes and logs nothing. A file stored over another keeps the
other's description unless its upload brings one: a new version of a file is still that file.

Search runs in one query: a recursive walk from the `under` directory builds each live file's full
path, and the criteria of `ByteShelfCommon.FileSearch` filter the files. `text` matches the name or
the description; both sides are folded by `FileSearch.Fold` (normalization form C, invariant
lowercase). SQLite's own `lower()` folds ASCII only, so the file row keeps folded copies
(`name_fold`, `description_fold`) that `FileRepository` writes from the name and description on
every write. `contentType` compares the type without its parameters, kept the same way in
`media_type` by `ContentTypes.MediaTypeOf`, and `type/*` matches the whole family.
Results come by full path; a page asks for one row more than it returns, to know whether another
follows, and its `nextCursor` is its last path, which the next page starts after. The in-memory
client applies the same rules, and the client contract tests run both.

## Trees (`DirectoryService.ListTreeAsync`)

A tree page walks the directories under `under` as search does, carrying each one's level so a
depth stops the walk, and asks for one row past the page of directories and of files apart: the first
`limit` + 1 entries of both together are among the first `limit` + 1 of their own kind.
`DirectoryTree.Page` in Common merges them by path and sets the cursor, for the in-memory client too.
A directory's path sorts before those of what it holds, so a client saving a tree page by page meets each
directory before its files.

## Encryption (`ShelfEncryption`)

Files are encrypted with a password in the clients, never on the server: the password and the
plaintext never reach it. An encrypted file's contents are an age file (c2sp.org/age) with a scrypt
recipient, which `age -d` opens anywhere; only the contents are encrypted, so names, sizes (of the
encrypted bytes), directories and descriptions stay as they are and search keeps finding them, and
the content type is the decrypted file's. A file is stored as encrypted with `encrypted=true` on
`PUT content` or in `POST uploads`, and the server checks it starts as an age file (400 otherwise);
it cannot check the password. The flag is on the file row and its change log entry, so replicas
carry it.

A directory is encrypted with a `check`: an age file of `FileEncryption.CheckText` encrypted with its
password, which a client decrypts to tell a wrong password before it encrypts anything with it. It
reveals nothing the encrypted files do not. From then on one rule holds, in `ShelfEncryption` and
mirrored by the in-memory client: no new plain file comes under an encrypted directory, and its
subtree has one password. Plain files already there stay plain until a client encrypts them. So a
plain upload under one is refused (409), whether the directory is the file's
own or an ancestor, existing directories or not; a directory is never encrypted inside or above
another; nothing moves into one from outside it, encrypted or not, since its password could differ
(moving out is fine, and the file stays encrypted); a deletion holding a plain file or an encrypted
directory is not restored into one; and upload links, which send plain files, are refused under one
or over an encrypted file, when made, when used, and again as the upload finishes. A download link is
never made to an encrypted file, nor serves one encrypted in place since. Encrypting a directory touches none of its files: a client encrypts them after, and
removing its encryption decrypts nothing, so a client decrypts them after; the server only records
the mark.

A client encrypts or decrypts a stored file by swapping its contents in place (`PUT content/swap`,
`POST uploads/swap`): the file keeps its id, path, type and description, and its old contents are
removed at once rather than moved to the trash, unless another file holds the same ones. This is the
one write that destroys contents without the trash, and it is allowed only for what the person
confirmed cannot be undone: it must flip the file's encryption, and it must name the SHA-256 the
contents still have, so a change made since they were read is never lost (409 otherwise). Its change
log entry (`swapped`) points the row at the new contents, and a replica removes the old ones as soon
as it applies it, the one exception to replicas keeping what the primary drops for their own restore
window ([Replication](../Replication/Replication.md)). A backup taken before still holds them.

## Moves (`FileService`, `DirectoryService`)

A move or rename changes only the moved row: everything under a moved directory follows it, and no
object is touched. Missing directories above the destination are created, and nothing is ever
overwritten. A directory move is refused when any path under it, the trash included, would grow
longer than `ShelfPath.MaxPathLength`, since such a path could no longer be named.

## Removing objects safely (`ObjectCollector`)

An object is removed only when no row, live or trashed, points at it. Both sides of that question
hold the object's lock (`ObjectStore.LockAsync`): a finishing upload from placing the object until
its row commits, a removal from checking for rows until the file is gone. A replica's sync places
objects for rows it commits later, after a whole page has arrived; it holds `MaintenanceGate` for the
whole step instead, which keeps maintenance, and so every removal, away until those rows commit
([Replication](../Replication/Replication.md)).

## Maintenance (`StorageMaintenanceWorker`)

At startup and then hourly, as the job `storage.maintenance` ([Jobs](../Jobs/Jobs.md)): removes uploads idle for
24 hours, single-`PUT` uploads started before the process (their request did not survive the
restart), staging entries with no upload, the trash past its window, and objects no row points at.
A pass holds `MaintenanceGate` throughout, so on a replica it waits for a sync step (a whole copy
included) to finish, and the next step waits for it.

## Change log

Every change to a shelf, directory or file appends a `change_log` row in the same transaction; see
[ChangeLog.md](../ChangeLog/ChangeLog.md).
