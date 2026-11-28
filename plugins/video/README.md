# The video plugin

What it makes of a video (`VideoMaker`), with its own ffmpeg, in its process's working folder:

| Kind | What it is |
|---|---|
| `thumbnail` | The poster: a WebP of at most 320 pixels on the long side, of the keyframe at or before a tenth of the way in. |
| `preview` | The storyboard: one WebP grid of frames 160 pixels wide across the whole video (`Storyboard`), whose layout the document says, which the player shows over its seek bar. |
| `stream` | A copy that streams: H.264 High in an MP4 with its index at the front, a keyframe every second of a closed GOP, at most 1080p (1080 wide when upright), CRF 23 at most about 6 Mbps, AAC at 160 kbps in stereo. Made in the background alone, in a process of its own at the lowest CPU priority, for up to 24 hours ([Processes](../../ByteShelf.Server/Plugins/Processes/Processes.md)). None is made of a video that streams well as it is, nor kept when it comes out larger than a stream may be (8 GiB, `DerivedFileLimits`), or than a video every browser plays as it is (an MP4 of H.264 and AAC, `StreamsWell.PlaysAsItIs`), so asking for it is a 404 and the video itself is what plays. A disk that fills as it is made says nothing of the video: the plugin cannot make streams until there is room. |

Poster and storyboard are each of keyframes, each copied out of the contents as it is and decoded alone, so a run
reads little more of the contents than those frames and their index: a video of gigabytes with keyframes far apart
is done in a quick run. A storyboard's frames are taken a few at once; one that cannot be taken is the nearest one
that could. A kind ffmpeg cannot make of the contents, such as the poster of a video cut short before a tenth of it,
is left out alone, and the others are made all the same. The stream is decoded and encoded with 4 threads each, so
it stays within ffmpeg's limit of memory, the 1.5 GiB its plugin's process has, which it inherits
([Processes](../../ByteShelf.Server/Plugins/Processes/Processes.md)), whatever the host has.

Its document about the contents (`VideoReader`):

```json
{
  "durationSeconds": 540.2,
  "width": 1920,
  "height": 1080,
  "streamsWell": false,
  "storyboard": { "intervalSeconds": 5.402, "columns": 10, "rows": 10, "frameWidth": 160, "frameHeight": 90, "count": 100 }
}
```

`width` and `height` are as it is shown: in square pixels, and upright as its display matrix says. `streamsWell`
is `StreamsWell`, the one rule of whether a video streams well as it is: an MP4 (not QuickTime's `qt  ` brand, which
some browsers do not play as `video/quicktime`) with its index at the front, of H.264 in 8-bit 4:2:0 (Baseline, Main
or High), with AAC or no audio, at most 2 seconds between keyframes over the whole video by its index (`Mp4Index`),
and at most 8 Mbps in all. The storyboard's layout is `Storyboard.For`, by which the maker makes it: the video in equal
stretches of at most 2 seconds, and at most 100 of them, so of longer past 200 seconds, 10 to a row, frame `i`
standing for the time from `i × intervalSeconds` to the next and of the keyframe at or before the middle of it. The reader and the storyboard
are of one version (`VideoPlugin.ReaderAndStoryboardVersion`), so the layout a document says is the storyboard's.

## The player

Its viewer of `video/*` (`VideoPlayer`), ahead of the app's own, plays the `stream` when the file's listing named
one as it opened, and else the video itself (`playingOf`): a stream made while the video plays is played the next
time it is opened. Of the two, one that cannot be played is left for the other, from where it was, and a video
neither of which plays is the next viewer's, as an encrypted one is. Of a video that streams well as it is, it
never asks for a stream. Of one that does not, whose stream is not listed, it asks what there is
(`DerivedReader.status`) and says only that a streaming copy is on its way or that there is no room on the shelf
for one. It shows the poster until it plays. Where the page reads files whole, as where it keeps no cookie, it
plays the video itself, which the page has read already. A video the network cut off is kept, and play loads it
again from where it was.

Its controls are in the preview's bottom bar (`PreviewControls`, `VideoControls`), fading with the bars: play and
pause, the time and the whole, the seek bar (`SeekBar`), which shows what is loaded and, where the pointer is, the
storyboard's frame of that time, then mute and the volume, the speed, `Original` while there is a stream, which
plays the video itself instead, from where it was, at its speed and volume, and full screen. The clock is in whole
seconds gone by, the whole too, so it reads the same at the end. Of a video whose length is not known, as one a
browser recorded, it says where it is alone, and the seek bar moves to nowhere. A click with the mouse on the video
plays or pauses it, a tap shows or hides the bars, and a double click goes full screen.

