# Storage

Shelves, the files and directories on them, the bytes behind them, uploads, the trash and the
cleanup. Shelves themselves are described in [Shelves.md](Shelves.md).

## Where things are

- **Objects** (`ObjectStore`): every distinct content is one plain file at
  `objects/ab/cd/<sha256>`, written once and never modified. `sha256sum` verifies any of them,
  and identical contents are stored once however many files point at them.
- **Staging** (`StagingArea`): the bytes of an upload in progress, at `staging/<upload id>`. It is
  in the root next to `objects/`, so finishing an upload is a rename.
- **Rows**: `directory`, `file` and `upload` in the database. A file row points at its object by
  hash. Files and directories share one namespace per directory. Names are stored in Unicode
  normalization form C.

## Endpoints

All under `/api/shelves/{shelf}`, admin only. A file or directory is named by exactly one of the
`path` and `id` query parameters. Errors answer an `ErrorResponse` with a `code` from
`ByteShelfCommon.ErrorCodes`.

| Method and route | What it does |
|---|---|
| `GET entries?path=` | Lists a directory (the root without `path` or `id`). |
| `GET files?path=` | A file's record. |
| `DELETE files?path=` | Moves a file to the trash; answers the deletion. |
| `GET content?path=` | Streams the contents; `Range`, `If-None-Match` and `If-Range` work, the ETag is the SHA-256. Served with `X-Content-Type-Options: nosniff` and a sandboxing `Content-Security-Policy`. `download=true` adds a file name. |
| `PUT content?path=&overwrite=` | Stores the body in one request. Needs `Content-Length` (411 without); the `Content-Type` is stored. 409 when the path is taken and `overwrite` is not set, 507 over quota. |
| `POST uploads` | Starts a resumable upload with its size; reserves it against the quota. |
| `GET uploads/{id}` | Progress: `receivedBytes` is where the next chunk starts. |
| `PUT uploads/{id}?offset=` | Appends a chunk of at most `UploadLimits.MaxChunkBytes` (64 MiB, 413 above); 409 when `offset` is not `receivedBytes`. |
| `POST uploads/{id}/complete` | Stores the file once every byte has arrived. 409 when the path was taken meanwhile; the upload is kept. |
| `DELETE uploads/{id}` | Drops the upload and releases its reservation. |
| `GET trash` | Deletions still inside the restore window, newest first. |
| `POST trash/{deletionId}/restore` | Puts a deletion back where it was. |

## Uploads (`UploadService`)

1. Start: the path and content type are checked and the whole declared size is reserved against
   the shelf's quota, in one write transaction (SQLite has one writer at a time, so two uploads can
   never both take the same free space). A single `PUT` uses `Content-Length` as the size.
2. Bytes go to staging at the offset the upload has reached; anything past the declared size is
   refused as it arrives.
3. Finish: the server hashes the staged bytes itself, then in one transaction creates missing
   directories, moves an overwritten file to the trash, inserts the file row and removes the upload.
   The object is placed just before the commit.

The client never supplies a hash the server trusts.

## Deletes, restores and the purge (`TrashService`)

A delete marks rows with a `deletion_id` and `deleted_at`; a deletion of a directory marks its
whole subtree with one id, which is what a restore brings back. A restore is refused when the name
has been taken since or the containing directory is itself in the trash. Deletions stay listed and
restorable for the restore window (`BYTESHELF_RESTORE_WINDOW_DAYS`, 30 by default) and count against
the quota until the purge removes them; one past its window is neither listed nor restorable, even
if the purge has not reached all of it yet.

## Removing objects safely (`ObjectCollector`)

An object is removed only when no row, live or trashed, points at it. Both sides of that question
hold the object's lock (`ObjectStore.LockAsync`): a finishing upload from placing the object until
its row commits, a removal from checking for rows until the file is gone.

## Maintenance (`StorageMaintenanceWorker`)

At startup and then hourly, recorded in `job_run` as `storage.maintenance`: removes uploads idle for
24 hours, single-`PUT` uploads started before the process (their request did not survive the
restart), staging entries with no upload, the trash past its window, and objects no row points at.

## Change log

Every change to a shelf, directory or file appends a `change_log` row in the same transaction; see
[ChangeLog.md](../ChangeLog/ChangeLog.md).
