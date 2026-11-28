using System.Text.Json;
using System.Text.Json.Nodes;

namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// Says of a video what the player needs before it plays it, whether it <c>streamsWell</c> as it is
    /// (<see cref="StreamsWell"/>), so no <c>stream</c> is made of it, and the layout of its <c>storyboard</c>
    /// (<see cref="Storyboard"/>), and for whatever else reads the file's record through the API, its
    /// <c>durationSeconds</c> and its <c>width</c> and <c>height</c> as shown. Of a video that does not say how long it
    /// plays it says no <c>durationSeconds</c> nor <c>storyboard</c>; of contents that hold no video ffmpeg reads it says
    /// nothing.
    /// </summary>
    public sealed class VideoReader : IContentDocumentReader
    {
        private static readonly JsonSerializerOptions CamelCase = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly Ffmpeg _ffmpeg = Ffmpeg.Locate();

        public async Task<JsonObject?> ReadAsync(Stream contents)
        {
            await using VideoSource? source = await VideoSource.OpenAsync(_ffmpeg, contents);
            if (source == null)
            {
                return null;
            }

            VideoProbe video = source.Probe;
            JsonObject document = new JsonObject
            {
                ["width"] = video.Width,
                ["height"] = video.Height,
                ["streamsWell"] = StreamsWell.Of(video),
            };

            // Of a video that does not say how long it plays, as none is made of it, nor its storyboard.
            if (video.DurationSeconds is { } duration)
            {
                document["durationSeconds"] = duration;
                document["storyboard"] = JsonSerializer.SerializeToNode(Storyboard.For(duration, video.Width, video.Height), CamelCase);
            }

            return document;
        }
    }
}
