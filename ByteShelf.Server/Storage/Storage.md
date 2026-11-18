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
| `GET files/metadata?path=` | What the file's contents say (`FileMetadata`): when and where it was taken, the camera, a video's length, a PDF's title and pages. `readable` says whether the server reads the file at all (not when it is encrypted or of a type it does not read); `readAt` is null until it has read them, and always when it does not. See [Metadata](../Metadata/Metadata.md). |
| `DELETE files?path=` | Moves a file to the trash; answers the deletion. |
| `POST files/move?path=` | Moves or renames a file to the body's `toPath`; missing directories above it are created. 400 for `/`, 409 when the path is taken. |
| `PUT files/description?path=` | Sets the body's `description`, or removes it when null or blank; 400 over `FileDescription.MaxLength` characters. |
| `POST entries/move` | Moves or renames files and directories in one request (`MoveEntriesRequest`): `moves`, each `{ path, toPath }`, or `paths` into `intoDirectory`, each keeping its name. See "Batches" below. |
| `PUT files/descriptions` | Sets or removes files' descriptions in one request (`SetDescriptionsRequest`): `descriptions`, each `{ path, description }`, or `paths` that all get `description` (null or blank removes theirs). See "Batches" below. |
| `GET search?under=&text=&…` | One page of the live files under `under` (the root by default) that match every criterion given: `text`, `contentType`, `minSizeBytes`, `maxSizeBytes`, `createdFrom`, `createdBefore`, `updatedFrom`, `updatedBefore`, `sha256`, the metadata criteria `takenFrom`, `takenBefore`, `hasGps`, `minDurationSeconds`, `maxDurationSeconds`, `minPageCount` and `maxPageCount` ([Metadata](../Metadata/Metadata.md)), `limit` (1 to 1000, 100 by default) and `after` (the previous page's `nextCursor`). Sizes are inclusive; a time range includes its start and excludes its end. 400 for a criterion `FileSearch.Normalize` refuses, 404 when `under` is missing or names a file. See "Descriptions and search" below. |
| `GET duplicates?under=&text=&…` | One page of the duplicates among the live plain, non-empty files `search` would find with the same criteria: `groups` of two or more files with the same SHA-256, each with its `sha256`, `sizeBytes`, `repeatedBytes` (the size times the copies after the first) and `files` by path, most `repeatedBytes` first, then by SHA-256. `limit` (1 to 1000, 100 by default) counts groups; `after` takes the previous page's `nextCursor`, written `bytes:sha256`. 400 for a criterion or limit `DuplicateSearch.Normalize` refuses or an `after` that is not such a cursor, 404 as for `search`. See "Duplicates" below. |
| `POST directories` | Creates the directory at the body's `path` and any missing one above it; an existing one is answered as it is. |
| `POST directories/move?path=` | Moves or renames a directory with everything in it to the body's `toPath`. 400 for the root, a destination of `/` or inside itself, or one that would make a path under it longer than `ShelfPath.MaxPathLength`; 409 when the path is taken. |
| `DELETE directories?path=` | Moves a directory and everything live under it to the trash as one deletion; 400 for the root. |
| `GET directories/encryption?path=` | The encrypted directory a file at `path` (existing or not) would be under, with its `check`; `encryptedBy` is null for none. See "Encryption" below. |
| `PUT directories/encryption?path=` | Encrypts a directory with the body's `check` (base64 of an age file of at most `FileEncryption.MaxCheckBytes`); 400 for a check that is not one, 409 when it is encrypted already or inside or above an encrypted directory. |
| `DELETE directories/encryption?path=` | Stops encrypting a directory; 409 when it is not encrypted itself. |
| `GET content?path=` (and `HEAD`) | Streams the contents (`HEAD`: only the headers); `Range`, `If-None-Match` and `If-Range` work, the ETag is the SHA-256. Served with `X-Content-Type-Options: nosniff`, `Cache-Control: private` and, except for a PDF (Chrome's viewer does not run sandboxed), a sandboxing `Content-Security-Policy`. `download=true` adds a file name. Also served at `/api/open/{shelf}/content`, where a signed-in browser's token may come in a cookie (see "Opening in a browser" below). |
| `POST download-link?path=&expiresInMinutes=` | A link to the contents that works without a token for `expiresInMinutes` (1 to 10080, 1440 by default), to share the file, with its `id` to revoke it by (see below). Making one is a read; 400 for a lifetime outside those bounds, 409 for an encrypted file. |
| `PUT content?path=&overwrite=&encrypted=` | Stores the body in one request. Needs `Content-Length` (411 without); the `Content-Type` is stored, or without one the type the path's extension names (`ContentTypes.ForUpload`), so `curl -T` stores a PDF as one. `encrypted=true` marks the body as an age file (400 when it does not start as one). 409 when the path is taken and `overwrite` is not set, or for a plain file under an encrypted directory; 507 over quota. |
| `POST upload-link` | A link that stores one file sent to it without a token, from the body's `path`, `overwrite`, `description` and `expiresInMinutes` (1 to 1440, 15 by default), with its `id` to revoke it by; see "Upload links" below. Needs `Add`, or `Write` with `overwrite`; 400 for the root or a request `CreateUploadLinkRequest.Normalize` refuses, 409 for a path under an encrypted directory or over an encrypted file. |
| `POST uploads` | Starts a resumable upload from the body's `path`, `sizeBytes`, `contentType` (optional, typed as `PUT content` is), `overwrite` (false by default), `encrypted` (as on `PUT content`, checked when it completes) and the file's `description` if it has one (a single `PUT` has no body to carry one); reserves the size against the quota. |
| `PUT content/swap?id=&sha256=&encrypted=` | Swaps the contents of the file `id` in place for the body, in one request (`Content-Length` needed): encrypting a plain file (`encrypted=true`, an age file) or decrypting an encrypted one. Needs write access. 400 when sent as encrypted but not an age file; 404 for no such live file; 409 when its contents are no longer `sha256`, when the swap would not flip its encryption, or for a plain file under an encrypted directory; 411 without `Content-Length`; 507 when what it adds to the file's size does not fit the quota. See "Encryption" below. |
| `POST uploads/swap` | Starts a resumable upload that swaps a file's contents when it completes, from the body's `fileId`, `sha256`, `sizeBytes` and `encrypted`, refused as `PUT content/swap` is. Its chunks, progress, completion and abort are an upload's. |
| `GET uploads/{id}` | Progress: `receivedBytes` is where the next chunk starts. |
| `PUT uploads/{id}?offset=` | Appends a chunk of at most `UploadLimits.MaxChunkBytes` (64 MiB, 413 above); 409 when `offset` is not `receivedBytes`. |
| `POST uploads/{id}/complete` | Stores the file once every byte has arrived. 409 when the path was taken meanwhile, or for a swap when the file's contents changed or the swap no longer flips its encryption; 404 for a swap whose file is gone. The upload is kept. |
| `DELETE uploads/{id}` | Drops the upload and releases its reservation. |
| `GET trash` | Deletions still inside the restore window, newest first. |
| `GET trash/{deletionId}/tree?under=&limit=&after=` | What a deletion took, by the paths it had, a page at a time as `tree` pages (`DeletionTree`): the deletion as `GET trash` lists it, the encrypted folder the entry at `under` was stored in (`encryptedBy`, which may have been moved or deleted since; its check still tells the password), and the page, whose directory is the one the deletion was made in and whose entries are what it took at `under` and below, that entry first. Without `under`, that is all it took. 404 when it took nothing at `under`. |
| `GET trash/{deletionId}/content?id=` (and `HEAD`) | A file the deletion took, sent as a live file's contents are, `download=true` too. Also served at `/api/open/{shelf}/trash/{deletionId}/content`, for a browser's cookie. |
| `POST trash/{deletionId}/restore` | Puts a deletion back where it was. |
| `DELETE trash` | Empties the trash: everything in it can no longer be restored, and is purged now with its objects (see below). |

## Opening in a browser (`AuthSetup.CookieTokenPath`)

A new tab or a save is a plain navigation, which cannot send an `Authorization` header. So the web UI
opens and saves a file at `/api/open/{shelf}/content`, and one in the trash at
`/api/open/{shelf}/trash/{deletionId}/content`: the paths under `/api/open`, where the session's access token
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
does, `download=true` included, to anyone holding the token until it runs out or is revoked (see
"Links" below). The link's row names the file by id; an altered, expired or revoked token, or one
whose file is in the trash, answers 404; a restored file keeps its id, so its unexpired links work
again. A file stored over the linked one is a new file, so the link no longer reaches it; one
encrypted or decrypted in place keeps its id, so the link serves its contents as they are then, and
answers 409 while it is encrypted. Serving a link is recorded nowhere. A link reads with its maker's
access, checked again on every use as an upload link's is (below): while the key or user that made
it may no longer read the shelf, it answers 404, and it works again if that access comes back before
it runs out. `DownloadLinks` makes the link with `PublicAddress.LinkTo`: absolute on `APP_BASE_URL`
when that is set, which clients keep as it is; otherwise (Development only) a path from the server's
root, which each client makes absolute against the address it reaches the server by.

## Upload links (`UploadLinks`)

A link that lets someone without access, or a program without the key, store one file: an agent
hands it on, or uses it to move a file too large for its own context. `PUT /api/upload-links/{token}`
(`UploadLinksController`) stores the body as a single `PUT content` by the link's maker would, with
`Content-Length` reserved against the quota first, typed the same way, and with the description the
link was made with. The link's row holds what it grants (`UploadLinkTarget`): the path, whether it
may overwrite and the description; the URL carries only the token.

The maker's access is checked again on every use (`LinkAccess`, as for a download link): the key or
user is looked up (`ShelfCallers`) and asked `ShelfAccessResolver` as for any request, so a user whose
grant was taken away or lowered stops their links at once (404 when the maker has no access, 403
when it may no longer store there), and a deleted key or user takes its links with it (below). A link that may not overwrite is refused (409) once
its path is taken, so it stores at most one file; one that may overwrite replaces the file on every
use until it runs out, 15 minutes by default and at most 24 hours (`UploadLink.Lifetime.Normalize`).
A replica refuses the `PUT`, as it refuses every write. A link sends a plain file, so it is refused
(409) under an encrypted directory or over an encrypted file, when it is made and again when it is
used, so a directory encrypted after the link was made stops it too.

## Links (`shelf_link`, `LinkService`)

Every download and upload link is a `shelf_link` row: its kind, its file or place, who made it (a key
or a user), and when it was made and runs out. Revoking a link deletes its row, so no request made
with it from then on is served; one already being served, such as an upload whose body is still
arriving, finishes. A link's row also goes with its shelf, its file (once purged) and the key or user that made it:
those references cascade, as nothing logs a link's removal. Maintenance deletes the rows that have
run out (below); until then a row past its expiry is never answered.

Each server keeps its own links: the rows are not in the change log, so replication never carries
them, as each server signs its links with its own secret. A replica makes download links of its own,
lists them and revokes them (`DELETE links/{id}` is allowed there); the primary never sees them, nor a
replica the primary's.

Under `/api/shelves/{shelf}`, with `Read`:

| Method and route | What it does |
|---|---|
| `GET links` | The links not run out, newest first (`ShelfLink`): each one's id, kind, its file's path as it is now (null while the file is in the trash) or the place it stores at, whether it overwrites, its description, who made it (`createdBy`: kind, id, name, and whether it is the caller) and when it was made and runs out. Never a link's URL. |
| `DELETE links/{id}` | Revokes a link; 204, or 404 when there is no such link the caller may see. |

A caller sees and revokes the links it made; one with `Write` on the shelf, every link on it
(`LinkService.VisibleMaker`, the one place the rule lives). No use is counted: a link meant to be
read once is made with a lifetime of minutes and revoked after, since range requests make one use
ill-defined.

## Link tokens (`Shared/Auth/LinkTokens`)

Download and upload links are both a token after their route: the link's id and an HMAC-SHA256 over
it. What a link grants and when it runs out are in its row. One secret, `keys/link-secret`, signs them
all, through a key derived from it for each kind of link (`HMAC(secret, kind)`), so a download link's
token never reads as an upload link's; changing a kind's purpose ends every link of that kind. An
altered token answers 404. Removing the secret and restarting ends every link at once.

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

What a listed deletion took can be read without restoring it (`ListDeletionAsync`,
`GetDeletedContentAsync`): a read, open to whoever can list the trash and answered on a replica too,
which is how a replica gives back a file purged on its primary. `DeletionWalk` walks the deletion's
directories from the one it was made in, so each entry has the path it had. An encrypted file's
contents are its age bytes. Contents purged between the lookup and the open answer 404, as a file
looked up a moment later would. Putting a file back on another server is an ordinary upload.

## Descriptions and search (`FileService`)

A file has at most one description, free text of up to 2,000 characters (code points, as SQLite's
`length()` counts them), trimmed, with blank meaning none. It is part of the file's row, so the change
log carries it to replicas. Setting it moves the file's `updatedAt` and is logged as an update;
setting the text it already has changes and logs nothing. A file stored over another keeps the
other's description unless its upload brings one: a new version of a file is still that file.

Search runs in one query: a recursive walk from the `under` directory builds each live file's full
path, and the criteria of `ByteShelfCommon.FileCriteria` filter the files. `text` matches the name or
the description; both sides are folded by `FileCriteria.Fold` (normalization form C, invariant
lowercase). SQLite's own `lower()` folds ASCII only, so the file row keeps folded copies
(`name_fold`, `description_fold`) that `FileRepository` writes from the name and description on
every write. `contentType` compares the type without its parameters, kept the same way in
`media_type` by `ContentTypes.MediaTypeOf`, and `type/*` matches the whole family. `text` also
matches the title and camera read from a plain file's contents, and the metadata criteria ask what
else was read ([Metadata](../Metadata/Metadata.md)); the in-memory client reads nothing, so there
they match no file.
Results come by full path; a page asks for one row more than it returns, to know whether another
follows, and its `nextCursor` is its last path, which the next page starts after. The in-memory
client applies the same rules, and the client contract tests run both. The criteria's condition
lives in `SearchCriteria`, and the files it takes in `FileRepository`'s `FoundSql`, both shared by
search and the search for duplicates.

## Duplicates (`FileService.FindDuplicatesAsync`)

The search for duplicates walks and filters as search does, then groups the plain files found by
SHA-256 and keeps the groups of two or more, all in one query. Only files the search finds count:
under a directory, a group holds only the files under it. Encrypted files are left out, since two
encryptions of the same file never share a hash, and so are empty files, which all share one hash
and whose copies tidy nothing away. A page asks for one group more than it returns and
reads only its own groups' files; `DuplicateSearchResults.Page` in Common builds the groups and the
cursor, the last group's repeated bytes and SHA-256 (`DuplicateCursor`), for the in-memory client
too. Identical contents are stored once in the object store, so duplicates cost no disk; the search
is for tidying.

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

## Batches (`EntryBatches`)

`POST entries/move` and `PUT files/descriptions` take 1 to `EntryBatch.MaxItems` items. Each item goes
through the one-entry service (a move by path tries a file, then a directory), in its own
transaction with its own change-log entry, in the order given, so a later item may use a path an
earlier one made. The answer is one result per item in that order: the file or directory as it is
now, or `error` with the code the one-entry endpoint would answer and its reason; a move whose path
names nothing says neither a file nor a directory is there. A refused item undoes
nothing and stops nothing. A request in neither or both forms, empty, too long, or with a path or
description the rules refuse is 400 before any item is applied; a new path that is too long only
once joined to `intoDirectory` is that item's refusal. A fault ends the request with 500, leaving
the items before it done; a client that goes away stops it between items.

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
restart), staging entries with no upload, the trash past its window, objects no row points at, and
links that have run out. A pass holds `MaintenanceGate` throughout, so on a replica it waits for a sync step (a whole copy
included) to finish, and the next step waits for it.

## Change log

Every change to a shelf, directory or file appends a `change_log` row in the same transaction; see
[ChangeLog.md](../ChangeLog/ChangeLog.md). Links are each server's own and are not logged (see "Links" above).
