namespace ByteShelf.Plugins.Video
{
    /// <summary>Contents being read as a video: served to ffmpeg by ranges (<see cref="RangeServer"/>), and what they are.</summary>
    internal sealed class VideoSource : IAsyncDisposable
    {
        private VideoSource(RangeServer server, VideoProbe probe)
        {
            Server = server;
            Probe = probe;
        }

        public RangeServer Server { get; }

        public VideoProbe Probe { get; }

        /// <summary><paramref name="contents"/>, a seekable stream, as a video; null when they hold none ffmpeg reads.</summary>
        public static async Task<VideoSource?> OpenAsync(Ffmpeg ffmpeg, Stream contents)
        {
            RangeServer server = RangeServer.Start(contents);
            try
            {
                if (await VideoProbe.ReadAsync(ffmpeg, server) is { } probe)
                {
                    return new VideoSource(server, probe);
                }
            }
            catch
            {
                await server.DisposeAsync();
                throw;
            }

            await server.DisposeAsync();
            return null;
        }

        public ValueTask DisposeAsync()
        {
            return Server.DisposeAsync();
        }
    }
}
