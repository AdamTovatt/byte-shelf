using System.Globalization;
using System.Text.Json;

namespace ByteShelf.Plugins.Video
{
    /// <summary>What ffprobe says of a video, with what its MP4 index says of seeking in it (<see cref="Mp4Index"/>).</summary>
    /// <param name="DurationSeconds">How long it plays, more than none; null when it does not say, as a recording a browser made often does not.</param>
    /// <param name="VideoStream">ffmpeg's number of its video's stream among all of the contents', which a cover's picture is not.</param>
    /// <param name="Width">How wide it is shown, upright and in square pixels.</param>
    /// <param name="Height">How high it is shown, so.</param>
    /// <param name="VideoCodec">ffmpeg's name of its video's codec, as <c>h264</c>.</param>
    /// <param name="VideoProfile">ffmpeg's name of the codec's profile, as <c>High</c>.</param>
    /// <param name="PixelFormat">ffmpeg's name of its pixels' format, as <c>yuv420p</c>.</param>
    /// <param name="AudioCodecs">Each of its audio's codecs, in order.</param>
    /// <param name="MajorBrand">An MP4's or QuickTime's brand, as <c>isom</c> or <c>qt  </c>.</param>
    /// <param name="Index">What its MP4 index says; null for any other container.</param>
    /// <param name="Bytes">How large the contents are.</param>
    internal sealed record VideoProbe(
        double? DurationSeconds,
        int VideoStream,
        int Width,
        int Height,
        string VideoCodec,
        string? VideoProfile,
        string? PixelFormat,
        IReadOnlyList<string> AudioCodecs,
        string? MajorBrand,
        Mp4Index? Index,
        long Bytes)
    {
        /// <summary>Bits a second, over the whole of it; null when how long it plays is not known.</summary>
        public double? BitsPerSecond => Bytes * 8 / DurationSeconds;

        /// <summary>
        /// What ffprobe says of the contents of <paramref name="bytes"/> it takes as <paramref name="input"/>, with what
        /// their <paramref name="index"/> says; null when they hold no video it reads.
        /// </summary>
        public static async Task<VideoProbe?> ReadAsync(Ffmpeg ffmpeg, IReadOnlyList<string> input, long bytes, Mp4Index? index)
        {
            string answer;
            try
            {
                answer = await ffmpeg.RunFfprobeAsync(["-print_format", "json", "-show_format", "-show_streams", .. input]);
            }
            catch (FfmpegFailedException)
            {
                return null;
            }

            return Parse(answer, bytes, index);
        }

        /// <summary>What ffprobe's JSON says, with <paramref name="index"/>; null when it says of no video.</summary>
        internal static VideoProbe? Parse(string json, long bytes, Mp4Index? index = null)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement[] streams = root.TryGetProperty("streams", out JsonElement all) && all.ValueKind == JsonValueKind.Array ? [.. all.EnumerateArray()] : [];
            // ffprobe lists every stream, in the order of their numbers.
            int stream = Array.FindIndex(streams, stream => Text(stream, "codec_type") == "video" && !IsPicture(stream));
            JsonElement shown = stream >= 0 ? streams[stream] : default;
            JsonElement format = root.TryGetProperty("format", out JsonElement found) ? found : default;
            if (stream < 0
                || Text(shown, "codec_name") is not { } codec
                || Number(shown, "width") is not { } width
                || Number(shown, "height") is not { } height
                || width < 1 || height < 1)
            {
                return null;
            }

            double? duration = Seconds(format, "duration") ?? Seconds(shown, "duration");
            (int shownWidth, int shownHeight) = ShownSize(shown, width, height);
            JsonElement tags = format.ValueKind == JsonValueKind.Object && format.TryGetProperty("tags", out JsonElement formatTags) ? formatTags : default;
            return new VideoProbe(
                duration > 0 ? duration : null,
                stream,
                shownWidth,
                shownHeight,
                codec,
                Text(shown, "profile"),
                Text(shown, "pix_fmt"),
                [.. streams.Where(stream => Text(stream, "codec_type") == "audio").Select(stream => Text(stream, "codec_name") ?? "")],
                Text(tags, "major_brand"),
                index,
                bytes);
        }

        /// <summary>Its size as shown: in square pixels by its pixels' shape, and turned as its display matrix says.</summary>
        private static (int Width, int Height) ShownSize(JsonElement video, int width, int height)
        {
            if (Text(video, "sample_aspect_ratio") is { } ratio
                && ratio.Split(':') is [{ } across, { } down]
                && int.TryParse(across, NumberStyles.None, CultureInfo.InvariantCulture, out int wide)
                && int.TryParse(down, NumberStyles.None, CultureInfo.InvariantCulture, out int high)
                && wide > 0 && high > 0)
            {
                width = Math.Max(1, (int)Math.Round((double)width * wide / high));
            }

            double rotation = video.TryGetProperty("side_data_list", out JsonElement sideData) && sideData.ValueKind == JsonValueKind.Array
                ? sideData.EnumerateArray().Select(side => side.TryGetProperty("rotation", out JsonElement turn) && turn.ValueKind == JsonValueKind.Number ? turn.GetDouble() : 0).FirstOrDefault(turn => turn != 0)
                : 0;
            return Math.Abs(Math.Round(rotation / 90)) % 2 == 1 ? (height, width) : (width, height);
        }

        /// <summary>A cover's picture, which ffmpeg names a video stream.</summary>
        private static bool IsPicture(JsonElement stream)
        {
            return stream.TryGetProperty("disposition", out JsonElement disposition)
                && disposition.ValueKind == JsonValueKind.Object
                && disposition.TryGetProperty("attached_pic", out JsonElement picture)
                && picture.ValueKind == JsonValueKind.Number
                && picture.GetInt32() == 1;
        }

        private static string? Text(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static int? Number(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
                ? number
                : null;
        }

        /// <summary>ffprobe says times as text.</summary>
        private static double? Seconds(JsonElement element, string name)
        {
            return Text(element, name) is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds)
                ? seconds
                : null;
        }
    }
}
