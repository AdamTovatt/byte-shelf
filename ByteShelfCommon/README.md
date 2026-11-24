# ByteShelfCommon

The contract shared by the ByteShelf server and its clients: the request and response types they
exchange, the rules for paths and shelf names, and source-generated JSON. Targets net8.0 and is
AOT-compatible.

## Types

| Type | What it is |
|---|---|
| `ShelfInfo` | A shelf with its quota and usage. |
| `CreateShelfRequest`, `UpdateShelfRequest` | Creates a shelf, and replaces its name and quota. |
| `ShelfFile` | A file's record: id, path, size, content type, SHA-256, timestamps, description, whether its contents are encrypted (`Encrypted`), and in listings the values of the fields plugins declare for them (`PluginFields`). |
| `SetDescriptionRequest`, `FileDescription` | Setting a file's description, and its rules. |
| `FileCriteria` | Which of a shelf's files a search looks at: the directory, text, type, sizes, times, SHA-256, whether it has a description, and what the server read from the contents (taken times, a GPS position, durations, page counts) or plugins keep (`PluginFields`), checked by `NormalizeCriteria`. Both searches below take their criteria from it. |
| `FileSearch`, `FileSearchResults` | The files that meet the criteria, a page at a time by path, and one page of them. |
| `DuplicateSearch`, `DuplicateSearchResults`, `DuplicateGroup`, `DuplicateCursor` | The groups of plain, non-empty files with the same contents among those that meet the criteria, a page of groups at a time, most repeated bytes first, and where a page ended. `DuplicateSearchResults.Page` groups, orders and pages them, for the server and the in-memory client alike. |
| `DownloadLink` | A link to a file's contents that works without a token until it runs out or is revoked, with its id, and the bounds of its lifetime (`Lifetime`: a day by default, at most 7 days). |
| `UploadLink`, `CreateUploadLinkRequest`, `NormalizedUploadLinkRequest` | A link that stores one file sent to it without a token, with its id, and asking for one (`Lifetime`: 15 minutes by default, at most 24 hours). |
| `ShelfLink`, `ShelfLinkKind`, `LinkMaker`, `LinkMakerKind` | A download or upload link as listed, to revoke by its id: what it reaches, who made it, and when it runs out. |
| `FileMetadata` | What the server read from a file's contents (when and where a photo was taken, the camera, a video's length, a PDF's title and pages), whether it reads that file at all (`Readable`), and what plugins keep about it (`Plugins`). |
| `PluginDeclaration`, `PluginField`, `PluginFieldKind`, `PluginDocumentSubject`, `InstalledPlugins` | A plugin with the fields it declares in its documents, and the plugins a server has, which a filter is checked against. |
| `PluginFileMetadata`, `PluginFieldEntry`, `PluginFieldValue`, `SetPluginMetadataRequest` | What one plugin keeps about a file, with its declared fields and their values, and writing its document about the file. |
| `PluginFieldFilter`, `PluginFieldCondition` | A search's criterion on a field a plugin declares. |
| `PluginDocument` | What a plugin keeps about a file or its contents: one JSON object, which is how it is written in JSON. |
| `MediaTypePattern` | One media type or a family such as `image/*`, and which media types it covers. |
| `LinkLifetime` | One kind of link's lifetime rule: its default, its bounds and `Normalize`. |
| `FileEncryption`, `EncryptedFolder`, `PathEncryption`, `SetFolderEncryptionRequest` | Password encryption: how an age file starts, an encrypted directory's check text, an encrypted directory with its check, what a path is encrypted under, and encrypting a directory. |
| `ShelfDirectory`, `DirectoryListing`, `ListedDirectory` | A directory and whether it is encrypted (`Encrypted`), what one holds, and a directory in a listing with the size and count of the files under it, and how many are encrypted. |
| `TreeQuery`, `DirectoryTree`, `TreeEntry` | What to list of everything under a directory (a depth, a page), one page of it, and one entry of a page, a directory or a file. |
| `StoredOrder` | The order names and paths are listed in: by code point, as the server's database sorts them. |
| `CreateDirectoryRequest`, `MoveRequest` | Creates a directory; moves or renames a file or directory. |
| `StartUploadRequest`, `StartContentSwapRequest`, `UploadSession` | Starting and following a resumable upload, of a new file or swapping a file's contents in place. |
| `TrashEntry`, `TrashEntryKind` | A deletion that can still be restored. |
| `ShelfAccessGrant`, `SetShelfAccessRequest`, `ShelfAccessPermission` | A user's access to a shelf, and giving it. |
| `ShelfGrants` | `ValidatePermission`, which refuses a value that is no `ShelfAccessPermission`. |
| `ShelfKeyInfo`, `ShelfKeyGrantInfo`, `CreateShelfKeyRequest`, `CreatedShelfKey`, `SetShelfKeyGrantRequest`, `ShelfKeyPermission` | A program's key with the shelves it works on and what it may do on each, creating one on a shelf, and giving it another. |
| `ShelfKeys` | The `X-Api-Key` header a key is sent in, the secret's prefix, which also tells a key sent as the bearer token from a session token, and `ValidatePermission`, which refuses a value that is no `ShelfKeyPermission`. |
| `EntryReference` | Names a file or directory by path or by id; a path or an id converts to one. |
| `UserInfo`, `CreateUserRequest`, `UpdateRolesRequest`, `UpdatePasswordRequest`, `UserRoles` | Users and changing them. |
| `RequestPasswordResetRequest`, `CompletePasswordResetRequest` | Asking for a reset link, and using it. |
| `SignInRequest`, `SessionTokens`, `RefreshTokenRequest` | Signing in, refreshing and signing out at `/api/auth`. |
| `ReplicationStatus`, `ReplicaInfo`, `ReplicationSourceInfo`, `PairingCodeResponse`, `PairWithPrimaryRequest`, `PromoteResponse` | A server's replicas or the primary it follows, pairing one, and promoting it. |
| `ServerVersion`, `ReleaseVersion` | The release a server runs, which only signed-in users and keys may read, and reading an assembly's release from its informational version, without the commit after the `+`. |
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
- `FileCriteria.NormalizeCriteria` checks the criteria: sizes, durations and page counts not
  negative, a duration a finite number of seconds, ranges not empty (a time range includes its
  start and excludes its end; sizes, durations and page counts are inclusive), a content type that
  is one media type or a family such as `image/*`, and a SHA-256 of 64 hex digits.
  `FileSearch.Normalize` adds a page of 1 to 1,000 files (100 by default), and
  `DuplicateSearch.Normalize` a page of 1 to 1,000 groups (100 by default). `Fold` is how search
  compares text, both sides: normalization form C, then invariant lowercase.
