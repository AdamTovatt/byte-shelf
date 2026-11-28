using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class VideoPluginTests
    {
        [Fact]
        public void Configure_AddsTheReaderAndTheMakerOfVideos()
        {
            Builder builder = new Builder();

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
            Builder builder = new Builder();

            new VideoPlugin().Configure(builder);

            Assert.Equal(builder.ReaderVersion, builder.Makes.Single(makes => makes.Kind == DerivedKind.Preview).Version);
        }

        private sealed class Builder : IPluginBuilder
        {
            public IServiceCollection Services { get; } = new ServiceCollection();

            public Type? Reader { get; private set; }

            public IReadOnlyList<string> Read { get; private set; } = [];

            public int ReaderVersion { get; private set; }

            public Type? Maker { get; private set; }

            public IReadOnlyList<DerivedFileDeclaration> Makes { get; private set; } = [];

            public void AddReader<TReader>(int version, IReadOnlyList<string> mediaTypes)
                where TReader : class, IContentDocumentReader, new()
            {
                Reader = typeof(TReader);
                Read = mediaTypes;
                ReaderVersion = version;
            }

            public void AddMaker<TMaker>(params IReadOnlyList<DerivedFileDeclaration> makes)
                where TMaker : class, IDerivedFileMaker, new()
            {
                Maker = typeof(TMaker);
                Makes = makes;
            }
        }
    }
}