Space and K play and pause, once however long the key is held, and J and L jump 10 seconds back and forward, again
as the key repeats, of the keys the preview hands its viewer (`keyAction`, `usePreviewKeys`), pressed with nothing
held (`isPlainKey`): none typed into a field, though on the volume slider too, none while a dialog is over it, and
not Space on a button that has the focus, which presses the button. While the seek bar has the focus, ← and → move
5 seconds, which then step to no other file, and Home and End go to the start and the end. Full screen is of the
page while the preview is open, its bars, menus and dialogs and all, and on an iPhone of the video in the browser's
own player (`usePreviewFullScreen`).

Where a video was stopped is kept in the browser by its file's id, for the last 200, and it is picked up there when
that is more than 10 seconds from either end (`resumePoint`). It is the browser's, not the account's: it outlives
signing out, and whoever signs in on that browser picks a video up where the last one left it.
A video stored without a type (`application/octet-stream`) is not taken, whatever its name, as the server makes
nothing of it either.

## What it reads

Files stored as `video/*`, of which ffmpeg reads a video in an MP4 or QuickTime, Matroska or WebM, AVI, MPEG
transport or program stream, FLV, ASF, Ogg, MXF or DV container. Of contents in none of those, or with no video but
a cover's picture, it makes and says nothing. Of a video that does not say how long it plays, as one a browser
records often does not, it makes the poster, of its start, and a stream, but no storyboard, and its document says
no `durationSeconds` nor `storyboard`. A playlist, such as HLS or ffmpeg's concat, is no video: ffmpeg would open
what it names.

ffmpeg reads the contents from the plugin's own server on the loopback (`RangeServer`), at an address of 32 random
bytes, by ranges of 64 KiB where it seeks and 1 MiB where it reads them all, over HTTP alone: it opens no file and no
other address. The last 16 MiB read are kept in memory (`BlockCache`), so the index each ffmpeg reads is read of the
contents once.

## ffmpeg

Static ffmpeg and ffprobe of a dated build of [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) (GPL,
with libx264), for Linux x64 and arm64, each in `ffmpeg/<runtime>/` of the plugin's zip with ffmpeg's licence
(`LICENSE.txt`) and what build it is and where its source is (`BUILD.txt`), with each library it is built with
and where that one's source is, at the revision it was built of (`server/FfmpegLibraries.txt`). They are downloaded
as the plugin builds, by `server/Ffmpeg.targets`, and kept under `server/obj`: a build takes the one of the machine
it is on, a publish both. One whose SHA-256 is not the pinned one, or that is not there to download, fails the build and says why. On
any other platform the reader and maker cannot be made, so the plugin does not run, which admins see as its
problem; nothing is installed on the host.

To move to a newer build, or when the pinned one is gone from BtbN's releases:

1. Pick a month-end release of `BtbN/FFmpeg-Builds`, which are kept longest, as `autobuild-YYYY-MM-DD-HH-MM`,
   and of it the two `ffmpeg-n<version>-…-linux64-gpl-<branch>.tar.xz` and `…-linuxarm64-gpl-<branch>.tar.xz`
   assets.
2. Take each asset's SHA-256 from the release (`gh release view <tag> --repo BtbN/FFmpeg-Builds --json assets`
   lists their digests), or from a download of it.
3. Put the tag in `FfmpegRelease` and each asset's name, without `.tar.xz`, and SHA-256 in `server/Ffmpeg.targets`,
   and run the plugin's tests, which make their videos with it.
4. List again in `server/FfmpegLibraries.txt` what it is built with: of each script in `scripts.d` of
   `BtbN/FFmpeg-Builds` at the tag whose `ffbuild_enabled` holds for `linux64` and `linuxarm64` of the `gpl`
   variant at that ffmpeg's version, its `SCRIPT_REPO` and `SCRIPT_COMMIT` (or tag), as `name: repo at revision`,
   in the scripts' order, marked `(linux-x64 alone)` where it holds for that one alone. `ffmpeg -buildconf` of each
   says which libraries it was configured with.

## Building and testing

Its first build downloads ffmpeg, so it needs the network, some 580 MB under `server/obj`, and a `tar` that reads
xz (`tar -xJ`); later ones use what was kept. `npm test` in `plugins/video` runs the player's tests, and
`dotnet test plugins/video/server.tests` the maker's and the reader's, which make their videos with the plugin's
ffmpeg and are skipped on a platform it has none for.