- `PluginDocument.Parse` checks a plugin's document, and its JSON converter does the same for one that
  is sent: one JSON object, nested at most 32 deep, of at most 64 KiB, counted in UTF-8 without
  whitespace and with its text as itself rather than as escapes.
- `PluginField` and `PluginDeclaration` check what a plugin declares: its id (`PluginDeclaration.IsId`:
  lowercase letters, digits and hyphens), a field's name (a letter, then
  letters, digits, hyphens and underscores, 64 at most), one of each name, and that only text is searched.
  `PluginFieldValue.Of` reads a field's value from a document, ignoring one of another JSON type.
- `PluginFieldFilter.Parse` reads a filter: `plugin.field` has a value, `!plugin.field` has none,
  `plugin.field~text` contains the text as search compares it, `plugin.field=1..5` is a number between two
  (both included; either may be left out, and `plugin.field=4` is that number),
  `plugin.field=2026-01-01..2026-02-01` an instant from one and before another (each a UTC date, or a time with
  an offset; either may be left out), and `plugin.field=true` or `=false` a flag. A search takes at most 20.
- `ContentHash` checks a SHA-256 named in a request (64 hex digits, in either case) and answers it
  lowercase, as the server stores it.
- `ContentTypes` checks the media type stored with a file, and `ForUpload` picks it when an upload
  gives none: the one its path's extension names, else `application/octet-stream`.
- `NormalizedUploadLinkRequest.Create` checks an upload link's request: a file's path (never the
  root), a description, and a lifetime within `UploadLink.Lifetime`.
- `PasswordPolicy` checks a password,
  `UserFields` an email and display name, `DisplayText` short names such as a key's, and
  `UserRoles` a user's roles.
- The server applies every rule, and so does the in-memory client in ByteShelfClient. The HTTP
  client applies `ShelfPath`, `ContentHash`, `ContentTypes`, `FileDescription`, `FileSearch`, `DuplicateSearch`, the link lifetimes
  and `NormalizedUploadLinkRequest` before sending,
  since they shape the request, and leaves the rest to the server.

## JSON

`ByteShelfCommon.Serialization.ByteShelfJsonContext` serializes every type that is sent without
reflection, in camelCase like the server; a `PluginDocument` is written by its own converter
(`PluginDocumentJsonConverter`), wherever it stands. Enums are written as camelCase strings and read back ignoring case, by
`CamelCaseEnumConverter`, set on each enum type so the server and every client agree.
