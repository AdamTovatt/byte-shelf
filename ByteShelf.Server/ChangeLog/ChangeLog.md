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

Credentials are logged as the server stores them: a user's `user_login` rows with the password hash,
and a key with the SHA-256 of its secret. A replica holds every file already, and a replica promoted
to primary must let its users sign in, so it is trusted with them too; whatever carries the log to a
replica has to authenticate it.
