# Metadata

What a file's contents say about it, read by the server so it can be found without anyone describing
it: when a photo or video was taken, where (GPS coordinates; place names would need a geocoding
service and are left out), with what camera, how long a video plays, and a PDF's title and pages.
`GET files/metadata` answers it as `ByteShelfCommon.FileMetadata`, and get_info shows it.

Plugins' readers find more, and plugins keep documents of their own about files; both are below
(*Plugins' documents*), and neither is in that answer.

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
whatever number of files hold them. A row with nothing found still says the contents were read.
Contents a parser fails on are damaged; since objects never change, they are recorded as carrying
nothing rather than read again. The title and camera are kept folded beside the text (as a file's
name is), for search.

A file shows its contents' row unless it is **encrypted**: the server never sees an encrypted file's
plaintext, and metadata read from it would leak what encryption hides. Such files are never read, and
`GET files/metadata` answers `FileMetadata.None` for one even when a plain file holds the same bytes.

**What was read from plaintext never outlives it**, whichever reader read it:

- A file encrypted in place drops what was read from its plain contents in the swap's transaction
  (`MetadataUpkeep`, which knows every table of metadata), unless another plain file, live or in the
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
| Written by | the plugin's reader, in the background, with its version | the plugin, over a version it names |
| Encrypted in place | erased, the log's earlier states too | kept, as a description is |
| Stored over | read again from the new contents | kept: the new file gets it, at the same version |

**A plugin must not put what it read from a file's plaintext into a document about the file**: that one is
kept when the file is encrypted, so it would outlive the plaintext. What is read from contents goes into the
document about them, through the plugin's reader.

A write of a document about a file names the version it replaces, and a removal is a write too: the row stays
with a null document, so no version of it is given twice and a write made against one that has since been
removed is refused.

Documents do not count toward a shelf's quota, as descriptions and what the server itself read do not: storing
a file over another copies up to 64 KiB per plugin whatever the new file's size.

Everything metadata knows of plugins comes from `IMetadataPlugins`. A plugin that is no longer installed
keeps its documents: those about a file go with it, those about contents are dropped by the job like any.

**A document about a file goes with the file's row by a foreign key, and nothing logs that**, so a replica
that keeps a purged file in its own trash keeps the document too. Its newest log entry would then stay for
good and come with every whole copy, so compaction drops it once its row is gone, a replica passes over a
document whose file it does not have, and a copy does not remove the document of a file it does not hold
([Change log](../ChangeLog/ChangeLog.md), [Replication](../Replication/Replication.md)).

## The job (`metadata.reading`)

`MetadataReadingWorker` runs `MetadataReading` ([Jobs](../Jobs/Jobs.md)) at startup, whenever an
upload finishes (`MetadataReadingSignal`, woken by `UploadService`), and hourly. A pass:

1. drops what was read from contents no plain file row, live or trashed, holds any more (a file in the
   trash keeps it for a restore), every plugin's, installed or not;
2. runs each reader in turn (`ContentReaders`), the server's own first, then each plugin's: it reads every
   object that a live, plain file of a type it takes holds and that it has not read, whenever it was
   stored, a batch at a time by SHA-256, each in its own transaction. A plugin's reader has a version, and
   what another version read it reads again; the server's own has none, as its table has none.

A read that throws, answers no valid document, or takes over 60 seconds is recorded as having found nothing,
and so not tried again by that version: contents never change. The server's own reader has no version, so
what it failed on it never tries again. A read over its time is cancelled by its token, and by its stream,
which refuses the next read (`CancellableReadStream`), so a reader that looks at no token still stops. One
that has not stopped 5 seconds later is left behind on the thread it holds, and its reader is not run again
until the server restarts; the other readers go on (`ContentReadRunner`). Shutting down records nothing.

Uploads never wait for it. Contents that vanish between being listed and read are passed over.

## Replicas

Rows are change-log entities, each named by an id made from what it is about (`ObjectMetadata.IdFor`, the
first 128 bits of the SHA-256; `PluginMetadataId` for plugins' documents), so no two rows on any server are
about the same, and reach replicas through the log and whole copies like any other
([Replication](../Replication/Replication.md)). A replica never reads: its
job does nothing while it follows a primary, and starts reading once it is promoted.

## The in-memory client

It reads no contents, so every file's metadata there is `FileMetadata.None`. The client contract tests
for what is read run against the server only (`HttpClientContractTests`); what both clients share is in
`ClientContractTests.Metadata.cs`.
