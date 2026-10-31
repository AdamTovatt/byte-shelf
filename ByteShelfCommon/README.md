# ByteShelfCommon

The contract shared by the ByteShelf server and its clients: the request and response types they
exchange, the rules for paths and shelf names, and source-generated JSON. Targets net8.0 and is
AOT-compatible.

## Types

| Type | What it is |
|---|---|
| `ShelfInfo` | A shelf with its quota and usage. |
| `CreateShelfRequest`, `UpdateShelfRequest` | Creates a shelf, and replaces its name and quota. |
| `ShelfFile` | A file's record: id, path, size, content type, SHA-256, timestamps. |
| `ShelfDirectory`, `DirectoryListing` | A directory, and what one holds. |
| `CreateDirectoryRequest`, `MoveRequest` | Creates a directory; moves or renames a file or directory. |
| `StartUploadRequest`, `UploadSession` | Starting and following a resumable upload. |
| `TrashEntry`, `TrashEntryKind` | A deletion that can still be restored. |
| `ErrorResponse`, `ErrorCodes` | Every error body, with a machine-readable code. |
| `UploadLimits` | The server's limits, such as the largest chunk. |

## Rules

- `ShelfPath` normalizes and validates paths inside a shelf: a leading `/`, names separated by `/`,
  no empty names, `.`, `..`, backslashes, control or invisible formatting characters, and every
  name in Unicode normalization form C. Names that are valid on a shelf are not always valid file
  names on every operating system (Windows refuses `CON` and a trailing dot), so a client that
  writes files to disk maps them itself.
- `ShelfName` validates shelf names: 1 to 63 lowercase letters, digits, `-`, `_` and `.`, starting
  with a letter or digit.

## JSON

`ByteShelfCommon.Serialization.ByteShelfJsonContext` serializes every type without reflection, in
camelCase like the server. Enums are written as camelCase strings.
