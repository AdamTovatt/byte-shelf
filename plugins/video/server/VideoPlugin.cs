using ByteShelfCommon;

namespace ByteShelf.Plugins.Video
{
    /// <summary>Reads how long videos play and how large they are, and makes posters, storyboards and copies that stream.</summary>
    public sealed class VideoPlugin : IByteShelfPlugin
    {
        /// <summary>What the contents are decides (<see cref="VideoProbe"/>), so one that holds no video is made into nothing.</summary>
        private static readonly string[] Videos = ["video/*"];

        /// <summary>
        /// The reader's version and the storyboard's, its <c>preview</c>: the reader says the layout the storyboard is made
        /// by (<see cref="Storyboard.For"/>), so a document is never of another layout than the storyboard beside it. A
        /// change to either reads every video and makes every storyboard again, which each take a quick run.
        /// </summary>
        internal const int ReaderAndStoryboardVersion = 1;

        public void Configure(IPluginBuilder builder)
        {
            builder.AddReader<VideoReader>(ReaderAndStoryboardVersion, Videos);
            builder.AddMaker<VideoMaker>(
                new DerivedFileDeclaration(DerivedKind.Thumbnail, 1, Videos),
                new DerivedFileDeclaration(DerivedKind.Preview, ReaderAndStoryboardVersion, Videos),
                new DerivedFileDeclaration(DerivedKind.Stream, 1, Videos));
        }
    }
}
