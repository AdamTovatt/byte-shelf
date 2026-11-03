# Replication

One-way: a primary is paired with replicas, each of which keeps a whole copy of it and follows it.
A server is a replica while it has a `replication_source` row; otherwise it is a primary, with or
without replicas. A replica has no replicas of its own. The rest of the server asks
`ReplicationState.AllowsWritesAsync` rather than looking for the row.

The replica does all the calling: it pulls the primary's change log and the bytes of its files. The
primary never calls a replica, so a replica needs to reach the primary's public URL and nothing needs
to reach the replica.

## Pairing

1. An admin on the primary makes a **pairing code** (`POST api/replication/codes`). A `replica_invite`
   row keeps the hash of its secret for 15 minutes (`PairingCodeResponse.Lifetime`). The code
   (`bsrep1_` + base64url JSON, `PairingCode`) carries the primary's `APP_BASE_URL`, its instance id
   and the secret.
2. An admin on the replica-to-be pastes it with a name (`POST api/replication/pair`). That server must
   not be a replica already, the code must not be its own (`server_instance` holds each server's id,
   made the first time it is asked for), and it must have no replicas and no shelves.
3. The replica posts the secret and the name to the primary (`POST api/replication/feed/accept`,
   rate-limited per caller). The primary uses the code up and answers with its instance id and a fresh
   **credential** (`bsr_` + 256 random bits), keeping only its SHA-256 in `replica`. A code is spent by a
   pairing, and only one of two simultaneous attempts can spend it.
4. The replica checks the instance id is the code's, keeps the primary's URL, instance id and the
   credential in `replication_source`, and starts a whole copy at once.

The credential travels in `X-Replica-Key` (`ReplicaCredentials.HeaderName`), never as a bearer token or
a shelf key, so nothing that signs in users or keys reads it, and `[ReplicaAuthenticated]` hands the
replica to the action through the request's items, never as a user. The replica keeps the credential as
sent: its database already holds everything the primary would answer with it. Its calls to the primary
follow no redirects, which would carry the header elsewhere, and each is given up once nothing has
arrived for a minute (`ReplicationOptions.IdleTimeout`), so a connection that died without closing
cannot hold the sync, and the gate with it, for good.

A replica takes the primary's users, its own included: its admins sign in with their accounts on the
primary from then on, and the sessions of the users it had end with the copy.

## Following the log

`ReplicaSyncWorker` steps through `ReplicaSync` without waiting while there is more, then every two
seconds; a failure is recorded on `replication_source` (the admins see it) and retried after a wait that
doubles from 5 seconds to 5 minutes.

Each step reads a page of the primary's change log after the last sequence applied here
(`GET api/replication/feed/changes?after=N&fingerprint=F`, which also tells the primary how far the
replica is), fetches the bytes of every file it names that are not here yet
(`GET api/replication/feed/files/{id}/object?sha256=H`), checks each one's SHA-256 before keeping it, and applies
the page in one transaction with how far it reaches. A replica stopped anywhere picks up where it was,
and applying an entry twice is harmless. A page says whether more follow (`HasMore`); its length does
not, since a primary may give fewer entries than asked for.

`ReplicaApplier` applies an entry by its entity's type: a state is written whole by id, a removal removes.
An older state may displace a newer one (a copy holds newer states than the log replayed after it): whatever
it displaces is set aside (a file or directory to the trash, a shelf or user under a name nobody can use, a
login, grant or key removed) and a later entry puts it back, so the result is always the primary's. Every
write is logged in the replica's own change log, so its pages update live and a promoted replica keeps a
whole log.

An entry naming contents the primary no longer holds for that file is passed over: the file was purged,
or its contents swapped (encrypted or decrypted in place) before the replica fetched them. The primary
serves a file's bytes only while they are still the SHA-256 asked for, and the log goes on to the file's
removal or its newer state, which still applies even in the same page.

**Deletions reach a replica late.** A file or directory the primary purges is only moved to the trash on
the replica, which purges it with its own maintenance once the replica's restore window has passed since
it was trashed. A deletion the replica followed keeps the primary's time, so the window counts from the
deletion on the primary, not from the purge; something the replica only trashes at the purge (it never
saw the deletion) counts from then. Either way a mistaken purge on the primary (emptying the trash) does
not reach the backup at once, and the replica gives such a file back from its trash without restoring
it, which it could not, being read-only (`GET trash/{deletionId}/content`, [Storage](../Storage/Storage.md)).
A shelf's removal removes what the replica held for it at once: the primary removes only an emptied shelf.

