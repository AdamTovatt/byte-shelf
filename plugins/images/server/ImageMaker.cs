using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>
    /// Makes smaller copies of images, and copies in types others can see, upright and without what came with the
    /// image besides its pixels and their colours. Of contents that are no image it reads, it makes nothing.
    /// </summary>
    public sealed class ImageMaker : IDerivedFileMaker
    {
        private const string WebP = "image/webp";
        private const string Jpeg = "image/jpeg";
        private const uint FullQuality = 92;

        /// <summary>What an agent's is tried at until it is small enough, as even noise is at the last.</summary>
        private static readonly uint[] AgentQualities = [85, 70, 55, 40];

        /// <summary>The kinds that are a smaller image as it is, from the largest.</summary>
        private static readonly (DerivedKind Kind, int LongSide, uint Quality)[] SmallerKinds =
            [(DerivedKind.Preview, 2048, 85), (DerivedKind.Thumbnail, 320, 80)];

        private readonly Codecs _codecs = Codecs.Prepare();

        public async Task MakeAsync(Stream contents, IReadOnlyList<DerivedKind> kinds, IMadeFiles made)
        {
            if (await ImageSource.OpenAsync(_codecs, contents) is not { } source)
            {
                return;
            }

            // Each from a decode of its own, so a whole image that is turned has the memory to itself. The server asks
            // for thumbnails and previews together, and for the others alone.
            // Of a camera's raw, whatever it is stored as, the preview is the sharpest there is.
            if (kinds.Contains(DerivedKind.Full) && !source.IsCameraRaw)
            {
                await AddFullAsync(source, made);
            }

            if (kinds.Contains(DerivedKind.Agent))
            {
                await AddAgentsAsync(source, made);
            }

            if (SmallerKinds.Any(smaller => kinds.Contains(smaller.Kind)))
            {
                await AddSmallerAsync(source, kinds, made);
            }
        }

        private static async Task AddFullAsync(ImageSource source, IMadeFiles made)
        {
            using DecodedImage decoded = source.Decode(longSide: null);
            IMagickImage<byte> image = decoded.Image;
            Pictures.ShrinkUpright(image, ImageLimits.MaxJpegLongSide);
            Colors.KeepRgbProfile(image);
            Pictures.OnWhite(image);

            // A gray one would be written as such, which an RGB profile is not for.
            image.Settings.ColorType = ColorType.TrueColor;
            await made.AddAsync(DerivedKind.Full, Jpeg, file => WriteAsync(image, MagickFormat.Jpeg, FullQuality, file));
        }

        private static async Task AddAgentsAsync(ImageSource source, IMadeFiles made)
        {
            using DecodedImage decoded = source.Decode(ImageLimits.AgentLongSide);
            IMagickImage<byte> image = decoded.Image;
            Pictures.ShrinkUpright(image, ImageLimits.AgentLongSide);
            Colors.ToSrgb(image);
            Pictures.OnWhite(image);
            byte[] jpeg = AgentQualities
                .Select(quality => Pictures.Encode(image, MagickFormat.Jpeg, quality))
                .First(encoded => encoded.Length <= DerivedFileLimits.MaxBytesOf(DerivedKind.Agent));
            await made.AddAsync(DerivedKind.Agent, Jpeg, file => file.WriteAsync(jpeg).AsTask());
        }

        /// <summary>The smaller kinds that are asked for, from one decode, each from the one before.</summary>
        private static async Task AddSmallerAsync(ImageSource source, IReadOnlyList<DerivedKind> kinds, IMadeFiles made)
        {
            (DerivedKind Kind, int LongSide, uint Quality)[] asked = [.. SmallerKinds.Where(smaller => kinds.Contains(smaller.Kind))];
            using DecodedImage decoded = source.Decode(asked[0].LongSide);
            foreach ((DerivedKind kind, int longSide, uint quality) in asked)
            {
                Pictures.ShrinkUpright(decoded.Image, longSide);
                Colors.KeepRgbProfile(decoded.Image);
                await made.AddAsync(kind, WebP, file => WriteAsync(decoded.Image, MagickFormat.WebP, quality, file));
            }
        }

        private static Task WriteAsync(IMagickImage<byte> image, MagickFormat format, uint quality, Stream file)
        {
            Pictures.Write(image, format, quality, file);
            return Task.CompletedTask;
        }
    }
}
