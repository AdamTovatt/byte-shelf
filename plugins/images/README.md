# The images plugin

What it makes of an image (`ImageMaker`), upright and with nothing the image came with, such as where it was
taken, but its colours:

| Kind | What it is |
|---|---|
| `thumbnail`, `preview` | WebP, at most 320 and 2048 pixels on the long side, from one decode. |
| `full` | JPEG at full size, or 65500 pixels on the long side, the most a JPEG has. Only of files stored as a type browsers do not show, HEIC or TIFF, and of no camera's raw, whatever it is stored as. |
| `agent` | JPEG in sRGB, at most 1568 pixels and 1 MiB. |

What is transparent stays so in WebP and is white in JPEG. None is larger than the image.

Its document about the contents (`ImageReader`) is their upright `width` and `height` and a `placeholder`, a `data:`
URL of a copy of 16 pixels, to show blurred until there is more. Of a camera's raw, it is of what the kinds are made
from: of its JPEG, the JPEG's size, which is of another shape when the camera was set to crop, as to 16:9. It is a
picture of the contents, so it is gone from the database when they are encrypted, as any such document is
([Metadata](../../ByteShelf.Server/Metadata/Metadata.md)).

## What it reads

Files stored as `image/*`, and of those only contents whose own first bytes are a JPEG, PNG, GIF, WebP, AVIF, HEIC,
TIFF, BMP or ICO (`ImageFormat`), whatever of these they are stored as; of others, such as an SVG, it makes nothing.
A file stored without an image type gets nothing, even when its name says it is one.

- Of an animation or a document of pages, the first; of an icon, the largest. An animated WebP is decoded whole, so
  a long one may take too much memory.
- A camera's raw begins as a TIFF: an ARW, a CR2, a DNG or a NEF. A TIFF that says it may be one (`CameraRaw`), by a
  CR2's mark or in its first directory by being a smaller copy, by a DNG's version or by pixels of a sensor's, is asked
  of LibRaw first; others are not, as LibRaw takes some of them for raws, such as a scanner's of 16-bit grays that
  names its maker. Of one LibRaw reads, every kind is made from the JPEG the camera made of it when that has at least
  1568 pixels on its long side, as an agent's has; else it is decoded, in the camera's white balance as shot. A raw
  whose first bytes are another's, such as an ORF, an RW2, a RAF or a CR3, is none. A TIFF LibRaw does not read, whose
  first page is a smaller copy, is refused.
- Of contents cut short, only a JPEG is made into anything: what there is of it. Of a camera's raw, that is the JPEG
  its camera made, as much of it as LibRaw hands over: of a DNG's, what there is. A DNG without one fails, as LibRaw
  finds it short; of an ARW cut short before its JPEG is whole, LibRaw hands over none and decodes what there is of
  its pixels with no sign that the rest is gone, black where it is cut.
- At most 200 MiB, and 120 megapixels of what is decoded: of a camera's raw made from its JPEG, the JPEG's. 1 GiB of
  memory for pixels; what decodes an AVIF, a HEIC or a camera's raw takes memory of its own beside that, so the largest
  of those fail.

## In the preview

Its viewer (`src/SharpeningViewer.tsx`) takes the files stored as `image/*`. An image's box has its recorded size
from the start, with its placeholder blurred in it, and keeps it. The thumbnail, if the listing names one, and the
preview load together, and the sharper of those that are there shows, never one less sharp after it. The whole image
loads behind them, unseen, the original or, of a type the browser does not show, `full`, and nothing when there is no
`full`, as of a camera's raw: once it is decoded (`decode()`), it takes their place in the same box, under the same
zoom (`useZoom`). If it fails, or there is none, the preview stays.
Leaving the image ends what it was loading. Of the files beside it, the size and the preview are loaded ahead, never
the whole. `byteshelf.client/e2e/shelf.spec.ts` records every frame of the swap.

It leaves an image to the next viewer (`Fallback`: the app's own, unless another plugin's comes first), to show or to
say why it cannot, when it is an SVG or encrypted, when nothing was made of it, and when the whole does not load
while no preview is shown, a thumbnail alone being too small to keep.

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
