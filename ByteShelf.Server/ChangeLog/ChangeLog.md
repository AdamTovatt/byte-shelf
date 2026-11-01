# Change log

A numbered record of every change to shelves, directories, files, users and their logins, shelf
grants and shelf keys, written for replicas to follow. Each row holds the entity's type and id, a
descriptive action, and the entity's whole state afterwards as JSON (`null` once the entity is
gone). Applying a row is an upsert or a delete by id, so applying it twice is harmless.

The repositories' own write methods append the row, inside the transaction that made the change
(`IChangeLogRepository.AppendAsync` takes the session and never opens its own). A write and its row
therefore commit or roll back together, and no service can make a change without logging it. A
removal of many rows goes through `DeleteAndLogAsync`, which runs the `DELETE … RETURNING id` and
logs each row it removed as purged.

One write is left out on purpose: emptying a shelf's trash first moves the deletion time of everything in
it past the restore window (`ExpireTrashAsync`), so nothing of it can be restored while it is purged. The
log already holds those rows as deleted, and each purge is logged; a replica keeps what it follows in its
own trash for its own window, which logging the new time would cut short.

Credentials are logged as the server stores them: a user's `user_login` rows with the password hash,
and a key with the SHA-256 of its secret. A replica holds every file already, and a replica promoted
to primary must let its users sign in, so it is trusted with them too; the replication feed that
carries the log needs a replica's credential ([Replication](../Replication/Replication.md)).

## Compaction

`ChangeLogCompactionWorker` compacts the log daily rather than cutting it: an entry older than 30 days
(`ChangeLogCompactionWorker.Retention`) goes once a newer entry for the same entity exists, and a removal
goes once it is that old. So the newest entry of every entity that still exists stays, and a whole copy
of the state is read from the log itself (`GetLatestStatesAfterAsync`: the newest entry of each entity,
and so the removal of one removed recently, which a copy read page by page needs to drop what an earlier
page gave it). A follower replaying the log across
a dropped entry could miss a removal, or meet a row before the only entry left for the parent it names, so
`change_log_state.pruned_through` keeps the highest sequence dropped: a replica behind it takes a whole
copy. The newest sequence comes from SQLite's `AUTOINCREMENT` counter, never `MAX(sequence)`, so
compaction cannot lower it.
