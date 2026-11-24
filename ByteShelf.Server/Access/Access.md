# Access

Who may use a shelf, and how: admins, users granted access to a shelf, and keys programs present.
Shelves themselves are in [Shelves.md](../Storage/Shelves.md), users in [Users.md](../Users/Users.md).

## Callers

- An **admin** (a user with the `admin` role) may do everything on every shelf.
- A **user** may use the shelves they hold a grant on (`shelf_grant`): `read`, or `readWrite`. A
  user without a grant on a shelf is told it does not exist.
- A **key** works on the shelves it holds a grant on (`shelf_key_grant`), each with `read`, `addOnly`
  or `readWrite`, as a user holds theirs. A key without a grant on a shelf is told it does not exist.
  A key always holds at least one grant: it is deleted with its last. A program sends its
  secret on every request, in the `X-Api-Key` header (`ByteShelfCommon.ShelfKeys.HeaderName`) or as
  the bearer token (`Authorization: Bearer bsk_...`, which MCP clients send); a bearer token is a key
  only when it starts with `bsk_`, which no session token does. The key is looked up each time, so
  deleting it stops it at once. Only the SHA-256 of the secret is stored. Keys are accepted on the
  shelf endpoints, `/mcp` and `GET /api/version/release` ([Version](../Version/Version.md)) only,
  never on the user endpoints.

A signed-in user's access token comes in the `Authorization` header, or, under `/api/open` only, in
a cookie for what a browser loads by itself: a navigation, or an image, a player or a frame of the web UI
([Storage](../Storage/Storage.md#opening-in-a-browser-authsetupcookietokenpath));
either way the same access applies.

A request carrying a key acts as the key, even with a user's token beside it. A key that does not
check out answers 401 rather than falling back to the token, and so does an empty `X-Api-Key` header,
one sent more than once, or a key sent both ways at once.

## What each endpoint needs

Every action on a route naming a shelf (`{shelf}`) carries `[ShelfAccess(operation)]`, which requires a
signed-in user or a key, resolves the shelf, and asks `ShelfAccessResolver` (the one place the rule
lives) whether the caller may do the operation there. No access answers 404, too little 403. `/mcp`
asks the same resolver which tools to offer a key ([Mcp](../Mcp/Mcp.md)).
`ShelfAccessCoverageTests` holds the same table per action. The resolver reads the caller's grant on
the shelf asked for, a user's in `shelf_grant` and a key's in `shelf_key_grant`, on every request, so a
grant given, changed or taken applies to the next request.

| Operation | Endpoints | Who may |
|---|---|---|
| `Read` | list, list a tree, search, file records and metadata, content, a download link, the shelf's info, the trash list, what a path is encrypted under, listing and revoking links (its own; every link with `Write`) | every caller with access |
| `Add` | store a new file (with a description), create directories, every upload endpoint but starting a swap, an upload link | `addOnly` and `readWrite` keys, `readWrite` users |
| `Write` | overwrite, swap a file's contents (encrypting or decrypting it in place, as one `PUT` or an upload), move, describe a stored file, delete, restore, empty the trash, encrypt a directory or stop encrypting it | `readWrite` keys and users |
| `Administer` | rename, quota and delete the shelf; its grants and keys, and giving a key the shelf or taking it | admins |

Storing with `overwrite`, as a single `PUT`, an upload or an upload link, needs `Write` (`ShelfAccess.RequireStore`).
An upload records who started it; only that caller, while it may still store what the upload stores
(a grant can change meanwhile), or one with `Write`, may look at, continue, complete or abort it
(`ShelfAccess.RequireUpload`), so an add-only key cannot touch another caller's upload.
`GET /api/shelves` lists the shelves the caller can see; creating a shelf is admin only.

The one route that serves file contents without a caller is `GET /api/downloads/{token}`: the token
is a signed link from `download-link`, which needs `Read`, so every key may make one, add-only
included. It names one file for the lifetime its maker picked, at most 7 days, or until it is
revoked. It reads as its maker: the key or user that made it is looked up and asked
`ShelfAccessResolver` again each time it is used, so it stops working while its maker has no access
to the shelf, and for good once its key or user is deleted
([Storage](../Storage/Storage.md#download-links-downloadlinks)).

The one route that stores without a caller is `PUT /api/upload-links/{token}`, a signed link from
`upload-link`, which needs `Add` (`Write` for one that overwrites). It acts as its maker, checked the
same way, so a link stops working once its key is deleted or its maker may no longer store there
([Storage](../Storage/Storage.md#upload-links-uploadlinks)).

Every caller sees and revokes the links it made (`GET` and `DELETE links`); one with `Write` sees and
revokes every link on the shelf ([Storage](../Storage/Storage.md#links-shelf_link-linkservice)).

## Endpoints

Admin only, under `/api/shelves/{shelf}`:

| Method and route | What it does |
|---|---|
| `GET access` | Every user granted access to the shelf, with their email and display name. |
| `PUT access/{userId}` | Grants a user `read` or `readWrite` access, or changes it. 404 for an unknown user. Setting the permission they already have changes and logs nothing. |
| `DELETE access/{userId}` | Takes a user's grant away; 404 when they have none. |
| `GET keys` | The keys with a grant on the shelf, each with all its shelves (`ShelfKeyInfo.Grants`), never their secrets. |
| `POST keys` | Creates a key with a name and permission on this shelf; answers its secret, the one time it is shown. |

Admin only, a key by its id, under `/api/keys/{keyId}`. The routes naming a shelf ask
`[ShelfAccess(Administer)]` there, the others the admin role; keys are refused (401, or 403 and 404 by
the shelf rule):

| Method and route | What it does |
|---|---|
| `GET grants` | The shelves the key works on, oldest grant first, each with its permission. 404 for an unknown key. |
| `PUT grants/{shelf}` | Gives the key the shelf, or changes its permission there. 404 for an unknown key. Setting the permission it already has changes and logs nothing. |
| `DELETE grants/{shelf}` | Takes the shelf from the key; the key is deleted when this was its last shelf. 404 when the key has no grant there. |
| `DELETE` | Deletes the key from every shelf. 404 for an unknown key. |

A key's name is trimmed and must then be 1 to 100 characters (`ShelfKeys.MaxNameLength`) without
control characters, the rule user display names follow too. A bad name, or a permission that is
not one of the listed values, answers 400.

Deleting a shelf removes its grants, users' and keys', and every key that was its last shelf;
deleting a user removes their grants. Deleting a key or a user removes the links it made, and
deleting a shelf every link on it. A key that loses one shelf of several keeps its links there, which
stop working until it is given the shelf again, since a link rechecks its maker's access on each use.

## Change log

Grants (`shelf_grant`), keys (`shelf_key`, with the hash of the secret) and keys' grants
(`shelf_key_grant`) are logged like every other change, in the same transaction: a new key, then its
grant; a key's grants gone, then the key.

## On a replica

A replica holds its primary's grants, keys and keys' grants, so a key made on the primary works on the
replica too, but only to read: every write there is refused with 409 `read_only_replica`, and so is
changing grants or keys ([Replication](../Replication/Replication.md)).
