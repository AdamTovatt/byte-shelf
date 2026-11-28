using System.Text.Json.Nodes;

namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// Says what the player needs before it plays a video: <c>durationSeconds</c>, its <c>width</c> and <c>height</c> as
    /// shown, whether it <c>streamsWell</c> as it is (<see cref="StreamsWell"/>), so no <c>stream</c> is made of it, and the
    /// layout of its <c>storyboard</c> (<see cref="Storyboard"/>). Of contents that hold no video ffmpeg reads it says
    /// nothing.
    /// </summary>
    public sealed class VideoReader : IContentDocumentReader
    {
        private readonly Ffmpeg _ffmpeg = Ffmpeg.Locate();

        public async Task<JsonObject?> ReadAsync(Stream contents)
        {
            await using VideoSource? source = await VideoSource.OpenAsync(_ffmpeg, contents);
            if (source == null)
            {
                return null;
            }

            VideoProbe video = source.Probe;
            Storyboard storyboard = Storyboard.For(video.DurationSeconds, video.Width, video.Height);
            return new JsonObject
            {
                ["durationSeconds"] = video.DurationSeconds,
                ["width"] = video.Width,
                ["height"] = video.Height,
                ["streamsWell"] = StreamsWell.Of(video),
                ["storyboard"] = new JsonObject
                {
                    ["intervalSeconds"] = storyboard.IntervalSeconds,
                    ["columns"] = storyboard.Columns,
                    ["rows"] = storyboard.Rows,
                    ["frameWidth"] = storyboard.FrameWidth,
                    ["frameHeight"] = storyboard.FrameHeight,
                    ["count"] = storyboard.Count,
                },
            };
        }
    }
}
