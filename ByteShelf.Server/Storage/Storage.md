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
`POST directories`, `POST uploads` and the `toPath` of a move, and in the `path` query parameter for
`PUT content`. Errors answer an `ErrorResponse` with a `code` from `ByteShelfCommon.ErrorCodes`; a
path that is not allowed is a 400.

| Method and route | What it does |
|---|---|
| `GET entries?path=` | Lists a directory (the root without `path` or `id`). |
| `GET tree?under=&depth=&limit=&after=` | One page of the live directories and files under `under` (the root by default), at any depth or down to `depth` levels (1: what it holds directly), together by path in code point order: `limit` (1 to 1000, 100 by default) entries, and `nextCursor` to pass as `after` for the next page. 400 for a depth below 1 or a limit out of range, 404 when `under` is missing or names a file. |
| `GET files?path=` | A file's record. |
| `DELETE files?path=` | Moves a file to the trash; answers the deletion. |
| `POST files/move?path=` | Moves or renames a file to the body's `toPath`; missing directories above it are created. 400 for `/`, 409 when the path is taken. |
| `PUT files/description?path=` | Sets the body's `description`, or removes it when null or blank; 400 over `FileDescription.MaxLength` characters. |
| `GET search?under=&text=&…` | One page of the live files under `under` (the root by default) that match every criterion given: `text`, `contentType`, `minSizeBytes`, `maxSizeBytes`, `createdFrom`, `createdBefore`, `updatedFrom`, `updatedBefore`, `sha256`, `limit` (1 to 1000, 100 by default) and `after` (the previous page's `nextCursor`). Sizes are inclusive; a time range includes its start and excludes its end. 400 for a criterion `FileSearch.Normalize` refuses, 404 when `under` is missing or names a file. See "Descriptions and search" below. |
| `POST directories` | Creates the directory at the body's `path` and any missing one above it; an existing one is answered as it is. |
| `POST directories/move?path=` | Moves or renames a directory with everything in it to the body's `toPath`. 400 for the root, a destination of `/` or inside itself, or one that would make a path under it longer than `ShelfPath.MaxPathLength`; 409 when the path is taken. |
| `DELETE directories?path=` | Moves a directory and everything live under it to the trash as one deletion; 400 for the root. |
| `GET content?path=` (and `HEAD`) | Streams the contents (`HEAD`: only the headers); `Range`, `If-None-Match` and `If-Range` work, the ETag is the SHA-256. Served with `X-Content-Type-Options: nosniff` and, except for a PDF (Chrome's viewer does not run sandboxed), a sandboxing `Content-Security-Policy`. `download=true` adds a file name. |
| `POST download-link?path=&expiresInMinutes=` | A link to the contents that works without a token for `expiresInMinutes` (1 to 10080, 1440 by default), to open, save or share the file (see below). Making one is a read; 400 for a lifetime outside those bounds. |
| `PUT content?path=&overwrite=` | Stores the body in one request. Needs `Content-Length` (411 without); the `Content-Type` is stored. 409 when the path is taken and `overwrite` is not set, 507 over quota. |
| `POST uploads` | Starts a resumable upload from the body's `path`, `sizeBytes`, `contentType` (optional), `overwrite` (false by default) and the file's `description` if it has one (a single `PUT` has no body to carry one); reserves the size against the quota. |
| `GET uploads/{id}` | Progress: `receivedBytes` is where the next chunk starts. |
| `PUT uploads/{id}?offset=` | Appends a chunk of at most `UploadLimits.MaxChunkBytes` (64 MiB, 413 above); 409 when `offset` is not `receivedBytes`. |
| `POST uploads/{id}/complete` | Stores the file once every byte has arrived. 409 when the path was taken meanwhile; the upload is kept. |
| `DELETE uploads/{id}` | Drops the upload and releases its reservation. |
| `GET trash` | Deletions still inside the restore window, newest first. |
| `POST trash/{deletionId}/restore` | Puts a deletion back where it was. |
| `DELETE trash` | Empties the trash: everything in it can no longer be restored, and is purged now with its objects (see below). |

## Download links (`DownloadLinks`)

One mechanism serves two uses. A plain link or an image's source cannot carry a token, so the web
UI asks for a five-minute link before it opens or saves a file; and a link shares a file with someone
who has no access, for the caller's pick of lifetime: a day by default, at most 7 days
(`DownloadLink.NormalizeLifetime`). `GET` or `HEAD /api/downloads/{token}` serves the contents as `GET content`
does, `download=true` included, to anyone holding the token until it runs out. The token names the
shelf and the file by id and when it runs out, signed with HMAC-SHA256 by `keys/download-link-secret`;
an altered or expired one, or one whose file is in the trash, answers 404; a restored file keeps its
id, so its unexpired links work again. A file stored over the linked one is a new file, so the link no
longer reaches it. Creating or serving a link is recorded nowhere. A link keeps working until it
runs out even if the access it was made with is taken away meanwhile; nothing takes one link back,
but removing the secret file and restarting ends them all. The server answers the link as a path
from its own root, and the clients make it absolute against the address they reach it by.

## Uploads (`UploadService`)

1. Start: the path, content type and description are checked and the whole declared size is
   reserved against the shelf's quota, in one write transaction (SQLite has one writer at a time, so two uploads can
   never both take the same free space). A single `PUT` uses `Content-Length` as the size. The
   upload records who started it (`started_by`), the only caller besides read-write ones who may use
   it afterwards (see [Access.md](../Access/Access.md)).
2. Bytes go to staging at the offset the upload has reached; anything past the declared size is
   refused as it arrives.
3. Finish: the server hashes the staged bytes itself, then in one transaction creates missing
   directories, moves an overwritten file to the trash, inserts the file row and removes the upload.
   The object is placed just before the commit.

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
