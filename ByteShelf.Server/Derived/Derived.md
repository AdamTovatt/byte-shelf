# Derived files

What plugins make from files' contents, kept and served by the server: thumbnails and previews, for
example. The kinds are `ByteShelfCommon.DerivedKind`:

| Kind | What it is | Made |
|---|---|---|
| `thumbnail` | A small picture for listings | after an upload, and for files already stored |
| `preview` | What a viewer shows of a file before its contents or beside them: an image's smaller copy, a video's storyboard | with the thumbnail, from one run |
| `full` | The whole contents in a type a browser shows | when asked for |
| `agent` | What an agent is shown: a PNG, JPEG, GIF or WebP, as it is shown nothing else ([Mcp](../Mcp/Mcp.md)) | when asked for |
| `stream` | The contents remade for streaming, in a type a browser plays and seeks | after an upload, and for files already stored; never when asked for, as it may take hours |

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
it `<sha256>.json` (`DerivedEntry`): the plugin and version that made it, and how that ended
(`DerivedOutcome`): made, with its media type; failed; or made but not kept for want of room, with how many
bytes it took. In the database is only how many bytes each one kept takes (`derived_size`,
`IDerivedSizeRepository`), for quotas, written with the files (`DerivedFiles`).

They are a cache: not replicated, not in backups, not found by search. Empty the folder and they are made
again, failures tried again too (with the server stopped: a run under way works in `derived/work`). What
another version made, or failed to make, is made again.

## Quota

What was made of contents counts in the quota of every shelf with a file row, live or in the trash, that
holds them, once a row, as a file's own bytes do ([Shelves](../Storage/Shelves.md)). One is kept only if
every such shelf has room for what it adds, its bytes less those of the one it replaces, asked in the
transaction that keeps it (`DerivedRoom`, `IShelfRepository.HasRoomForMoreOfAsync`), so two keeping at once
cannot both take the last of the room. Otherwise it is not kept, nor is the one it would replace, and its
entry says how many bytes it took (`DerivedOutcome.NoRoom`), which is not a failure: asking for it is a 507
`quota_exceeded` while a shelf that holds the contents has no room for as many, and makes it again once every
one has, but for a `stream`, which is never made when asked for. What is made in the background is also made
again by its hourly pass once every such shelf has room, by the room each shelf had as the pass began. Of kinds
made in one run, each is weighed in turn, so a thumbnail may be kept where its preview is not.

An upload's reservation is measured against usage that includes derived files. An upload of contents
something was made of already is refused as it finishes, with 507, when its row with those files would put
its shelf over (`UploadService`).

`derived_size` is written in the step that keeps or removes a file, is the server's own as the folder is
(never logged, replicated or backed up, so a replica counts what it made), and is brought in line with the
folder by `derived.maintenance`, at startup and daily: an emptied or edited folder counts what is there.
What fails between the database and the folder counts wrongly until then. A `full` copy trimmed stops
counting.

Whatever writes them begins its transaction before it takes the contents' lock (`ContentsTransactions`), as
a swap does, since a swap holds the database while it waits for that lock. So what a run made is taken out
of its process's folder into one of its own, noting the contents, and kept only once the run is over
(`TakenFiles`): a removal of the contents finds and removes it as it does a process's folder, and the
keeping, which waits for the removal's commit, then finds no plain file holding them.

| Kind | The most one may take (`DerivedFileLimits.MaxBytesOf`) | Together |
|---|---|---|
| `thumbnail` | 1 MiB | no limit |
| `preview` | 16 MiB | no limit |
| `full` | 512 MiB | 2 GiB (`DerivedStore.MaxFullBytes`), the least recently used going first, a file's modification time being when it was last used. The newest stays whatever its size, and one in use until the next trim. |
| `agent` | 1 MiB | no limit |
| `stream` | 8 GiB | no limit; never trimmed |

A maker's write that would pass that throws, and the kind has failed unless the maker makes it smaller. What is in
its place in the end is kept only if it is a file within that, whatever put it there.

## Making them (`DerivedMaking`, `MakerRun`)

A maker runs in its plugin's process ([Processes](../Plugins/Processes/Processes.md)), for one contents at a
time, with all the kinds to be made from one read (`MakingGroup`). The maker writes each to a file that the
process opens and limits for it (`MadeFiles`); one it did not finish is not kept. However many ask for one, it is
made once, and goes on though nobody waits. What fails because of the contents is kept as a failure; where they
are not to blame nothing is kept, and the next one to ask, or the next pass, tries again. A maker that could not
run, whatever the contents, is logged, and admins see why with its plugin (`PluginProblems`,
[Plugins](../Plugins/Plugins.md)) until a later run of its in the same lane has run to its end, which in the long
lane may be hours after it began.

