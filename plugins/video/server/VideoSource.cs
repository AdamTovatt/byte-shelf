namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// Contents being read as a video: served to ffmpeg by ranges (<see cref="RangeServer"/>), what they are, and how
    /// ffmpeg takes them as its input.
    /// </summary>
    internal sealed class VideoSource : IAsyncDisposable
    {
        /// <summary>What ffmpeg asks for at once where it seeks: a block (<see cref="BlockCache"/>).</summary>
        internal const int SeekingRequestBytes = BlockCache.BlockBytes;

        /// <summary>What ffmpeg asks for at once where it reads all of the contents: fewer requests, as many blocks.</summary>
        internal const int ReadingRequestBytes = 16 * BlockCache.BlockBytes;

        private readonly RangeServer _server;

        private VideoSource(RangeServer server, VideoProbe probe)
        {
            _server = server;
            Probe = probe;
        }

        public VideoProbe Probe { get; }

        /// <summary>The arguments of ffmpeg's or ffprobe's input that seek in the contents.</summary>
        public IReadOnlyList<string> SeekingInput => SeekingInputOf(_server);

        /// <summary>The arguments of ffmpeg's input that read all of the contents.</summary>
        public IReadOnlyList<string> ReadingInput => Ffmpeg.Input(_server.Url, ReadingRequestBytes);

        /// <summary><paramref name="contents"/>, a seekable stream, as a video; null when they hold none ffmpeg reads.</summary>
        public static async Task<VideoSource?> OpenAsync(Ffmpeg ffmpeg, Stream contents)
        {
            RangeServer server = RangeServer.Start(contents);
            try
            {
                Mp4Index? index = await Mp4Index.ReadAsync(server.Contents);
                if (await VideoProbe.ReadAsync(ffmpeg, SeekingInputOf(server), server.Contents.Length, index) is { } probe)
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
            return _server.DisposeAsync();
        }

        private static IReadOnlyList<string> SeekingInputOf(RangeServer server)
        {
            return Ffmpeg.Input(server.Url, SeekingRequestBytes);
        }
    }
}
