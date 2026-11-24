# The images plugin

What it makes of an image (`ImageMaker`), upright and with nothing the image came with, such as where it was
taken, but its colours:

| Kind | What it is |
|---|---|
| `thumbnail`, `preview` | WebP, at most 320 and 2048 pixels on the long side, from one decode. |
| `full` | JPEG at full size, or 65500 pixels on the long side, the most a JPEG has. Only of files stored as a type browsers do not show, HEIC or TIFF. |
| `agent` | JPEG in sRGB, at most 1568 pixels and 1 MiB. |

What is transparent stays so in WebP and is white in JPEG. None is larger than the image.

Its document about the contents (`ImageReader`) is their upright `width` and `height` and a `placeholder`, a `data:`
URL of a copy of 16 pixels, to show blurred until there is more. It is a picture of the contents, so it is gone
from the database when they are encrypted, as any such document is ([Metadata](../../ByteShelf.Server/Metadata/Metadata.md)).

## What it reads

Files stored as `image/*`, and of those only contents whose own first bytes are a JPEG, PNG, GIF, WebP, AVIF, HEIC,
TIFF, BMP or ICO (`ImageFormat`), whatever of these they are stored as; of others, such as an SVG, it makes nothing.
A file stored without an image type gets nothing, even when its name says it is one.

- Of an animation or a document of pages, the first; of an icon, the largest. An animated WebP is decoded whole, so
  a long one may take too much memory.
- A camera's raw files are none: a TIFF whose first page is a smaller copy is refused, and a CR2.
- Of contents cut short, only a JPEG is made into anything: what there is of it.
- At most 200 MiB and 120 megapixels, with 1 GiB of memory for pixels. What decodes an AVIF or a HEIC takes memory of
  its own beside that, so the largest of those fail.

## In the preview

Its viewer (`src/SharpeningViewer.tsx`) takes the files stored as `image/*`; an SVG, an encrypted image and one of
which nothing was made are the app's own viewer's to show, or to say why it cannot. An image's box has its recorded
size from the start, with its placeholder blurred in it, and keeps it. The thumbnail, if the listing names one, and
the preview load together, and the sharper of those that are there shows, never one less sharp after it. The whole
image loads behind them, unseen, the original or, of a type the browser does not show, `full`: once it is decoded
(`decode()`), it takes their place in the same box, under the same zoom (`useZoom`). If it fails, the preview
stays. Leaving the image ends what it was loading. Of the files beside it, the size and the preview are loaded ahead,
never the whole. `byteshelf.client/e2e/shelf.spec.ts` records every frame of the swap.

## Colours

An RGB profile of at most 64 KiB is kept, and an AVIF or a HEIC marked as Display P3 without one is given it. Anything
else becomes sRGB (CMYK, gray with a profile of grays, a larger profile) or is taken for it: no profile, a damaged
one, a PNG marked by `cICP`, or an AVIF or HEIC marked as anything else, HDR included (`Colors`).

## ImageMagick

Magick.NET decodes and writes, kept to those formats by `server/policy.xml` and to its limits by `Codecs`, through
which every read goes. ImageMagick adds to that policy whatever other `policy.xml` it finds, on Linux in
`/tmp/ImageMagick` too, where anyone on the machine may put one: the plugin does not run when it does not go by its
own limits, which admins see as a problem of its reader.

`Magick.NET.Notice.txt`, with the licences of the libraries in its native ones, must come with them. The plugin has
those for Linux x64 and arm64, macOS arm64 and Windows x64, all in its one zip; on another platform neither it nor its
tests run.
