# Access

Who may use a shelf, and how: admins, users granted access to a shelf, and keys programs present.
Shelves themselves are in [Shelves.md](../Storage/Shelves.md), users in [Users.md](../Users/Users.md).

## Callers

- An **admin** (a user with the `admin` role) may do everything on every shelf.
- A **user** may use the shelves they hold a grant on (`shelf_grant`): `read`, or `readWrite`. A
  user without a grant on a shelf is told it does not exist.
- A **key** works on exactly one shelf, with `read`, `addOnly` or `readWrite`. A program sends its
  secret on every request, in the `X-Api-Key` header (`ByteShelfCommon.ShelfKeys.HeaderName`) or as
  the bearer token (`Authorization: Bearer bsk_...`, which MCP clients send); a bearer token is a key
  only when it starts with `bsk_`, which no session token does. The key is looked up each time, so
  deleting it stops it at once. Only the SHA-256 of the secret is stored. Keys are accepted on the
  shelf endpoints and `/mcp` only, never on the user endpoints.

A request carrying a key acts as the key, even with a user's token beside it. A key that does not
check out answers 401 rather than falling back to the token, and so does an empty `X-Api-Key` header,
one sent more than once, or a key sent both ways at once.

## What each endpoint needs

Every action on `api/shelves/{shelf}/...` carries `[ShelfAccess(operation)]`, which requires a
signed-in user or a key, resolves the shelf, and asks `ShelfAccessResolver` (the one place the rule
lives) whether the caller may do the operation there. No access answers 404, too little 403. `/mcp`
asks the same resolver which tools to offer a key ([Mcp](../Mcp/Mcp.md)).
`ShelfAccessCoverageTests` holds the same table per action.

| Operation | Endpoints | Who may |
|---|---|---|
| `Read` | list, list a tree, search, file records, content, a download link, the shelf's info, the trash list | every caller with access |
| `Add` | store a new file (with a description), create directories, every upload endpoint | `addOnly` and `readWrite` keys, `readWrite` users |
| `Write` | overwrite, move, describe a stored file, delete, restore, empty the trash | `readWrite` keys and users |
| `Administer` | rename, quota and delete the shelf; its grants and keys | admins |

Storing with `overwrite`, as a single `PUT` or an upload, needs `Write` (`ShelfAccess.RequireStore`).
An upload records who started it; only that caller, or one with `Write`, may look at, continue,
complete or abort it (`ShelfAccess.RequireUpload`), so an add-only key cannot touch another caller's
upload. `GET /api/shelves` lists the shelves the caller can see; creating a shelf is admin only.

The one route that serves file contents without a caller is `GET /api/downloads/{token}`: the token
is a signed link from `download-link`, which needs `Read`, so every key may make one, add-only
included. It names one file for the lifetime its maker picked, at most 7 days. Whoever holds the
link may read that file until it runs out, even if the access it was made with is taken away
meanwhile ([Storage](../Storage/Storage.md#download-links-downloadlinks)).

## Endpoints

Admin only, under `/api/shelves/{shelf}`:

| Method and route | What it does |
|---|---|
| `GET access` | Every user granted access to the shelf, with their email and display name. |
| `PUT access/{userId}` | Grants a user `read` or `readWrite` access, or changes it. 404 for an unknown user. Setting the permission they already have changes and logs nothing. |
| `DELETE access/{userId}` | Takes a user's grant away; 404 when they have none. |
| `GET keys` | The shelf's keys, never their secrets. |
| `POST keys` | Creates a key with a name and permission; answers its secret, the one time it is shown. |
| `DELETE keys/{keyId}` | Deletes a key; 404 when the shelf has no such key. |

A key's name is trimmed and must then be 1 to 100 characters (`ShelfKeys.MaxNameLength`) without
control characters, the rule user display names follow too. A bad name, or a permission that is
not one of the listed values, answers 400.

Deleting a shelf removes its grants and keys; deleting a user removes their grants.

## Change log

Grants (`shelf_grant`) and keys (`shelf_key`, with the hash of the secret) are logged like every other
change, in the same transaction.

## On a replica

A replica holds its primary's grants and keys, so a key made on the primary works on the replica too,
but only to read: every write there is refused with 409 `read_only_replica`, and so is changing grants
or keys ([Replication](../Replication/Replication.md)).
