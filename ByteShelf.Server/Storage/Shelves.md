# Shelves

A shelf is one storage place with its own directories and quota, made per purpose (one for an
app's files, one for database backups). Shelves are one flat level: nothing nests, and a shelf is
never a directory. Names follow `ByteShelfCommon.ShelfName` and appear in URLs
(`/api/shelves/{name}/...`).

Anyone with access to a shelf can list and read it; everything else here is admin only (see
[Access.md](../Access/Access.md)):

| Method and route | What it does |
|---|---|
| `GET /api/shelves`, `GET /api/shelves/{shelf}` | Every shelf the caller can use, or one, with its usage, what the caller may do with its files, and whether the server is a replica. |
| `POST /api/shelves` | Creates a shelf and its root directory in one transaction. |
| `PUT /api/shelves/{shelf}` | Replaces the name and quota; both are always set, so a body without `quotaBytes` removes the quota. A new name changes the shelf's URLs; a quota below what the shelf holds stops new uploads, and keeps new thumbnails, previews and the like of what it holds from being kept, for every shelf that holds the same contents too. |
| `DELETE /api/shelves/{shelf}` | Deletes the shelf with its grants, users' and keys', and every key that was its last shelf, only once it holds nothing: no file (the trash included), no directory but its root, no upload. 409 otherwise. Deleted files stay in the trash, and block this, until their window ends or the trash is emptied (`DELETE /api/shelves/{shelf}/trash`, see [Storage.md](Storage.md)). |

The quota counts the size of every file row on the shelf, the trash included, and of every derived
file (thumbnail, preview and the like, see [Derived.md](../Derived/Derived.md)) made of that row's
contents, plus the declared size of every upload in progress. Identical contents count on every row
that holds them, their derived files too. The API, web UI, CLI and MCP show it as one number,
`usedBytes`, which covers the files and their derived files together. A shelf without a quota has no limit.
An upload is refused as it starts when its declared size does not fit, and as it finishes when the
derived files the server already has of its contents do not fit beside it.
