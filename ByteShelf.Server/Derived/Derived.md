# Derived files

What plugins make from files' contents, kept and served by the server: thumbnails and previews, for
example. The kinds are `ByteShelfCommon.DerivedKind`:

| Kind | What it is | Made |
|---|---|---|
| `thumbnail` | A small picture for listings | after an upload, and for files already stored |
| `preview` | What a viewer shows until it has the contents | with the thumbnail, from one run |
| `full` | The whole contents in a type a browser shows | when asked for |
| `agent` | What an agent is shown | when asked for |

A server without plugins that make them has none, and asking for one is a 404.

## Who makes what (`DerivedMakers`)

Everything derived files know of plugins comes from `IDerivedFilePlugins`. A plugin has one maker
(`IDerivedFileMaker`), which declares each kind it makes with the media types it makes it from and a
version. Of two plugins making one kind, one that takes the type exactly beats one that takes its family,
then the first by id.

Identical contents are stored once and have one derived file of a kind, so the maker is chosen by
every type plain files hold the contents as, and is the same whichever of them is asked about. A file
whose own type nothing takes has none, though another holds the same contents.

## On disk (`DerivedStore`)

`derived/<kind>/ab/cd/<sha256>` in the root, by the SHA-256 of the contents it was made from, and beside
it `<sha256>.json` (`DerivedEntry`): the plugin and version that made it, its media type and the
original's width, height and type, or that making it failed. Nothing about them is in the database.

They are a cache: not replicated, not in backups, not counted in quota, not found by search. Empty the
folder and they are made again, failures tried again too (with the server stopped: a run under way works in
`derived/work`). What another version made, or failed to make, is made again.

| Kind | The most one may take (`DerivedKinds.MaxBytesOf`) | Together |
|---|---|---|
| `thumbnail` | 1 MiB | no limit |
| `preview` | 16 MiB | no limit |
| `full` | 512 MiB | 2 GiB (`DerivedStore.MaxFullBytes`), the least recently used going first, a file's modification time being when it was last used. The newest stays whatever its size, and one in use until the next trim. |
| `agent` | 1 MiB | no limit |

One larger than that has failed.

## Making them (`DerivedMaking`, `MakerRuns`)

Each run is a child process of the server's own executable, `--make-derived <folder>`
(`MakeDerivedCommand`, started by `MakerProcess`), for one contents, with all the kinds to be made from
one read (`MakingGroup`): a native decoder of untrusted bytes can hang or crash there without the server
doing so. The server itself has no instance of a maker, only what it declares. The child's memory has no limit
of its own.

- One at a time per plugin, those asked for first, then contents just stored, then the backlog
  (`MakingTurns`). However many ask for one, it is made once, and goes on though nobody waits.
- Killed after 60 seconds. A run that is killed, crashes or throws has failed, and the failure is kept.
  Where the contents are not to blame nothing is kept: the next one to ask, or the next pass, tries again.
  That is when the plugin does not load, is another version than the server loaded or ends before its maker
  runs, when the contents cannot be opened, and when the disk is full, as far as the maker's error says so
  (any other I/O error is as good as damaged contents).
- It works in `derived/work/<sha256>.<id>` (`MakingFolder`), its temporary files included, which goes with
  the run; a server that starts empties it. One that cannot be removed is logged, and found again by its
  name when the contents are to leave nothing behind.
- Its output is thrown away and an exception is logged by its type alone, since either may quote the
  contents. Its memory is left out of the dumps .NET makes, and on Linux out of the system's core dumps.
- It ends when the server does, however the server ends, and takes the processes it started with it, as a
  run that ends in any way does: it leads a process group of its own (`ProcessGroup`). On Windows, which has
  none, what a maker started may outlive one that crashes, or the server when it is killed.
- A server that begins to stop ends every run first (`MakingLifetime`), and a run ignores the signals a
  system sends to stop things, so stopping is never taken for the contents' doing.

`DerivedBackground` makes thumbnails and previews of live plain files: of contents as uploads and a
replica's sync store them (`StoredContents`), and of everything already stored, as the job
`derived.making`, at startup and hourly. A replica makes its own.

## Serving them (`DerivedFileService`)

`GET files/derived?kind=` by `path` or `id`, with the file's read permission, and
`GET trash/{deletionId}/derived?id=&kind=` for a file in the trash; both also under `/api/open`. Sent as
contents are (`nosniff`, the sandbox), with an ETag of the kind, the contents, the plugin and the version,
and `Cache-Control: private, max-age=3600`, so a browser may show one for up to an hour after the file is
encrypted, as it may the original.

One that is missing is made and waited for, 10 seconds at most; then it is a 503 `not_ready` with
`Retry-After: 5` while making goes on, as it is when the run is stopped. 404 when nothing makes it, when it
could not be made, and for an encrypted file, even when a plain file holds the same bytes. 500 when the
plugin could not run, or what it made could not be written.

## Removing them (`DerivedRemoval`)

**A derived file shows what encryption hides, so it never outlives the plaintext.** What was derived from
contents goes with the last plain file, live or in the trash, that holds them. An encrypted file with the
same bytes does not count.

- A swap (encrypting or decrypting in place) removes it **inside its transaction, before it commits**
  (`CommitReleasingAsync`, the one way to commit a swap), and holds the lock of what is derived from those
  contents until it has committed: a run of theirs is killed and waited for, its folder removed with any an
  earlier run left, and none starts while the rest of the server still sees the file as plain. What
  cannot be removed refuses the swap. If the commit then fails, what was removed is made again.
- A replica does the same in the transaction that applies the swap; what cannot be removed stops its sync
  until it can.
- A run starts, and what it made is kept, only where a plain file holds the contents, asked under that
  lock.
- A purge removes it once it has committed, and so does whatever else removes an object
  (`ObjectCollector`). One that fails leaves the rest to maintenance.

The job `derived.maintenance`, at startup and daily, removes what is left of contents no plain file holds (by a
crash, a removal that failed, or a shelf removed on a replica, which takes its files without looking at what
they held) and what a crash left without an entry. It also removes what no installed plugin makes any more,
and trims `full`.
