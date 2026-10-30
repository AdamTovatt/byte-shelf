# Shelves

A shelf is one storage place with its own directories and quota, made per purpose (one for an
app's files, one for database backups). Shelves are one flat level: nothing nests, and a shelf is
never a directory. Names follow `ByteShelfCommon.ShelfName` and appear in URLs
(`/api/shelves/{name}/...`).

`POST /api/shelves` creates a shelf and its root directory in one transaction; `GET /api/shelves`
lists every shelf with its usage. Both are admin only.

The quota counts the size of every file row on the shelf, the trash included, plus the declared
size of every upload in progress. A shelf without a quota has no limit.
