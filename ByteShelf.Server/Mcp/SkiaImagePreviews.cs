using ByteShelf.Tools.Mcp;
using SkiaSharp;

namespace ByteShelf.Server.Mcp
{
    /// <summary>
    /// <c>/mcp</c>'s image previews, decoded and drawn by SkiaSharp: any image Skia decodes (PNG, JPEG,
    /// GIF's first frame, WebP, BMP, ICO and others; not HEIC on Linux), made one at a time.
    /// </summary>
    internal sealed class SkiaImagePreviews : IImagePreviews
    {
        /// <summary>
        /// The most memory an image's pixels may take while a preview is drawn from them: decoded, after
        /// the scaling its format can do while decoding (a JPEG to an eighth), with the smaller copies
        /// (mipmaps) drawing it down builds. A small file that decodes to a huge image is never decoded.
        /// </summary>
        public const long MaxDecodedBytes = 256L * 1024 * 1024;

        private const int BytesPerPixel = 4;
        private const int FirstQuality = 85;
        private const int LowestQuality = 40;
        private const int QualityStep = 15;

        /// <summary>
        /// One preview is read and made at a time in the process, so previews never hold more than one
        /// image and <see cref="MaxDecodedBytes"/> of pixels together, however many calls wait.
        /// </summary>
        private static readonly SemaphoreSlim OneAtATime = new SemaphoreSlim(1, 1);

        public async Task<PreviewOutcome> MakeAsync(Func<CancellationToken, Task<byte[]>> readContent, CancellationToken cancellationToken)
        {
            await OneAtATime.WaitAsync(cancellationToken);
            try
            {
                return Make(await readContent(cancellationToken), cancellationToken);
            }
            finally
            {
                OneAtATime.Release();
            }
        }

        /// <summary>
        /// Where a stored pixel (x, y) is drawn in the upright image of <paramref name="width"/> by
        /// <paramref name="height"/>, for each EXIF orientation.
        /// </summary>
        internal static SKMatrix ToUpright(SKEncodedOrigin origin, float width, float height)
        {
            // x' = scaleX x + skewX y + transX; y' = skewY x + scaleY y + transY.
            return origin switch
            {
                SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
                SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
                SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
                SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightTop => new SKMatrix(0, -1, width, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, width, -1, 0, height, 0, 0, 1),
                SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, height, 0, 0, 1),
                _ => SKMatrix.Identity,
            };
        }

        /// <summary>The preview of <paramref name="content"/>, or why there is none; cancelled between its steps, since decoding and encoding cannot be stopped part way.</summary>
        private static PreviewOutcome Make(byte[] content, CancellationToken cancellationToken)
        {
            // The codec owns the stream it reads, and disposes it with itself. No codec (null, whatever
            // the annotation says) comes with a result other than Success.
            using SKCodec codec = SKCodec.Create(new SKMemoryStream(content), out SKCodecResult opened);
            if (opened != SKCodecResult.Success)
            {
                return new PreviewOutcome.NotMade(NoPreview.NotDecodable);
            }

            SKSizeI stored = codec.Info.Size;
            float scale = Math.Min(1f, (float)ImagePreview.LongSide / Math.Max(stored.Width, stored.Height));
            SKSizeI decodedSize = codec.GetScaledDimensions(scale);
            long decodedBytes = (long)decodedSize.Width * decodedSize.Height * BytesPerPixel;
            // Mipmaps add a third: each level is a quarter of the one above it.
            if (decodedBytes + (decodedBytes / 3) > MaxDecodedBytes)
            {
                return new PreviewOutcome.NotMade(NoPreview.TooManyPixels);
            }

            SKImageInfo decodedInfo = new SKImageInfo(decodedSize.Width, decodedSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using SKBitmap decoded = new SKBitmap(decodedInfo);
            SKCodecResult result = codec.GetPixels(decodedInfo, decoded.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                return new PreviewOutcome.NotMade(NoPreview.NotDecodable);
            }

            cancellationToken.ThrowIfCancellationRequested();
            SKEncodedOrigin origin = codec.EncodedOrigin;
            SKSizeI upright = origin >= SKEncodedOrigin.LeftTop ? new SKSizeI(stored.Height, stored.Width) : stored;
            // Immutable, so the image shares the bitmap's pixels rather than copying them.
            decoded.SetImmutable();
            using SKImage image = SKImage.FromBitmap(decoded);
            SKSizeI size = Fit(upright, ImagePreview.LongSide);
            return SmallEnough(image, origin, size, cancellationToken) is byte[] jpeg
                ? new PreviewOutcome.Made(new ImagePreview(jpeg, size.Width, size.Height, upright.Width, upright.Height, MediaTypeOf(codec.EncodedFormat)))
                : new PreviewOutcome.NotMade(NoPreview.TooDetailed);
        }

        /// <summary>
        /// <paramref name="image"/> drawn upright at <paramref name="size"/> as a JPEG of at most
        /// <see cref="ByteShelfMcpTools.MaxInlineImageBytes"/>, at a lower quality where fine detail
        /// needs it; null when even the lowest is larger, which noise in every pixel of a full-size
        /// square still is not.
        /// </summary>
        private static byte[]? SmallEnough(SKImage image, SKEncodedOrigin origin, SKSizeI size, CancellationToken cancellationToken)
        {
            using SKImage drawn = Upright(image, origin, size);
            for (int quality = FirstQuality; quality >= LowestQuality; quality -= QualityStep)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using SKData jpeg = drawn.Encode(SKEncodedImageFormat.Jpeg, quality);
                if (jpeg.Size <= ByteShelfMcpTools.MaxInlineImageBytes)
                {
                    return jpeg.ToArray();
                }
            }

            return null;
        }

        /// <summary><paramref name="image"/>, stored as <paramref name="origin"/> says, drawn upright on white at <paramref name="size"/>.</summary>
        private static SKImage Upright(SKImage image, SKEncodedOrigin origin, SKSizeI size)
        {
            bool turned = origin >= SKEncodedOrigin.LeftTop;
            float uprightWidth = turned ? image.Height : image.Width;
            float uprightHeight = turned ? image.Width : image.Height;

            using SKSurface surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            canvas.Scale(size.Width / uprightWidth, size.Height / uprightHeight);
            canvas.Concat(ToUpright(origin, uprightWidth, uprightHeight));
            // Mipmaps, so a large step down averages what it drops rather than skipping it.
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return surface.Snapshot();
        }

        /// <summary><paramref name="size"/> scaled down, keeping its shape, until its long side is at most <paramref name="longSide"/>.</summary>
        private static SKSizeI Fit(SKSizeI size, int longSide)
        {
            double scale = Math.Min(1.0, (double)longSide / Math.Max(size.Width, size.Height));
            return new SKSizeI(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
        }

        private static string MediaTypeOf(SKEncodedImageFormat format)
        {
            return format switch
            {
                SKEncodedImageFormat.Jpeg => "image/jpeg",
                SKEncodedImageFormat.Ico => "image/x-icon",
                SKEncodedImageFormat.Wbmp => "image/vnd.wap.wbmp",
                SKEncodedImageFormat.Dng => "image/x-adobe-dng",
                _ => "image/" + format.ToString().ToLowerInvariant(),
            };
        }
    }
}
