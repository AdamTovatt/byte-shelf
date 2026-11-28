using System.Globalization;
using ByteShelfCommon;

namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// Makes a video's poster (<c>thumbnail</c>), its storyboard (<c>preview</c>, <see cref="Storyboard"/>) and a copy of it
    /// that streams (<c>stream</c>), each with ffmpeg in the working folder. Poster and storyboard seek to each frame they
    /// take and read little else of the contents, so a video of gigabytes is done in a quick run. Of contents that hold no
    /// video ffmpeg reads, it makes nothing.
    /// </summary>
    public sealed class VideoMaker : IDerivedFileMaker
    {
        private const string WebP = "image/webp";
        private const string Mp4 = "video/mp4";

        internal const int PosterLongSide = 320;

        /// <summary>How far into a video its poster is taken.</summary>
        internal const double PosterAt = 0.1;

        /// <summary>A stream is at most 1080p: 1920 by 1080 pixels, or 1080 by 1920 when it is upright.</summary>
        internal const int StreamLongSide = 1920;

        internal const int StreamShortSide = 1080;

        /// <summary>A stream's keyframes, one a second: within <see cref="StreamsWell.MaxKeyframeGapSeconds"/>, so a seek lands within a second of where it was asked.</summary>
        internal const int StreamKeyframeSeconds = 1;

        /// <summary>A stream's most bits a second of video, which with its audio's stays within <see cref="StreamsWell.MaxBitsPerSecond"/>.</summary>
        internal const int StreamMaxVideoBitsPerSecond = 6_000_000;

        internal const int StreamAudioBitsPerSecond = 160_000;

        /// <summary>
        /// Threads of the decoder and of the encoder of a stream, each. ffmpeg has the process's memory limit of 1.5 GiB
        /// (Processes.md), which counts what each thread reserves: by as many threads as a host of 16 cores has, it fails
        /// to encode a 4K video, which 4 and 4 do in some 400 MiB.
        /// </summary>
        private const string Threads = "4";

        private readonly Ffmpeg _ffmpeg = Ffmpeg.Locate();

        public async Task MakeAsync(Stream contents, IReadOnlyList<DerivedKind> kinds, IMadeFiles made)
        {
            await using VideoSource? source = await VideoSource.OpenAsync(_ffmpeg, contents);
            if (source == null)
            {
                return;
            }

            DirectoryInfo work = Directory.CreateTempSubdirectory("video-");
            try
            {
                foreach ((DerivedKind kind, Func<VideoSource, string, IMadeFiles, Task> add) in Adders)
                {
                    if (!kinds.Contains(kind))
                    {
                        continue;
                    }

                    try
                    {
                        await add(source, work.FullName, made);
                    }
                    catch (FfmpegFailedException)
                    {
                        // Left out alone, as ffmpeg could not make it of these contents, such as a poster of a video cut
                        // short before a tenth of it: the others are made all the same.
                    }
                }
            }
            finally
            {
                work.Delete(recursive: true);
            }
        }

        /// <summary>How each kind is made, each in files of its own in the working folder.</summary>
        private IEnumerable<(DerivedKind Kind, Func<VideoSource, string, IMadeFiles, Task> Add)> Adders =>
        [
            (DerivedKind.Thumbnail, AddPosterAsync),
            (DerivedKind.Preview, AddStoryboardAsync),
            (DerivedKind.Stream, AddStreamAsync),
        ];

        private async Task AddPosterAsync(VideoSource source, string work, IMadeFiles made)
        {
            VideoProbe video = source.Probe;
            (int width, int height) = Sizes.Fit(video.Width, video.Height, PosterLongSide, PosterLongSide, even: false);
            string poster = Path.Combine(work, "poster.webp");

            // At the start of a video that does not say how long it plays.
            await TakeFrameAsync(source, (video.DurationSeconds ?? 0) * PosterAt, width, height, poster, "libwebp");
            await made.AddAsync(DerivedKind.Thumbnail, WebP, file => CopyAsync(poster, file));
        }

        /// <summary>Each frame by a seek of its own, a few at once, then all of them in their grid; none of a video that does not say how long it plays.</summary>
        private async Task AddStoryboardAsync(VideoSource source, string work, IMadeFiles made)
        {
            VideoProbe video = source.Probe;
            if (video.DurationSeconds is not { } duration)
            {
                return;
            }

            Storyboard storyboard = Storyboard.For(duration, video.Width, video.Height);
            string FrameOf(int frame) => Path.Combine(work, string.Create(CultureInfo.InvariantCulture, $"frame-{frame:D3}.png"));

            await Parallel.ForEachAsync(
                Enumerable.Range(0, storyboard.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4) },
                async (frame, _) =>
                {
                    try
                    {
                        await TakeFrameAsync(source, storyboard.TimeOf(frame), storyboard.FrameWidth, storyboard.FrameHeight, FrameOf(frame), "png");
                    }
                    catch (FfmpegFailedException)
                    {
                        // Its place takes the nearest frame there is, below.
                    }
                });

            int[] taken = [.. Enumerable.Range(0, storyboard.Count).Where(frame => File.Exists(FrameOf(frame)))];
            if (taken.Length == 0)
            {
                throw new FfmpegFailedException("ffmpeg", "took no frame of the video");
            }

            foreach (int missing in Enumerable.Range(0, storyboard.Count).Except(taken))
            {
                File.Copy(FrameOf(taken.MinBy(frame => Math.Abs(frame - missing))), FrameOf(missing));
            }

            string grid = Path.Combine(work, "storyboard.webp");
            await _ffmpeg.RunFfmpegAsync(
            [
                "-framerate", "1", "-start_number", "0", "-i", Path.Combine(work, "frame-%03d.png"),
                "-vf", string.Create(CultureInfo.InvariantCulture, $"tile={storyboard.Columns}x{storyboard.Rows}"),
                "-frames:v", "1", "-c:v", "libwebp", "-quality", "70", "-f", "image2", grid,
            ]);
            await made.AddAsync(DerivedKind.Preview, WebP, file => CopyAsync(grid, file));
        }

        /// <summary>
        /// The keyframe at or before <paramref name="seconds"/>, upright and <paramref name="width"/> by
        /// <paramref name="height"/>, to <paramref name="path"/> in <paramref name="codec"/>. It is copied out of the
        /// contents as it is, beside <paramref name="path"/>, and decoded from there: ffmpeg decoding at a seek would read
        /// and decode all that follows the keyframe up to the time, which is seconds of a video's bytes where keyframes are
        /// far apart.
        /// </summary>
        private async Task TakeFrameAsync(VideoSource source, double seconds, int width, int height, string path, string codec)
        {
            string keyframe = Path.ChangeExtension(path, ".mkv");
            await _ffmpeg.RunFfmpegAsync(
            [
                "-noaccurate_seek", "-ss", Seconds(seconds), .. source.SeekingInput,
                "-map", VideoOf(source.Probe), "-c", "copy", "-frames:v", "1", "-f", "matroska", keyframe,
            ]);
            await _ffmpeg.RunFfmpegAsync(
            [
                "-threads", "1", "-i", keyframe, "-frames:v", "1",
                "-vf", Scaled(width, height),
                "-f", "image2", "-c:v", codec, path,
            ]);
            File.Delete(keyframe);
            if (!File.Exists(path))
            {
                throw new FfmpegFailedException("ffmpeg", "took no frame");
            }
        }

        /// <summary>
        /// A copy that streams: H.264 High in 8-bit 4:2:0 and AAC in stereo, which every browser plays; at CRF 23 and at
        /// most <see cref="StreamMaxVideoBitsPerSecond"/>, at most 1080p, so it plays over a home connection; a keyframe
        /// every <see cref="StreamKeyframeSeconds"/> of a closed GOP; in an MP4 with its index at the front, so it plays at
        /// once: so it streams well by <see cref="StreamsWell"/>'s rule. None is made of a video that streams well as it
        /// is, nor kept when it comes out larger than a stream may be (<see cref="DerivedFileLimits.MaxBytesOf"/>), or than
        /// a video every browser plays as it is.
        /// </summary>
        private async Task AddStreamAsync(VideoSource source, string work, IMadeFiles made)
        {
            VideoProbe video = source.Probe;
            if (StreamsWell.Of(video))
            {
                return;
            }

            (int maxWidth, int maxHeight) = video.Width >= video.Height ? (StreamLongSide, StreamShortSide) : (StreamShortSide, StreamLongSide);
            (int width, int height) = Sizes.Fit(video.Width, video.Height, maxWidth, maxHeight, even: true);
            string stream = Path.Combine(work, "stream.mp4");
            await _ffmpeg.RunFfmpegAsync(
            [
                "-threads", Threads, .. source.ReadingInput,
                "-map", VideoOf(video), "-map", "0:a:0?", "-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1",
                "-vf", Scaled(width, height),
                "-c:v", "libx264", "-threads", Threads, "-preset", "veryfast", "-profile:v", "high", "-pix_fmt", "yuv420p",
                "-crf", "23", "-maxrate", Number(StreamMaxVideoBitsPerSecond), "-bufsize", Number(2 * StreamMaxVideoBitsPerSecond),
                "-force_key_frames", $"expr:gte(t,n_forced*{Number(StreamKeyframeSeconds)})", "-x264-params", "scenecut=0:open-gop=0",
                "-c:a", "aac", "-b:a", Number(StreamAudioBitsPerSecond), "-ac", "2",
                "-movflags", "+faststart", "-f", "mp4", stream,
            ]);
            long bytes = new FileInfo(stream).Length;
            if (bytes > DerivedFileLimits.MaxBytesOf(DerivedKind.Stream) || (bytes >= video.Bytes && StreamsWell.PlaysAsItIs(video)))
            {
                return;
            }

            await made.AddAsync(DerivedKind.Stream, Mp4, file => CopyAsync(stream, file));
        }

        private static string Seconds(double seconds)
        {
            return seconds.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string Number(int number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>ffmpeg's map of the video's own stream, which a cover's picture ahead of it is not.</summary>
        private static string VideoOf(VideoProbe video)
        {
            return string.Create(CultureInfo.InvariantCulture, $"0:{video.VideoStream}");
        }

        /// <summary>The filter that scales a frame to <paramref name="width"/> by <paramref name="height"/>, in square pixels.</summary>
        private static string Scaled(int width, int height)
        {
            return string.Create(CultureInfo.InvariantCulture, $"scale={width}:{height},setsar=1");
        }

        private static async Task CopyAsync(string path, Stream file)
        {
            await using FileStream made = File.OpenRead(path);
            await made.CopyToAsync(file);
        }
    }
}
