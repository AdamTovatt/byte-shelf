# Shelves

A shelf is one storage place with its own directories and quota, made per purpose (one for an
app's files, one for database backups). Shelves are one flat level: nothing nests, and a shelf is
never a directory. Names follow `ByteShelfCommon.ShelfName` and appear in URLs
(`/api/shelves/{name}/...`).

All admin only:

| Method and route | What it does |
|---|---|
| `GET /api/shelves`, `GET /api/shelves/{shelf}` | Every shelf, or one, with its usage. |
| `POST /api/shelves` | Creates a shelf and its root directory in one transaction. |
| `PUT /api/shelves/{shelf}` | Replaces the name and quota; both are always set, so a body without `quotaBytes` removes the quota. A new name changes the shelf's URLs; a quota below what the shelf holds only stops new uploads. |
| `DELETE /api/shelves/{shelf}` | Deletes the shelf, only once it holds nothing: no file (the trash included), no directory but its root, no upload. 409 otherwise. Deleted files stay in the trash, and block this, until their window ends or the trash is emptied (`DELETE /api/shelves/{shelf}/trash`, see [Storage.md](Storage.md)). |

The quota counts the size of every file row on the shelf, the trash included, plus the declared
size of every upload in progress. A shelf without a quota has no limit.
