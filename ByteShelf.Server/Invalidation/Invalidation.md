# Invalidation

A Server-Sent Events stream that tells the web UI which cached queries to refetch after a change.

- `GET /api/invalidation/stream` (signed in) holds a `text/event-stream` response open. Headers
  go out at once, a comment heartbeat follows every 25 seconds, and `X-Accel-Buffering: no` keeps
  reverse proxies from buffering it. One caller may hold 10 streams at once; the 11th gets 429.
- `InMemoryInvalidationBroadcaster` keeps one bounded channel (capacity 100, `DropOldest`) per
  stream. Delivery is best-effort: events drop under backpressure, and the UI refetches when it
  reconnects.
- An event names a resource, not the data. One naming a shelf (`shelf-contents` and `shelf-access`
  with the shelf's id) goes only to streams whose user can see that shelf when it is sent, by the
  rule `ShelfAccessResolver` holds, with the user's roles and grants read as they are then
  (`InvalidationAudience`); the rest name nothing and go to every stream. A stream ends once its
  user has been deleted.

## Where events come from

`ChangeLogInvalidationPublisher` follows the change log, polling it once a second, and publishes what
each new entry makes stale (`ChangeLogInvalidations`). Every change a service makes to storage, users
and access is in the log, written in the transaction that made it, so no service publishes on its own
(what goes with a file by a foreign key is not in it, but the file's purge is): nothing can forget to,
and nothing is published for a change that rolls back. SQLite commits one writer at a time in sequence
order, so following the sequence misses nothing. It starts from where the log stands when it first reads
it, and a failed read, that first one included, is tried again at the next poll.

| Resource | Made stale by | Metadata |
|---|---|---|
| `users` | a user's changes | none |
| `shelves` | a shelf's changes, any file or directory, and derived files that came or went (usage), any user's grant (who sees which shelf, and what they may do there) | none |
| `shelf-contents` | a shelf's changes, its files and directories (the trash included), what plugins keep about a file, and derived files of its live plain files that came or went | the shelf's id; none for a purge, which no longer knows its shelf |
| `shelf-access` | a shelf's changes and its users' grants; any key's grant | the shelf's id; none once the user's grant is gone, and none for a key's grant, since a shelf's key list shows each key's other shelves and so every shelf's list is stale |

A key's own row makes nothing stale: it is shown only through its grants, which are logged beside it
when it comes and goes.

A promotion and a change of someone's role make no `shelves` stale: whether its server is a replica, and what
the caller may do there, are as they were until a shelf is next read.

What plugins make of files' contents ([Derived files](../Derived/Derived.md)) is in no log. `DerivedInvalidationPublisher`
publishes that the shelves with a live plain file it was made of are stale, and `shelves`, whose usage counts
it, at once and then at most every 5 seconds however much came or went (`DerivedChanges`), so a listing shows
new thumbnails.

What was read from files' contents ([Metadata](../Metadata/Metadata.md)) makes nothing stale: an
entry names an object, not a shelf, and refreshing every open listing would show nothing new, as listings
carry none of it, unlike thumbnails. The web UI fetches a file's metadata afresh whenever it shows it. A plugin's document
about contents is the same.

A plugin's document about a file makes its shelf's contents stale, as a description does: listings carry
its fields and search finds the file by them. Its entry names the file, whose shelf the publisher looks
up, and makes nothing stale once the file is gone, whose purge did. A document that was deleted
(`--forget-plugin`, or a replica removing what its primary no longer has) names none, so every shelf's
contents are stale; an entry whose state the log forgot for a newer one
([Change log](../ChangeLog/ChangeLog.md)) says nothing, as the newer follows it.

Every constant in `InvalidationResources` needs an entry in the web UI's map from resources to
queries; a missing one silently stops that resource from refreshing, and a client test checks the
two lists match.
