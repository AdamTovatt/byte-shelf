# Metadata

What a file's contents say about it, read by the server so it can be found without anyone describing
it: when a photo or video was taken, where (GPS coordinates; place names would need a geocoding
service and are left out), with what camera, how long a video plays, and a PDF's title and pages.
`GET files/metadata` answers it as `ByteShelfCommon.FileMetadata`, and get_info shows it.

Plugins' readers find more, and plugins keep documents of their own about files; both are in the same
answer, by plugin id, and below (*Plugins' documents*).

The server's own reader reads files of the types in `ReadMediaTypes`: `image/*`, `video/*`, `audio/*` and
`application/pdf`. `FileMetadata.Readable` says whether a file is (one of those types and not
encrypted); `ReadAt` is null until it has been read, and for good when it is not readable.

## Reading (`MetadataReader`)

Managed libraries only, with no native tool and nothing over the network: MetadataExtractor for EXIF
(JPEG, HEIC, PNG, WebP and the rest it knows) and QuickTime/MP4, PdfPig for PDFs. Both are server
only and never ship in `ByteShelfClient`. The format is told by the contents' own bytes, not the type
the file was stored with. Only headers are read: no image's pixels are decoded and no page drawn.

- **Taken at**: EXIF's original time, else its digitized one, with the offset the camera wrote for it
  (EXIF 2.31). A camera that wrote none gives its clock's time, kept as if it were UTC. Then Apple's
  QuickTime creation date, then the movie header's, which is UTC; zero there means unknown.
- **Position**: EXIF's GPS, else QuickTime's ISO 6709 location; exactly 0, 0 is a camera without a
  fix and counts as none.
- **Duration**: the QuickTime movie header's, so QuickTime and MP4 files have one; other sound
  formats (MP3, WAV, FLAC) are read but carry none MetadataExtractor gives.
- **Camera**: make and model, from EXIF or QuickTime; the model alone when it already names the make.
- **Title** and **pages**: a PDF's document information and page count.

Text is cleaned of control characters and cut to `MetadataReader.MaxTextLength` code points, between
characters (an emoji with its modifiers stays whole); v0012 checks the same limit, which on a replica
only the database holds.

## Storing (`object_metadata`)

One row per object, keyed by its SHA-256 as the object store is, so identical contents are read once
whatever number of files hold them, with the version of the server's reader that did. A row with nothing
found still says the contents were read.
Contents a parser fails on are damaged; since objects never change, they are recorded as carrying
nothing, and only another version of the reader reads them again. What a version reads replaces what
another found, nothing included. The title and camera are kept folded beside the text (as a file's
name is), for search.

A file shows its contents' row unless it is **encrypted**: the server never sees an encrypted file's
plaintext, and metadata read from it would leak what encryption hides. Such files are never read, and
`GET files/metadata` answers nothing read for one even when a plain file holds the same bytes; what plugins
keep about the file itself it still has.

**What was read from plaintext never outlives it**, whichever reader read it:

- A file encrypted in place drops what was read from its plain contents in the swap's transaction
  (`FileRepository.SwapContentAsync`, the one way to swap contents, for a backup too, through
  `MetadataUpkeep`, which knows every table of metadata), unless another plain file, live or in the
  trash, holds them, and the change log forgets those rows' earlier states
  ([Change log](../ChangeLog/ChangeLog.md)). An encrypted file with the same bytes does not count.
- Those removals are in the log **before** the swap, so a replica, which applies the log in pages, never
  holds a reading of contents it has applied the swap of, wherever a page ends and whether or not it ever
  fetched them.
- A read that fails is logged by its exception's type alone: a message may quote the contents, and the
  server's log forgets nothing.
- A reading is recorded only where a live plain file holds the contents in the same transaction
  (`PlainContents.HeldByLiveFile`, part of the write itself), since they may have been encrypted or
  deleted while they were read.

## Search

`FileCriteria`, which search and the search for duplicates share, asks about metadata with
`TakenFrom` and `TakenBefore` (a start included, an end excluded), `HasGps`, `MinDurationSeconds`
and `MaxDurationSeconds`, and `MinPageCount` and `MaxPageCount` (inclusive), and its text matches
the title and camera as it does a name. The server's query joins each file to its contents' row,
none for an encrypted file (`SearchCriteria.Join`, in Storage/Repositories), so a file not read
yet, or never to be read, fails every metadata criterion.

## Plugins' documents

A plugin keeps JSON objects of its own (`ByteShelfCommon.PluginDocument`: at most 64 KiB in UTF-8, counted
without whitespace and with text as itself rather than as escapes, and nested at most 32 deep), of two kinds
that follow different things:

| | About the contents | About the file |
|---|---|---|
| Table | `content_plugin_metadata`, by plugin and SHA-256 | `file_plugin_metadata`, by plugin and file |
| Written by | the plugin's reader, in the background, with its version | whoever may write the file, over a version they name |
| Encrypted in place | erased, the log's earlier states too | kept, as a description is |
| Stored over | read again from the new contents | kept: the new file gets it, at the same version |

**A plugin must not put what it read from a file's plaintext into a document about the file**: that one is
kept when the file is encrypted, so it would outlive the plaintext. What is read from contents goes into the
document about them, through the plugin's reader.

A write of a document about a file names the version it replaces, and a removal is a write too: the row stays
with a null document, so no version of it is given twice and a write made against one that has since been
removed is refused.

The log keeps only the newest state of a document of either kind
([Change log](../ChangeLog/ChangeLog.md)), so writing one over and over does not grow it.

Documents do not count toward a shelf's quota, as descriptions and what the server itself read do not: storing
a file over another copies up to 64 KiB per plugin whatever the new file's size.

Everything metadata knows of plugins comes from what loads them ([Plugins](../Plugins/Plugins.md)):
`InstalledPlugins`, what they declare, and `IPluginReaders`. A command and a backup do not know them
(`KnownPlugins.NotKnown`), so they can remove documents but not write one (`PluginFieldValueRepository`
refuses): its values would be lost. A plugin that is no longer loaded keeps its documents, invisible until it
is back: those about a file go with it, those about contents are dropped by the job like any.
`ByteShelf.Server --forget-plugin <id>` (`PluginMetadataCommand`) deletes both kinds, logged, with the
versions of those about files, and the log forgets their states; a replica refuses it.

`PUT files/plugin-metadata?plugin=` writes the one about a file (`FileMetadataService`), whole, by anyone who
may write the file and only for an installed plugin; another version than the named one is a 409, and the
file's `updatedAt` stays. Nothing over HTTP writes one about contents.

**A document about a file goes with the file's row by a foreign key, and nothing logs that**, so a replica
that keeps a purged file in its own trash keeps the document too. Reading that trash does not give it: only
promoting the replica and restoring the file brings it back. Its newest log entry would then stay for
good and come with every whole copy, so compaction drops it once its row is gone, a replica passes over a
document whose file it does not have, and a copy does not remove the document of a file it does not hold
([Change log](../ChangeLog/ChangeLog.md), [Replication](../Replication/Replication.md)).

## Declared fields (`plugin_field_value`)

A plugin declares fields in its `plugin.json` ([Plugins](../Plugins/Plugins.md),
`ByteShelfCommon.PluginDeclaration`): a property at the top of one of its two
documents, with a label, a kind (`text`, `number`, `date`, an instant written as ISO 8601 with an offset, or
`flag`), whether a search's free text matches it (text only) and whether listings carry it. A value of
another JSON type is ignored. `PluginFieldValue.Of` in Common reads one, for the server and the in-memory
client alike.

The server keeps each declared value in `plugin_field_value`, taken in the transaction that writes or applies
a document (`PluginFieldValueRepository`, called by both documents' repositories) and gone with the document by
a foreign key, so it never outlives one erased. Text is folded as a title is, its first 1,000 code points. The
values are neither logged nor replicated: every server takes its own by the plugins it has.

`plugin_values_taken` says by which fields a plugin's values were taken, and by which of the server's own
rules (`PluginFieldIndex.Version`, raised with them). At startup, before the server takes requests,
`PluginFieldIndex` takes them again for a plugin whose fields or rules are others now and drops those of one
that is gone.

- **A file's metadata** (`GET files/metadata`) has those with a value, by plugin, and the web UI's Details
  dialog, `get_info` and `byteshelf stat` show them.
- **Listings** (`entries`, `tree`, `search` and a deletion's tree) carry the fields declared for them
  (`ShelfFile.PluginFields`, `ListedFiles`): a text of up to 256 bytes, a longer one left out.
- **Search** takes `FileCriteria.PluginFields`, at most 20 filters that a file must all meet, and its text
  matches the fields declared to be searched. A filter on a plugin or field that is not there, or of another
  kind than the field, is 400. An encrypted file has no field of its contents.

A filter names a plugin by its id and a field by its name (`PluginFieldFilter`), and is written the same in a
query string (`pluginFields=`, escaped, once a filter), the CLI and MCP:

| Filter | The field |
|---|---|
| `notes.due` | has a value |
| `!notes.due` | has none |
| `notes.caption~apple` | is a text that contains it, ignoring case, in its first 1,000 code points |
| `notes.rating=3..5`, `=3..`, `=..5`, `=4` | is a number in the range, both ends included, or that number |
| `notes.due=2026-01-01..2026-02-01`, `=2026-01-01..`, `=..2026-02-01` | is an instant from the first (included) to the second (excluded), each a UTC date or an ISO 8601 time with an offset |
| `notes.done=true`, `=false` | is a flag that is that |

## The job (`metadata.reading`)

`MetadataReadingWorker` runs `MetadataReading` ([Jobs](../Jobs/Jobs.md)) at startup, whenever an
upload finishes (`MetadataReadingSignal`, woken by `UploadService`), and hourly. A pass:

1. drops what was read from contents no plain file row, live or trashed, holds any more (a file in the
   trash keeps it for a restore), every plugin's, installed or not;
2. runs each reader in turn (`ContentReaders`), the server's own first, then each plugin's: it reads every
   object that a live, plain file of a type it takes holds and that it has not read, whenever it was
   stored, a batch at a time by SHA-256, each in its own transaction. Every reader has a version (the
   server's own is `MetadataReader.Version`), and what another version read it reads again.

A plugin's reader is made the first time it has something to read, and has 60 seconds for that; one that is
not made by then says nothing of any contents, which stay to be read.

A read that throws, answers no valid document, or takes over 60 seconds is recorded as having found nothing,
and so not tried again by that version: contents never change. A read over its time is cancelled by its token,
and by its stream, which refuses the next read (`CancellableReadStream`), so a reader that looks at no token
still stops. One that has not stopped 5 seconds later is left behind on the thread it holds. A reader left
behind, or not made, is not run again until the server restarts, and admins are told why (`ReaderProblems`,
[Plugins](../Plugins/Plugins.md)); the other readers go on (`ContentReadRunner`). Shutting down records
nothing.

Uploads never wait for it. Contents that vanish between being listed and read are passed over.

## Replicas

Rows are change-log entities, each named by an id made from what it is about (`ObjectMetadata.IdFor`, the
first 128 bits of the SHA-256; `PluginMetadataId` for plugins' documents), so no two rows on any server are
about the same, and reach replicas through the log and whole copies like any other
([Replication](../Replication/Replication.md)). A replica never reads: its
job does nothing while it follows a primary, and starts reading once it is promoted.

## The in-memory client

It reads no contents, so nothing is read from any file there. It is given the plugins it has with the fields
they declare, keeps their documents about files, and evaluates filters on them; it has none about contents. The
client contract tests for what is read run against the server only (`HttpClientContractTests`,
`PluginFieldSearchTests`); what both clients share is in `ClientContractTests.Metadata.cs` and
`ClientContractTests.Plugins.cs`.