How each group is made is said in one place, the group itself (`MakingGroup`): its kinds; what has it made
(`MakingStart`): someone asking for a missing one, the background's job, or both; and the lane its runs take
(`PluginLane`). A `stream` is a group of its own in the long lane: a process and a turn of its own beside the
plugin's other runs, 24 hours a run, at the lowest priority. A restart of the server ends such a run with nothing
kept, and the next pass starts it over. Every other group is in the quick lane, 60 seconds a run.

`DerivedBackground` makes the groups made in the background (`MakingGroup.InBackground`: thumbnails with previews,
and streams) of live plain files: of contents as uploads and a replica's sync store them (`StoredContents`, an
`IStoredContentsListener`), and of everything already stored, as the jobs `derived.making` and
`derived.stream_making`, at startup and hourly. Each plugin and group goes at its own pace, so streams being made
for days hold up no thumbnail. A replica makes its own.

## Serving them (`DerivedFileService`)

`GET files/derived?kind=` by `path` or `id`, with the file's read permission, and
`GET trash/{deletionId}/derived?id=&kind=` for a file in the trash; both also under `/api/open`. Sent as
contents are (`nosniff`, the sandbox), with an ETag of the kind, the contents, the plugin and the version,
and `Cache-Control: private, max-age=3600`, so a browser may show one for up to an hour after the file is
encrypted.

One that is missing is made and waited for, 10 seconds at most; then it is a 503 `not_ready` with
`Retry-After: 5` while making goes on, as it is when the run is stopped. A missing `stream` is not made: it is a
503 `not_ready` at once with `Retry-After: 60`, while the background has yet to make it or is making it; a 404 when
the background makes none of the contents (`DerivedBackground.MakingOfAsync`, by the same files its backlog takes),
as once only the trash holds them; and a 500 while the plugin's long runs could not run when it last had one to
do. 404 when nothing makes it, when it could not be made, and for an encrypted file, even when a plain file holds
the same bytes. 507 when it was made with no room to keep it and a shelf that holds the contents still has none
(see Quota). 500 when the plugin could not run, or what it made could not be written.

Listings, trees, searches, a deletion's tree and a file's own record name the kinds there are of each file, with a
word for each that changes when another is made in its place (`ShelfFile.Derived`, `DerivedListing`): so a
client asks for none that has yet to be made. Naming them makes nothing. What is on disk of contents is read once,
for at most 100,000 contents, and again once the server changes it (`KnownEntries`). One that came or went is told
to open listings ([Invalidation](../Invalidation/Invalidation.md)). ByteShelfCommon reads them without the kinds
it does not know (`KnownEnumKeysConverter`), so a kind added later fails none of its listings. Versions up to
4.0.1 fail on any kind they do not know: on a server whose plugins make streams, a client of one cannot list a
folder that holds a file with one.

The web UI adds a query parameter `v` to the address an `<img>` loads one from, which the server does not read:
what tells that one from any other at its place, so that the browser, which shows its copy for an hour, never
shows another's.

## Removing them (`ContentsTraces`)

**A derived file shows what encryption hides, so it never outlives the plaintext.** What was derived from
contents goes with the last plain file, live or in the trash, that holds them. An encrypted file with the
same bytes does not count.

- A swap (encrypting or decrypting in place) and a purge of files remove it **inside their transaction,
  before it commits**, as committing a `ContentsSession` does ([Storage](../Storage/Storage.md)), and hold
  the contents' lock until it has committed: every process that read them is ended and what it left
  removed, metadata's readers included ([Processes](../Plugins/Processes/Processes.md)), and none is given
  them while the rest of the server still sees the file as plain. What cannot be removed refuses the swap or
  the purge. If the commit then fails, what was removed is made again.
- A replica does the same in the transaction that applies a swap or removes a shelf; what cannot be removed
  stops its sync until it can.
- A run starts (`PluginProcesses`), and what it made is kept (`MakerRun`), only where a plain file holds the
  contents (`IPlainContentsRepository`), asked under that lock.
- Storage maintenance, removing an object no row points at (`ObjectCollector`), removes it too. One that
  fails leaves the rest to the next pass.

The job `derived.maintenance`, at startup and daily, removes what is left of contents no plain file holds (by
a crash, or a removal that failed), what plugins' processes have of them included, and what a crash left
without an entry. It also removes what no installed plugin makes any more, trims `full`, and counts the sizes
of what is left again.
