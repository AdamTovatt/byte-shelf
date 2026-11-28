using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace ByteShelf.Plugins.Video.Tests.TestUtilities
{
    /// <summary>Videos made for the tests by the plugin's own ffmpeg, each once a run, and what ffmpeg says of what was made of them.</summary>
    internal static class TestVideos
    {
        /// <summary>6 seconds at 30 frames a second of 320 by 240, with sound.</summary>
        private const string Picture = "testsrc2=size=320x240:rate=30:duration=6";

        private const string Sound = "sine=frequency=440:duration=6";

        private static readonly string[] PictureAndSound = ["-f", "lavfi", "-i", Picture, "-f", "lavfi", "-i", Sound];

        private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> Made = new ConcurrentDictionary<string, Lazy<Task<byte[]>>>();

        public static Ffmpeg Ffmpeg { get; } = Ffmpeg.Locate();

        /// <summary>An MP4 that streams well: H.264 High, AAC, a keyframe every second, its index at the front, some 200 kbps.</summary>
        public static Task<byte[]> StreamingWellAsync()
        {
            return MakeAsync(".mp4", [.. PictureAndSound, "-c:v", "libx264", "-profile:v", "high", "-pix_fmt", "yuv420p", "-g", "30", "-c:a", "aac", "-movflags", "+faststart"]);
        }

        /// <summary>As <see cref="StreamingWellAsync"/>, with a keyframe every 5 seconds, and of so many bits a second that a stream of it is smaller.</summary>
        public static Task<byte[]> SparseKeyframesAsync()
        {
            return MakeAsync(".mp4", [.. PictureAndSound, "-c:v", "libx264", "-crf", "10", "-pix_fmt", "yuv420p", "-g", "150", "-sc_threshold", "0", "-c:a", "aac", "-movflags", "+faststart"]);
        }

        /// <summary>As <see cref="StreamingWellAsync"/>, with its index at the end.</summary>
        public static Task<byte[]> IndexAtEndAsync()
        {
            return MakeAsync(".mp4", [.. PictureAndSound, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "30", "-c:a", "aac"]);
        }

        /// <summary>Its own pixels as <see cref="StreamingWellAsync"/>'s, in Matroska.</summary>
        public static Task<byte[]> MatroskaAsync()
        {
            return MakeAsync(".mkv", "-f", "lavfi", "-i", Picture, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "30");
        }

        /// <summary>A WebM that does not say how long it plays, as one a browser records: written as it is made, as a live stream.</summary>
        public static Task<byte[]> NoKnownLengthAsync()
        {
            return MakeAsync(".webm", "-f", "lavfi", "-i", Picture, "-c:v", "libvpx", "-b:v", "200k", "-live", "1");
        }

        /// <summary>A video of <paramref name="picture"/>, a lavfi source, with whatever <paramref name="arguments"/> add, in an MP4.</summary>
        public static Task<byte[]> Mp4Async(string picture, params string[] arguments)
        {
            return MakeAsync(".mp4", ["-f", "lavfi", "-i", picture, .. arguments]);
        }

        /// <summary>
        /// What <paramref name="arguments"/>, ffmpeg's but the output, make of nothing, as a file ending in
        /// <paramref name="extension"/>: once a run, unless ffmpeg failed, which the next test that asks tries again.
        /// </summary>
        public static async Task<byte[]> MakeAsync(string extension, params string[] arguments)
        {
            string key = extension + " " + string.Join(' ', arguments);
            Lazy<Task<byte[]>> making = Made.GetOrAdd(key, _ => new Lazy<Task<byte[]>>(() => RunAsync(extension, arguments)));
            try
            {
                return await making.Value;
            }
            catch
            {
                Made.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(key, making));
                throw;
            }
        }

        /// <summary><paramref name="video"/> with its display matrix turned by <paramref name="degrees"/>, as a phone held upright records.</summary>
        public static async Task<byte[]> TurnedAsync(byte[] video, int degrees)
        {
            return await WithFileAsync(video, ".mp4", input => RunAsync(".mp4", "-display_rotation:v:0", degrees.ToString(CultureInfo.InvariantCulture), "-i", input, "-c", "copy"));
        }

        /// <summary>What ffprobe says of <paramref name="file"/>'s streams and format.</summary>
        public static async Task<JsonDocument> ProbeAsync(byte[] file, string extension)
        {
            string json = await WithFileAsync(file, extension, path => Ffmpeg.RunFfprobeAsync(["-print_format", "json", "-show_format", "-show_streams", path]));
            return JsonDocument.Parse(json);
        }

        /// <summary>
        /// <paramref name="video"/>, an MP4 with its index at the front, cut short before its first frame at or after
        /// <paramref name="seconds"/>: its index still says it plays for all of its time, as of a file whose upload ended early.
        /// </summary>
        public static async Task<byte[]> CutAsync(byte[] video, double seconds)
        {
            string json = await WithFileAsync(video, ".mp4", path => Ffmpeg.RunFfprobeAsync(["-print_format", "json", "-select_streams", "v:0", "-show_packets", path]));
            using JsonDocument packets = JsonDocument.Parse(json);
            long cut = packets.RootElement.GetProperty("packets").EnumerateArray()
                .Where(packet => double.Parse(packet.GetProperty("pts_time").GetString()!, CultureInfo.InvariantCulture) >= seconds)
                .Min(packet => long.Parse(packet.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture));
            return video[..(int)cut];
        }

        /// <summary>How long ffprobe says <paramref name="file"/> plays.</summary>
        public static async Task<double> DurationAsync(byte[] file, string extension)
        {
            using JsonDocument probe = await ProbeAsync(file, extension);
            return double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        }

        /// <summary>The first video stream ffprobe finds in <paramref name="file"/>.</summary>
        public static async Task<JsonElement> VideoStreamAsync(byte[] file, string extension)
        {
            using JsonDocument probe = await ProbeAsync(file, extension);
            return probe.RootElement.GetProperty("streams").EnumerateArray().First(stream => stream.GetProperty("codec_type").GetString() == "video").Clone();
        }

        /// <summary>How bright <paramref name="image"/> is on average, from 0 to 255, or the part of it ffmpeg's <paramref name="crop"/> takes, as <c>w:h:x:y</c>.</summary>
        public static async Task<double> MeanGrayAsync(byte[] image, string? crop = null)
        {
            return await WithFileAsync(image, ".webp", async path =>
            {
                string gray = Path.ChangeExtension(path, ".gray");
                string[] cropped = crop == null ? [] : ["-vf", "crop=" + crop];
                await Ffmpeg.RunFfmpegAsync(["-i", path, .. cropped, "-f", "rawvideo", "-pix_fmt", "gray", gray]);
                byte[] pixels = await File.ReadAllBytesAsync(gray);
                return pixels.Average(pixel => (double)pixel);
            });
        }

        private static async Task<byte[]> RunAsync(string extension, params string[] arguments)
        {
            DirectoryInfo folder = Directory.CreateTempSubdirectory("video-tests-");
            try
            {
                string output = Path.Combine(folder.FullName, "made" + extension);
                await Ffmpeg.RunFfmpegAsync([.. arguments, output]);
                return await File.ReadAllBytesAsync(output);
            }
            finally
            {
                folder.Delete(recursive: true);
            }
        }

        private static async Task<T> WithFileAsync<T>(byte[] file, string extension, Func<string, Task<T>> use)
        {
            DirectoryInfo folder = Directory.CreateTempSubdirectory("video-tests-");
            try
            {
                string path = Path.Combine(folder.FullName, "file" + extension);
                await File.WriteAllBytesAsync(path, file);
                return await use(path);
            }
            finally
            {
                folder.Delete(recursive: true);
            }
        }
    }
}
