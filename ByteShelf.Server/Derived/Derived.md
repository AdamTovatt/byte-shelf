# Derived files

What plugins make from files' contents, kept and served by the server: thumbnails and previews, for
example. The kinds are `ByteShelfCommon.DerivedKind`:

| Kind | What it is | Made |
|---|---|---|
| `thumbnail` | A small picture for listings | after an upload, and for files already stored |
| `preview` | What a viewer shows until it has the contents | with the thumbnail, from one run |
| `full` | The whole contents in a type a browser shows | when asked for |
| `agent` | What an agent is shown: a PNG, JPEG, GIF or WebP, as it is shown nothing else ([Mcp](../Mcp/Mcp.md)) | when asked for |

A server without plugins that make them has none, and asking for one is a 404.

## Who makes what (`DerivedMakers`)

Everything derived files know of plugins comes from `IDerivedFilePlugins`. A plugin has one maker
(`ByteShelf.Plugins.IDerivedFileMaker`, added with `AddMaker`, [Plugins](../Plugins/Plugins.md)), declared with
each kind it makes, the media types it makes it from and a version. Of two plugins making one kind, one that
takes the type exactly beats one that takes its family, then the first by id.

Identical contents are stored once and have one derived file of a kind, so the maker is chosen by
every type plain files hold the contents as, and is the same whichever of them is asked about. A file
whose own type nothing takes has none, though another holds the same contents.

## On disk (`DerivedStore`)

`derived/<kind>/ab/cd/<sha256>` in the root, by the SHA-256 of the contents it was made from, and beside
it `<sha256>.json` (`DerivedEntry`): the plugin and version that made it and its media type, or that
making it failed. Nothing about them is in the database.

They are a cache: not replicated, not in backups, not counted in quota, not found by search. Empty the
folder and they are made again, failures tried again too (with the server stopped: a run under way works in
`derived/work`). What another version made, or failed to make, is made again.

| Kind | The most one may take (`DerivedFileLimits.MaxBytesOf`) | Together |
|---|---|---|
| `thumbnail` | 1 MiB | no limit |
| `preview` | 16 MiB | no limit |
| `full` | 512 MiB | 2 GiB (`DerivedStore.MaxFullBytes`), the least recently used going first, a file's modification time being when it was last used. The newest stays whatever its size, and one in use until the next trim. |
| `agent` | 1 MiB | no limit |

A maker's write that would pass that throws, and the kind has failed unless the maker makes it smaller. What is in
its place in the end is kept only if it is a file within that, whatever put it there.

## Making them (`DerivedMaking`, `MakerRun`)

A maker runs in its plugin's process ([Processes](../Plugins/Processes/Processes.md)), for one contents at a
time, with all the kinds to be made from one read (`MakingGroup`). It writes each to a file that the process
opens and limits for it (`MadeFiles`); one it did not finish is not kept. However many ask for one, it is made
once, and goes on though nobody waits. What fails because of the contents is kept as a failure; where they are not to
blame nothing is kept, and the next one to ask, or the next pass, tries again.

`DerivedBackground` makes thumbnails and previews of live plain files: of contents as uploads and a
replica's sync store them (`StoredContents`), and of everything already stored, as the job
`derived.making`, at startup and hourly. A replica makes its own.

## Serving them (`DerivedFileService`)

`GET files/derived?kind=` by `path` or `id`, with the file's read permission, and
`GET trash/{deletionId}/derived?id=&kind=` for a file in the trash; both also under `/api/open`. Sent as
contents are (`nosniff`, the sandbox), with an ETag of the kind, the contents, the plugin and the version,
and `Cache-Control: private, max-age=3600`, so a browser may show one for up to an hour after the file is
encrypted.

One that is missing is made and waited for, 10 seconds at most; then it is a 503 `not_ready` with
`Retry-After: 5` while making goes on, as it is when the run is stopped. 404 when nothing makes it, when it
could not be made, and for an encrypted file, even when a plain file holds the same bytes. 500 when the
plugin could not run, or what it made could not be written.

Listings, trees, searches, a deletion's tree and a file's own record name the kinds there are of each file, with a
word for each that changes when another is made in its place (`ShelfFile.Derived`, `DerivedListing`): so a
client asks for none that has yet to be made. Naming them makes nothing. What is on disk of contents is read once,
for at most 100,000 contents, and again once the server changes it (`KnownEntries`). One that came or went is told
to open listings ([Invalidation](../Invalidation/Invalidation.md)).

The web UI adds a query parameter `v` to the address an `<img>` loads one from, which the server does not read:
what tells that one from any other at its place, so that the browser, which shows its copy for an hour, never
shows another's.

## Removing them (`DerivedRemoval`)

**A derived file shows what encryption hides, so it never outlives the plaintext.** What was derived from
contents goes with the last plain file, live or in the trash, that holds them. An encrypted file with the
same bytes does not count.

- A swap (encrypting or decrypting in place) removes it **inside its transaction, before it commits**
  (`CommitReleasingAsync`, the one way to commit a swap), and holds the contents' lock until it has committed:
  every process that read them is ended and what it left removed, metadata's readers included
  ([Processes](../Plugins/Processes/Processes.md)), and none is given them while the rest of the server still
  sees the file as plain. What cannot be removed refuses the swap. If the commit then fails, what was removed
  is made again.
- A replica does the same in the transaction that applies the swap; what cannot be removed stops its sync
  until it can.
- A run starts (`PluginProcesses`), and what it made is kept (`MakerRun`), only where a plain file holds the
  contents (`IPlainContentsRepository`), asked under that lock.
- A purge removes it once it has committed, and so does whatever else removes an object
  (`ObjectCollector`). One that fails leaves the rest to maintenance.

The job `derived.maintenance`, at startup and daily, removes what is left of contents no plain file holds (by a
crash, a removal that failed, or a shelf removed on a replica, which takes its files without looking at what
they held), what plugins' processes have of them included, and what a crash left without an entry. It also removes what no installed plugin makes any more,
and trims `full`.
