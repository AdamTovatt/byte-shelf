using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>What the image maker makes, taken as the server takes it (<see cref="MadeFiles"/>).</summary>
    internal static class ImageMadeFiles
    {
        extension(MadeFiles)
        {
            /// <summary>Every kind the plugin declares it makes, as it declares them (<c>ImagesPluginTests</c>).</summary>
            public static IReadOnlyList<DerivedKind> DeclaredKinds
            {
                get
                {
                    RecordingPluginBuilder builder = new RecordingPluginBuilder();
                    new ImagesPlugin().Configure(builder);
                    return [.. builder.Makes.Select(makes => makes.Kind)];
                }
            }

            /// <summary>Makes <paramref name="kinds"/> from <paramref name="contents"/>, or every kind when there are none.</summary>
            public static Task<MadeFiles> MakeAsync(byte[] contents, params IReadOnlyList<DerivedKind> kinds)
            {
                return MadeFiles.MakeAsync(new ImageMaker(), contents, kinds.Count == 0 ? MadeFiles.DeclaredKinds : kinds);
            }

            public static Task<MadeFiles> MakeAsync(Stream contents, params IReadOnlyList<DerivedKind> kinds)
            {
                return MadeFiles.MakeAsync(new ImageMaker(), contents, kinds.Count == 0 ? MadeFiles.DeclaredKinds : kinds);
            }

            /// <summary>Makes <paramref name="kind"/> from <paramref name="contents"/>, and decodes it.</summary>
            public static async Task<MagickImage> MakeOneAsync(byte[] contents, DerivedKind kind)
            {
                return (await MadeFiles.MakeAsync(contents, kind)).Decode(kind);
            }
        }

        extension(MadeFiles made)
        {
            /// <summary>The file of <paramref name="kind"/> as the type it says it is.</summary>
            public MagickImage Decode(DerivedKind kind)
            {
                MagickFormat format = made.MediaTypeOf(kind) == "image/webp" ? MagickFormat.WebP : MagickFormat.Jpeg;
                return new MagickImage(made.BytesOf(kind), new MagickReadSettings { Format = format });
            }
        }
    }
}
