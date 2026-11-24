# Change log

A numbered record of every change to shelves, directories, files, users and their logins and
passkeys, shelf grants, shelf keys and their grants, what was read from files' contents and what
plugins keep about files ([Metadata](../Metadata/Metadata.md)), written for replicas to follow. Each
row holds the entity's type and id, the action, and the entity's whole state afterwards as JSON
(`null` once the entity is gone). A replica looks at the state alone: applying a row is an upsert or a
delete by id, so applying it twice is harmless. Live updates look at the action too, which tells a removal
from a state forgotten as below ([Invalidation](../Invalidation/Invalidation.md)).

What was read from contents, by the server or a plugin's reader, and what a plugin keeps about a file are
the kinds of entity whose earlier states the log forgets when one changes or is removed
(`ChangeEntityType.ForgetsEarlierStates`): its earlier entries' payloads are blanked in the transaction that
logs the new one, so a replica takes each for a removal, and one that replays one meets the newest after it. A
photo encrypted in place loses its row at once, and its position and camera must not stay in the log, on the
primary or a replica, for the compaction window below. A plugin's document may be 64 KiB and written any
number of times, so its earlier states would grow the log without a bound.

Download and upload links (`shelf_link`) are not logged: each server keeps the links it signed
itself, and the database removes one with what it names ([Storage](../Storage/Storage.md#links-shelf_link-linkservice)).

The repositories' own write methods append the row, inside the transaction that made the change
(`IChangeLogRepository.AppendAsync` takes the session and never opens its own). A write and its row
therefore commit or roll back together, and no service can write a change without logging it. A
removal of many rows goes through `DeleteAndLogAsync`, which runs the `DELETE … RETURNING id` and
logs each row it removed as purged.

One kind of removal is the database's own and not logged: a row that goes with its owner's by a foreign
key (`ChangeEntityType.Owned`). What a plugin keeps about a file goes with the file that way, since a
replica keeps a purged file in its own trash, and the document with it.

One write of a service's own is left out on purpose: emptying a shelf's trash first moves the deletion time of everything in
it past the restore window (`ExpireTrashAsync`), so nothing of it can be restored while it is purged. The
log already holds those rows as deleted, and each purge is logged; a replica keeps what it follows in its
own trash for its own window, which logging the new time would cut short.

Credentials are logged as the server stores them: a user's `user_login` rows with the password hash,
a passkey with its public key, and a key with the SHA-256 of its secret. A passkey's sign count and
last use are left out: they belong to the server that saw the sign-in, as its sessions do. A replica
holds every file already, and a replica promoted to primary must let its users sign in, so it is trusted with them too; the replication feed that
carries the log needs a replica's credential ([Replication](../Replication/Replication.md)).

## Compaction

`ChangeLogCompactionWorker` compacts the log daily rather than cutting it: an entry older than 30 days
(`ChangeLogCompactionWorker.Retention`) goes once a newer entry for the same entity exists, and a removal
goes once it is that old, as does an entry of what went with its owner unlogged
(`ChangeEntityType.Owned`). So the newest entry of every entity that still exists stays, and a whole
copy of the state is read from the log itself (`GetLatestStatesPageAsync`: the newest entry of each entity,
and so the removal of one removed recently, which a copy read page by page needs to drop what an earlier
page gave it). A follower replaying the log across
a dropped entry could miss a removal, or meet a row before the only entry left for the parent it names, so
`change_log_state.pruned_through` keeps the highest sequence dropped: a replica behind it takes a whole
copy. The newest sequence comes from SQLite's `AUTOINCREMENT` counter, never `MAX(sequence)`, so
compaction cannot lower it.
