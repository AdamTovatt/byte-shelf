# Backup

`ByteShelf.Server --backup <zip>` writes a consistent zip of the root at `BYTESHELF_ROOT` while the server
runs on it (`RootBackup`, run by `BackupCommand`). It opens the root with `RootFolder.OpenExisting`, so
it never migrates the database or creates folders there, and it refuses a zip path inside the root.

1. **Snapshot.** `VACUUM INTO` copies the database as one read transaction sees it, into a file beside
   the zip created readable by its owner only.
2. **Objects.** Every object a file in the snapshot points at, trashed files included, goes into the zip
   under `objects/ab/cd/<sha256>`, its SHA-256 checked on the way. An object that does not match, or is
   missing while the live database still points at it, fails the backup, so damage is not carried into
   it unnoticed. An object can go missing after the snapshot in two ways. A live file whose contents
   were swapped meanwhile (encrypted or decrypted in place, which removes the old ones at once) is
   backed up with the contents it holds now, its snapshot row pointed at them and logged as swapped. The
   other files on that object were purged by the server, so they are purged from the snapshot through
   `FileRepository`, logged as the server logged them.
3. **The snapshot itself**, without uploads in progress (their staged bytes are not copied), as
   `byteshelf.db`, then every file in `keys/`.

The zip holds every secret the server has: the password hashes, the sign-in and link secrets
in `keys/` (the link secret signs share and upload links), and on a replica the credential it follows its primary with, in plain text in the
database. Keep it as safe as the root itself.

The zip is written as `<zip>.partial` and renamed into place only when whole, so a failed backup leaves
nothing behind and never replaces a good one. `<zip>.lock` is held exclusively for the whole run, so a
second backup to the same path fails rather than sharing those files. It is created readable by its owner only, and the database
and keys inside it unzip that way.

`BackupCommand` answers the exit code: 0 written, 1 failed with nothing written, 2 when the zip's path or
`BYTESHELF_ROOT` is missing.

A backup restored onto a primary gives its log numbers it may already have given other changes after the
backup; its replicas notice and take a whole copy ([Replication](../Replication/Replication.md)). One
that has applied no entry since its last copy has nothing to compare, so it notices only if it is ahead
of the restored log; until then it may hold changes the restore undid.
