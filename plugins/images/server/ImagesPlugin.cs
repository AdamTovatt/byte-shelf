using ByteShelfCommon;

namespace ByteShelf.Plugins.Images
{
    /// <summary>Reads images' sizes, and makes smaller copies of them and ones browsers and agents can see.</summary>
    public sealed class ImagesPlugin : IByteShelfPlugin
    {
        /// <summary>What the contents' own bytes are decides (<see cref="ImageFormat"/>), so one that is none of the formats, as an SVG, is made into nothing.</summary>
        private static readonly string[] Images = ["image/*"];

        /// <summary>The types of those formats that browsers do not show.</summary>
        private static readonly string[] NotShownByBrowsers = ["image/heic", "image/heif", "image/heic-sequence", "image/heif-sequence", "image/tiff"];

        public void Configure(IPluginBuilder builder)
        {
            builder.AddReader<ImageReader>(2, Images);
            builder.AddMaker<ImageMaker>(
                new DerivedFileDeclaration(DerivedKind.Thumbnail, 2, Images),
                new DerivedFileDeclaration(DerivedKind.Preview, 2, Images),
                new DerivedFileDeclaration(DerivedKind.Full, 1, NotShownByBrowsers),
                new DerivedFileDeclaration(DerivedKind.Agent, 2, Images));
        }
    }
}
