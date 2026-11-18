# Metadata

What a file's contents say about it, read by the server so it can be found without anyone describing
it: when a photo or video was taken, where (GPS coordinates; place names would need a geocoding
service and are left out), with what camera, how long a video plays, and a PDF's title and pages.
`GET files/metadata` answers it as `ByteShelfCommon.FileMetadata`, and get_info shows it.

Files of the types in `ReadMediaTypes` are read: `image/*`, `video/*`, `audio/*` and
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
A file encrypted in place drops its plain contents' row in the swap's transaction, unless another
file still holds them, and the change log forgets the row's earlier states
([Change log](../ChangeLog/ChangeLog.md)), so nothing read from the plaintext outlives it.

## Search

`FileSearch` asks about metadata with `TakenFrom` and `TakenBefore` (a start included, an end
excluded), `HasGps`, `MinDurationSeconds` and `MaxDurationSeconds`, and `MinPageCount` and
`MaxPageCount` (inclusive), and its text matches the title and camera as it does a name. The search
query joins each file to its contents' row, none for an encrypted file
(`Metadata/Repositories/MetadataSearch`), so a file not read yet, or never to be read, fails every
metadata criterion.

## The job (`metadata.reading`)

`MetadataReadingWorker` runs `MetadataReading` ([Jobs](../Jobs/Jobs.md)) at startup, whenever an
upload finishes (`MetadataReadingSignal`, woken by `UploadService`), and hourly. A pass:

1. drops the rows of contents no file row, live or trashed, holds any more (a file in the trash keeps
   its row for a restore);
2. reads every object a live, plain file of a type in `ReadMediaTypes` holds and that has no row
   yet, whenever it was stored, a batch at a time by SHA-256, each in its own transaction.

Uploads never wait for it. Contents that vanish between being listed and read are passed over.

## Replicas

Rows are change-log entities (`object_metadata`, named by `ObjectMetadata.IdFor`: the first 128 bits of
the SHA-256, so one object never has two rows on any server) and reach replicas through the log and
whole copies like any other ([Replication](../Replication/Replication.md)). A replica never reads: its
job does nothing while it follows a primary, and starts reading once it is promoted.

## The in-memory client

It reads no contents, so every file's metadata there is `FileMetadata.None`. The client contract tests
for what is read run against the server only (`HttpClientContractTests`); what both clients share is in
`ClientContractTests.Metadata.cs`.
