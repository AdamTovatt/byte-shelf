# ByteShelfCommon

The contract shared by the ByteShelf server and its clients: the request and response types they
exchange, the rules for paths and shelf names, and source-generated JSON. Targets net8.0 and is
AOT-compatible.

## Types

| Type | What it is |
|---|---|
| `ShelfInfo` | A shelf with its quota and usage. |
| `CreateShelfRequest`, `UpdateShelfRequest` | Creates a shelf, and replaces its name and quota. |
| `ShelfFile` | A file's record: id, path, size, content type, SHA-256, timestamps, description. |
| `SetDescriptionRequest`, `FileDescription` | Setting a file's description, and its rules. |
| `FileSearch`, `FileSearchResults` | What to look for among a shelf's files, and one page of what was found. |
| `DownloadLink` | A link to a file's contents that works without a token until it runs out, and the bounds of its lifetime (a day by default, at most 7 days). |
| `ShelfDirectory`, `DirectoryListing` | A directory, and what one holds. |
| `TreeQuery`, `DirectoryTree` | What to list of everything under a directory (a depth, a page), and one page of it. |
| `StoredOrder` | The order names and paths are listed in: by code point, as the server's database sorts them. |
| `CreateDirectoryRequest`, `MoveRequest` | Creates a directory; moves or renames a file or directory. |
| `StartUploadRequest`, `UploadSession` | Starting and following a resumable upload. |
| `TrashEntry`, `TrashEntryKind` | A deletion that can still be restored. |
| `ShelfAccessGrant`, `SetShelfAccessRequest`, `ShelfAccessPermission` | A user's access to a shelf, and giving it. |
| `ShelfKeyInfo`, `CreateShelfKeyRequest`, `CreatedShelfKey`, `ShelfKeyPermission` | A program's key to one shelf, and creating one. |
| `ShelfKeys` | The `X-Api-Key` header a key is sent in, and the secret's prefix. |
| `EntryReference` | Names a file or directory by path or by id; a path or an id converts to one. |
| `UserInfo`, `CreateUserRequest`, `UpdateRolesRequest`, `UpdatePasswordRequest`, `UserRoles` | Users and changing them. |
| `RequestPasswordResetRequest`, `CompletePasswordResetRequest` | Asking for a reset link, and using it. |
| `SignInRequest`, `SessionTokens`, `RefreshTokenRequest` | Signing in, refreshing and signing out at `/api/auth`. |
| `ReplicationStatus`, `ReplicaInfo`, `ReplicationSourceInfo`, `PairingCodeResponse`, `PairWithPrimaryRequest`, `PromoteResponse` | A server's replicas or the primary it follows, pairing one, and promoting it. |
| `ErrorResponse`, `ErrorCodes` | Every error body, with a machine-readable code. |
| `UploadLimits` | The server's limits, such as the largest chunk. |

## Rules

- `ShelfPath` normalizes and validates paths inside a shelf: a leading `/`, names separated by `/`,
  no empty names, `.`, `..`, backslashes, control or invisible formatting characters, and every
  name in Unicode normalization form C. Names that are valid on a shelf are not always valid file
  names on every operating system (Windows refuses `CON` and a trailing dot), so a client that
  writes files to disk maps them itself.
- `ShelfName` validates shelf names: 1 to 63 lowercase letters, digits, `-`, `_` and `.`, starting
  with a letter or digit. `ShelfSettings` adds that a quota cannot be negative.
- `FileDescription` trims a description and puts it in normalization form C; blank is none, and it
  holds at most 2,000 characters, counted as code points.
- `FileSearch.Normalize` checks a search: a page of 1 to 1,000 files (100 by default), sizes not
  negative and ranges not empty (a range includes its start and excludes its end), a content type
  that is one media type or a family such as `image/*`, and a SHA-256 of 64 hex digits. `Fold` is
  how search compares text, both sides: normalization form C, then invariant lowercase.
- `ContentTypes` checks the media type stored with a file, `PasswordPolicy` a password,
  `UserFields` an email and display name, `DisplayText` short names such as a key's, and
  `UserRoles` a user's roles.
- The server applies every rule, and so does the in-memory client in ByteShelfClient. The HTTP
  client applies `ShelfPath`, `ContentTypes`, `FileDescription` and `FileSearch` before sending,
  since they shape the request, and leaves the rest to the server.

## JSON

`ByteShelfCommon.Serialization.ByteShelfJsonContext` serializes every type without reflection, in
camelCase like the server. Enums are written as camelCase strings.
