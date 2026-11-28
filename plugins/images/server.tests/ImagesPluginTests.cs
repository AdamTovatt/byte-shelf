using ByteShelf.Plugins.Testing;
using ByteShelfCommon;

namespace ByteShelf.Plugins.Images.Tests
{
    public sealed class ImagesPluginTests
    {
        [Fact]
        public void Configure_AddsTheReaderAndTheMakerOfImages()
        {
            RecordingPluginBuilder builder = new RecordingPluginBuilder();

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
            RecordingPluginBuilder builder = new RecordingPluginBuilder();

            new ImagesPlugin().Configure(builder);

            Assert.Equal(
                ["image/heic", "image/heif", "image/heic-sequence", "image/heif-sequence", "image/tiff"],
                builder.Makes.Single(makes => makes.Kind == DerivedKind.Full).MediaTypes);
        }

    }
}
