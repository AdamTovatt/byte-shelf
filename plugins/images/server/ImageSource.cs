using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>
    /// Contents that are an image the plugin reads, by their own bytes, of a size it decodes; or the JPEG a camera's raw
    /// carries, in their place. Nothing of it is decoded yet.
    /// </summary>
    internal sealed class ImageSource
    {
        /// <summary>What <see cref="ImageFormat.DecodesSmaller"/> counts in.</summary>
        private const uint Scales = 8;

        private readonly Codecs _codecs;
        private readonly Stream _contents;
        private readonly ImageFormat _format;
        private readonly uint _storedWidth;
        private readonly uint _storedHeight;

        private ImageSource(Codecs codecs, Stream contents, ImageFormat format, IMagickImage<byte> pinged, bool isCameraRaw)
        {
            _codecs = codecs;
            _contents = contents;
            _format = format;
            (_storedWidth, _storedHeight) = ShownSize(pinged, format);
            IsCameraRaw = isCameraRaw;
        }

        /// <summary>Whether it is a camera's raw, or the JPEG its camera made of it.</summary>
        public bool IsCameraRaw { get; }

        /// <summary>
        /// The image that <paramref name="contents"/> are, or of a camera's raw the JPEG it carries when that is large
        /// enough; null when they are none the plugin reads.
        /// </summary>
        /// <exception cref="MagickException">They are damaged.</exception>
        public static async Task<ImageSource?> OpenAsync(Codecs codecs, Stream contents)
        {
            if (contents.Length > ImageLimits.MaxSourceBytes)
            {
                return null;
            }

            byte[] head = new byte[ImageFormat.HeadBytes];
            int read = await contents.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false);
            if (ImageFormat.Of(head.AsSpan(0, read)) is not { } format)
            {
                return null;
            }

            using MagickImageCollection images = new MagickImageCollection();
            if (format.MayBeCameraRaw
                && await CameraRaw.MayBeAsync(contents, head.AsMemory(0, read))
                && CameraRaw.Ping(codecs, images, contents, ReadSettings(ImageFormat.Raw)))
            {
                IMagickImage<byte> raw = Shown(images);
                return OfCamerasJpeg(codecs, raw) ?? Of(codecs, contents, ImageFormat.Raw, raw, isCameraRaw: true);
            }

            codecs.Ping(images, contents, ReadSettings(format));
            return Of(codecs, contents, format, Shown(images), isCameraRaw: false);
        }

        /// <summary>
        /// The whole image; or, when only <paramref name="longSide"/> pixels of its long side are wanted, smaller where
        /// decoding it so takes less, but no smaller than that.
        /// </summary>
        /// <exception cref="MagickException">It is damaged, or takes too much memory.</exception>
        public DecodedImage Decode(int? longSide)
        {
            MagickReadSettings settings = ReadSettings(_format);
            if (_format.DecodesSmaller && longSide is { } wanted)
            {
                uint scale = Math.Clamp((uint)Math.Ceiling((double)wanted * Scales / Math.Max(_storedWidth, _storedHeight)), 1, Scales);

                // ImageMagick decodes at the scale nearest to this size.
                settings.SetDefine("jpeg:size", $"{_storedWidth * scale / Scales}x{_storedHeight * scale / Scales}");
            }

            using MagickImageCollection images = new MagickImageCollection();
            _codecs.Read(images, _contents, settings);
            IMagickImage<byte> image = Shown(images);
            if (ShownSize(image, _format) != (image.Width, image.Height))
            {
                // The rest of what is shown is transparent.
                image.Alpha(AlphaAction.Set);
                images.Coalesce();
                image = Shown(images);
            }

            images.Remove(image);
            Colors.AddDisplayP3ProfileIfMarked(image);
            (int width, int height) = UprightSize(image.Orientation);
            return new DecodedImage(image, width, height);
        }

        /// <summary>The image <paramref name="pinged"/> is of <paramref name="contents"/>; null when it has more pixels than are decoded, or is a smaller copy.</summary>
        private static ImageSource? Of(Codecs codecs, Stream contents, ImageFormat format, IMagickImage<byte> pinged, bool isCameraRaw)
        {
            (uint width, uint height) = ShownSize(pinged, format);
            return Math.Max((ulong)pinged.Width * pinged.Height, (ulong)width * height) <= ImageLimits.MaxPixels && !IsSmallerCopy(pinged)
                ? new ImageSource(codecs, contents, format, pinged, isCameraRaw)
                : null;
        }

        /// <summary>
        /// The JPEG the camera made of the <paramref name="raw"/> pinged, when it has the pixels of every kind but the
        /// largest previews, as many as an agent's has; null when there is none or a smaller one.
        /// </summary>
        /// <exception cref="MagickException">It is damaged.</exception>
        private static ImageSource? OfCamerasJpeg(Codecs codecs, IMagickImage<byte> raw)
        {
            if (CameraRaw.JpegOf(raw) is not { } jpeg)
            {
                return null;
            }

            MemoryStream contents = new MemoryStream(jpeg, writable: false);
            using MagickImageCollection images = new MagickImageCollection();
            codecs.Ping(images, contents, ReadSettings(ImageFormat.Jpeg));
            IMagickImage<byte> pinged = Shown(images);
            return Math.Max(pinged.Width, pinged.Height) >= ImageLimits.AgentLongSide ? Of(codecs, contents, ImageFormat.Jpeg, pinged, isCameraRaw: true) : null;
        }

        private static MagickReadSettings ReadSettings(ImageFormat format)
        {
            MagickReadSettings settings = new MagickReadSettings { Format = format.Magick };
            if (!format.LargestIsShown)
            {
                settings.FrameIndex = 0;
            }

            if (format.IsCameraRaw)
            {
                CameraRaw.SetDecoding(settings);
            }

            return settings;
        }

        /// <summary>The one of <paramref name="images"/> that is shown.</summary>
        private static IMagickImage<byte> Shown(MagickImageCollection images)
        {
            return images.MaxBy(image => (long)image.Width * image.Height)
                ?? throw new InvalidDataException("There is no image.");
        }

        /// <summary>The size <paramref name="image"/> is shown at: that of the animation it is the first frame of, which may be larger.</summary>
        private static (uint Width, uint Height) ShownSize(IMagickImage<byte> image, ImageFormat format)
        {
            return format.FirstFrameMayBePart && image.Page is { Width: > 0, Height: > 0 } canvas
                ? (canvas.Width, canvas.Height)
                : (image.Width, image.Height);
        }

        /// <summary>Whether a TIFF says its first page is a smaller copy of another: as in a camera's raw that LibRaw does not read, whose picture is not one.</summary>
        private static bool IsSmallerCopy(IMagickImage<byte> image)
        {
            return image.GetAttribute("tiff:subfiletype") == "REDUCEDIMAGE";
        }

        /// <summary>The image's size upright, by the <paramref name="orientation"/> it was decoded with: not every format says it before.</summary>
        private (int Width, int Height) UprightSize(Orientation orientation)
        {
            return orientation is Orientation.LeftTop or Orientation.RightTop or Orientation.RightBottom or Orientation.LeftBottom
                ? ((int)_storedHeight, (int)_storedWidth)
                : ((int)_storedWidth, (int)_storedHeight);
        }
    }
}