**Swapped contents go at once.** Encrypting or decrypting a file swaps its contents in place, which the
person confirmed cannot be undone ([Storage](../Storage/Storage.md#encryption-shelfencryption)). A row's
SHA-256 changes in no other way, so when an applied state points a file here at other contents, the old
object is removed once the page (or the whole copy) commits, unless a row here still points at it, such
as another file with the same contents. Removed users' sessions end first; a removal that fails is logged
and left to maintenance, which removes whatever nothing points at.

A change of a type this build does not know (from a newer primary) stops replication with
`ReplicaOutdatedException`, saying to update, rather than being skipped. The replica's admins see that
message, and those of `PrimaryFailedException` (unreachable, refused, or an answer that cannot be used);
any other failure shows only that the server's log says why.

## Whole copies

A replica takes a whole copy when it is paired, and when the primary says it must:

- it is behind an entry compaction dropped (`change_log_state.pruned_through`): replaying what is left
  could miss a removal, or meet a row before the only entry for the parent it names;
- it is ahead of the log, as after the primary was restored from a backup;
- the entry it applied last is no longer the one under that number. A restored primary gives the numbers
  it lost to other changes, so the replica sends the fingerprint of the entry it applied last
  (`ChangeLogEntry.GetFingerprint`: its entity and instant) and the primary compares. Right after a copy
  the replica has none until it applies an entry, so a restore in that gap goes unnoticed unless the
  replica is ahead.

The copy is read from the log itself (see [ChangeLog.md](../ChangeLog/ChangeLog.md), *Compaction*), page
by page in order of sequence (`GET api/replication/feed/copy`): the newest entry of every entity, which is
its state, or its removal once it is gone. The pages go into `replication_copy`, the bytes are fetched as
they arrive, and then one transaction removes what the copy does not hold (files and directories only to
the trash, as above) and writes everything it does. `ReplicaApplier` holds, for each entity type, its table
and how to write and remove one, so the removal pass and applying entries cannot disagree on the types
there are.

The pages are not one snapshot: each is read when it is asked for, and the primary goes on changing in
between. An entity that changes after its page was read has a newer entry, so it comes again on a later
page: a newer state replaces the one copied, and a removal drops it from `replication_copy`. So the copy
ends as the primary stood when its last page was read, and a file never stays in it without the directory
it is in. The log is then followed from where it stood when the copy began, so every change made while the
copy was read is applied after it as well.

## Read-only

On a replica `ReplicaReadOnlyFilter`, applied to every controller, refuses every action but GET and HEAD
with 409 `read_only_replica`, unless it carries `[AllowedOnReplica]`: asking for a download link and
promoting. Signing in, renewing and signing out are not controller actions and work as anywhere, and so
does `/mcp`, whose tools write through the filtered API. `ReplicaWriteCoverageTests` goes through every
endpoint the server maps, minimal APIs included, and lists each one that takes a write on a replica, so a
new one is refused there, or fails that test, until somebody decides otherwise.

## Promoting

`POST api/replication/promote` on a replica removes `replication_source`: from then on the server takes
writes and may have replicas of its own. What it holds stays as it is; a whole copy in progress is dropped.
Through the API, `ReplicaSyncControl.PauseSteps` stops the step in flight rather than waiting for it; the
server's `--promote` runs in another process, which cannot, so the step finds the pairing gone when it
commits (below). It tells the primary,
which drops the replica (`DELETE api/replication/feed/pairing`); the answer says whether the primary heard,
and when it did not, the replica stays listed there until removed.

## The server's own commands

`ReplicationCommand` (`--replication-status`, `--replication-code`, `--follow`, `--forget`, `--promote`)
runs `ReplicationPairing` on a root without the web host (`AddReplicationPairing`), framed by `RootCommand`
as the user commands are. It leaves following the primary to the server: a running one finds a pairing on
its next poll. `--follow` is the one that may create a root with no database yet (`StartsARoot`), so a
replica can be paired before its first start; `RootCommand` removes it again when the pairing fails.
Promoting from another process cannot pause that server's step, so a step's transaction commits only
while the pairing it read is still there (`SetProgressAsync` matches the credential, and the step rolls
back what it applied when it does not); a whole copy's last transaction is guarded the same way,
and the pages it wrote after the promotion cleared them are cleared again. Pairing checks for shelves and
replicas once more in the transaction that pairs, since a running server can make a shelf while the
command waits on the primary, and the first copy would remove it; the primary is then told to drop the
replica it had just accepted.

## Objects and maintenance

`MaintenanceGate` is held by storage maintenance for a whole pass and by the sync for a whole step, so the
sweeps of `staging/` and of unreferenced objects never take bytes fetched but not yet named by a committed
row. A whole copy is one step, so maintenance on a replica waits for it. On a replica only a step removes
objects besides maintenance, the old contents of a swap, since every write of its own that could is
refused.

## Endpoints

| Method | Route | Who | Purpose |
|---|---|---|---|
| GET | `/api/replication` | admin | This server's replicas with how many changes each is behind, or the primary it follows. |
| POST | `/api/replication/codes` | admin, primary | A pairing code. |
| DELETE | `/api/replication/replicas/{id}` | admin, primary | Unpair a replica. |
| POST | `/api/replication/pair` | admin | Make this server a replica of the code's primary. 400 for something that is not a pairing code, or a missing or too long name; 409 for a replica, a server with shelves or replicas, a code made on this server, or a used or expired code; 502 `primary_failed` when the primary cannot be reached or used. |
| POST | `/api/replication/promote` | admin, replica | Stop following the primary. |
| POST | `/api/replication/feed/accept` | a code's secret | The primary's half of pairing; 404 for a used or expired code. |
| GET | `/api/replication/feed/changes?after=N&fingerprint=F&limit=L` | replica credential | The log after `N`, or that a copy is needed. |
| GET | `/api/replication/feed/copy?after=N&limit=L` | replica credential | A page of a whole copy. |
| GET | `/api/replication/feed/files/{id}/object?sha256=H` | replica credential | A file's bytes, live or in the trash, while they are still `H`; 404 once purged or swapped for other contents. |
| DELETE | `/api/replication/feed/pairing` | replica credential | The replica stopped following. |

The credentialed feed actions are outside the shared rate limits: a copy asks for every file one by one.
