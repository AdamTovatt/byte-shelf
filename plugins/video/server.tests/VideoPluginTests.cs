using ByteShelf.Plugins.Testing;
using ByteShelfCommon;

namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class VideoPluginTests
    {
        [Fact]
        public void Configure_AddsTheReaderAndTheMakerOfVideos()
        {
            RecordingPluginBuilder builder = new RecordingPluginBuilder();

            new VideoPlugin().Configure(builder);

            Assert.Equal(typeof(VideoReader), builder.Reader);
            Assert.Equal(["video/*"], builder.Read);
            Assert.Equal(typeof(VideoMaker), builder.Maker);
            Assert.Empty(builder.Services);
            Assert.Equal([DerivedKind.Thumbnail, DerivedKind.Preview, DerivedKind.Stream], builder.Makes.Select(makes => makes.Kind));
            Assert.All(builder.Makes, makes => Assert.Equal(["video/*"], makes.MediaTypes));
        }

        /// <summary>The reader's document says the storyboard's layout, so neither changes without the other.</summary>
        [Fact]
        public void Configure_TheReaderAndTheStoryboard_AreOfOneVersion()
        {
            RecordingPluginBuilder builder = new RecordingPluginBuilder();

            new VideoPlugin().Configure(builder);

            Assert.Equal(builder.ReaderVersion, builder.Makes.Single(makes => makes.Kind == DerivedKind.Preview).Version);
        }

    }
}
