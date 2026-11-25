# Change log

A numbered record of every change to shelves, directories, files, users and their logins and
passkeys, shelf grants, shelf keys and their grants, what was read from files' contents and what
plugins keep about files ([Metadata](../Metadata/Metadata.md)), written for replicas to follow. Each
row holds the entity's type and id, the action, and the entity's whole state afterwards as JSON
(`null` once the entity is gone). A replica applies a row by its state alone: an upsert or a delete by id, so
applying it twice is harmless. It logs the row with its action, and stops at an action it does not know rather
than skip it. Live updates look at the action too, which tells a removal from a state forgotten as below
([Invalidation](../Invalidation/Invalidation.md)).

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
logs each row it removed as purged, or `ReleaseAndLogAsync`, as released (*Compaction*).

One kind of removal is the database's own and not logged: a row that goes with its owner's by a foreign
key (`ChangeEntityType.Owned`). What a plugin keeps about a file goes with the file that way, since a
replica keeps a purged file in its own trash, and the document with it.

One write of a service's own is left out on purpose: emptying a shelf's trash first moves the deletion
time of everything in it past the restore window (`ExpireTrashAsync`), so nothing of it can be restored
while it is purged. The log already holds those rows as deleted, and each purge is logged; a replica
keeps what it follows in its own trash for its own window, which logging the new time would cut short.

Credentials are logged as the server stores them: a user's `user_login` rows with the password hash,
a passkey with its public key, and a key with the SHA-256 of its secret. A passkey's sign count and
last use are left out: they belong to the server that saw the sign-in, as its sessions do. A replica
holds every file already, and a replica promoted to primary must let its users sign in, so it is trusted with them too; the replication feed that
carries the log needs a replica's credential ([Replication](../Replication/Replication.md)).

## Compaction

`ChangeLogCompactionWorker` compacts the log hourly rather than cutting it (`IChangeLogRepository.CompactAsync`,
by `ChangeLogRetention`): an entry older than 30 days goes once a newer entry for the same entity exists, and a
removal goes once it is that old, as does an entry of what went with its owner unlogged
(`ChangeEntityType.Owned`). So the newest entry of every entity that still exists stays, and a whole copy of the
state is read from the log itself (`GetLatestStatesPageAsync`: the newest entry of each entity, and so the
removal of one removed recently, which a copy read page by page needs to drop what an earlier page gave it).

What tells of plain contents a file was encrypted away from goes after 24 hours instead
(`ChangeLogRetention.PlainTraces`), so after at most about 25 hours while the server runs. Anyone with the
database could otherwise check whether the shelf held a given known file:

- a file's entries whose state is not encrypted (`ChangeEntityType.EncryptedFlag`), with the plain contents'
  SHA-256 and size, 24 hours after the first later entry of the file whose state is. The rule goes by the
  state and not by the `swapped` action, since a replica that took a whole copy logs the file's newer state
  without one;
- the entries of what was read from those contents (`ChangeEntityType.ReadFromContents`), whose ids are made
  from the SHA-256, 24 hours after the swap removed it. That removal is logged as `released`
  (`ChangeAction.Released`) rather than `purged`, and the rule takes it and the states forgotten before it.
  What is removed in any other way, as contents no file holds any more go, keeps the 30 days: the files
  that held them keep their own entries that long anyway.

The 24 hours count from when this server logged the swap, so a replica that applies it late keeps those
entries a day from then. Only the file encrypted counts: another file that held the same plain contents, or
still holds them, keeps its entries for the 30 days like any. The rules apply to every entry, whenever it was
made.

Clearing those entries in the swap's own transaction instead would change what the log promises followers: a
blanked file state reads as a removal, an entry deleted at a replica's position forces a whole copy, and
removals would have to be worked out on the replica. So they wait a day for compaction.

A follower replaying the log across a dropped entry could miss a removal, or meet a row before the only entry
left for the parent it names, so `change_log_state.pruned_through` keeps the highest sequence dropped: a
replica behind it takes a whole copy, and one at it follows on
([Replication](../Replication/Replication.md#whole-copies)). The newest sequence comes from SQLite's
`AUTOINCREMENT` counter, never `MAX(sequence)`, so compaction cannot lower it.

A replica compacts its own log by the same rules, as every write it applies is logged there, a release as
`released`.
