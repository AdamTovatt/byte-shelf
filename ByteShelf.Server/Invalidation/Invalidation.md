# Invalidation

A Server-Sent Events stream that tells the web UI which cached queries to refetch after a change.

- `GET /api/invalidation/stream` (signed in) holds a `text/event-stream` response open. Headers
  go out at once, a comment heartbeat follows every 25 seconds, and `X-Accel-Buffering: no` keeps
  reverse proxies from buffering it. One caller may hold 10 streams at once; the 11th gets 429.
- `InMemoryInvalidationBroadcaster` keeps one bounded channel (capacity 100, `DropOldest`) per
  stream. Delivery is best-effort: events drop under backpressure, and the UI refetches when it
  reconnects.
- An event names a resource, not the data, and goes to every stream. A resource that only some
  users may see must not carry anything in its metadata the others may not; a shelf's id is all
  any of them carries.

## Where events come from

`ChangeLogInvalidationPublisher` follows the change log, polling it once a second, and publishes
what each new entry makes stale (`ChangeLogInvalidations`). Every change to storage, users and
access is in the log, written in the transaction that made it, so no service publishes on its own:
nothing can forget to, and nothing is published for a change that rolls back. SQLite commits one
writer at a time in sequence order, so following the sequence misses nothing.

| Resource | Made stale by | Metadata |
|---|---|---|
| `users` | a user's changes | none |
| `shelves` | a shelf's changes, any file or directory (usage), any grant (who sees which shelf) | none |
| `shelf-contents` | a shelf's changes, its files and directories (the trash included) | the shelf's id; none for a purge, which no longer knows its shelf |
| `shelf-access` | a shelf's changes, its grants and keys | the shelf's id; none once the grant or key is gone |

Every constant in `InvalidationResources` needs an entry in the web UI's map from resources to
queries; a missing one silently stops that resource from refreshing, and a client test checks the
two lists match.
