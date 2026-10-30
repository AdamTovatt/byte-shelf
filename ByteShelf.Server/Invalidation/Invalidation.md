# Invalidation

A Server-Sent Events stream that tells the web UI which cached queries to refetch after a change.

- `GET /api/invalidation/stream` (signed in) holds a `text/event-stream` response open. Headers
  go out at once, a comment heartbeat follows every 25 seconds, and `X-Accel-Buffering: no` keeps
  reverse proxies from buffering it. One caller may hold 10 streams at once; the 11th gets 429.
- `InMemoryInvalidationBroadcaster` keeps one bounded channel (capacity 100, `DropOldest`) per
  stream. Delivery is best-effort: events drop under backpressure, and the UI refetches when it
  reconnects.
- A service publishes `new InvalidationEvent(InvalidationResources.X)` after its transaction
  commits, never inside it, or a client could refetch state that then rolls back.
- An event names a resource, not the data, and goes to every stream. A resource that only some
  users may see must not carry anything in its metadata the others may not.

Every constant in `InvalidationResources` needs an entry in the web UI's map from resources to
queries; a missing one silently stops that resource from refreshing.
