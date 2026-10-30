# Change log

A numbered record of every change to shelves, directories and files, written for replicas to
follow. Each row holds the entity's type and id, a descriptive action, and the entity's whole state
afterwards as JSON (`null` once the entity is gone). Applying a row is an upsert or a delete by id,
so applying it twice is harmless.

The repositories' own write methods append the row, inside the transaction that made the change
(`IChangeLogRepository.AppendAsync` takes the session and never opens its own). A write and its row
therefore commit or roll back together, and no service can make a change without logging it.
