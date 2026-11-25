using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Plugins.Images.Tests
{
    public sealed class ImagesPluginTests
    {
        [Fact]
        public void Configure_AddsTheReaderAndTheMakerOfImages()
        {
            Builder builder = new Builder();

            new ImagesPlugin().Configure(builder);

            Assert.Equal(typeof(ImageReader), builder.Reader);
            Assert.Equal(["image/*"], builder.Read);
            Assert.Equal(typeof(ImageMaker), builder.Maker);
            Assert.Empty(builder.Services);
            Assert.Equal(
                [DerivedKind.Thumbnail, DerivedKind.Preview, DerivedKind.Full, DerivedKind.Agent],
                builder.Makes.Select(makes => makes.Kind));
            Assert.All(builder.Makes.Where(makes => makes.Kind != DerivedKind.Full), makes => Assert.Equal(["image/*"], makes.MediaTypes));
        }

        /// <summary>A browser shows the others' contents themselves.</summary>
        [Fact]
        public void Configure_MakesFullOfWhatBrowsersDoNotShow()
        {
            Builder builder = new Builder();

            new ImagesPlugin().Configure(builder);

            Assert.Equal(
                ["image/heic", "image/heif", "image/heic-sequence", "image/heif-sequence", "image/tiff"],
                builder.Makes.Single(makes => makes.Kind == DerivedKind.Full).MediaTypes);
        }

        private sealed class Builder : IPluginBuilder
        {
            public IServiceCollection Services { get; } = new ServiceCollection();

            public Type? Reader { get; private set; }

            public IReadOnlyList<string> Read { get; private set; } = [];

            public Type? Maker { get; private set; }

            public IReadOnlyList<DerivedFileDeclaration> Makes { get; private set; } = [];

            public void AddReader<TReader>(int version, IReadOnlyList<string> mediaTypes)
                where TReader : class, IContentDocumentReader, new()
            {
                Reader = typeof(TReader);
                Read = mediaTypes;
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
