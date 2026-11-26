using System.Collections.Frozen;
using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>
    /// ImageMagick, kept to the formats policy.xml names and within <see cref="ImageLimits"/>; every read goes through one.
    /// Its own settings allow it every format it has, some of which read other files. Pixels stay in memory; a HEIC, an
    /// AVIF and what may be a camera's raw, which LibRaw (ImageMagick's DNG) is asked about, are copied to a temporary file
    /// to be read, which is in the working folder and gone once it has been.
    /// </summary>
    internal sealed class Codecs
    {
        private static readonly Lock Preparing = new Lock();

        private static readonly FrozenSet<MagickFormat> Allowed = new[]
        {
            MagickFormat.Jpeg, MagickFormat.Png, MagickFormat.Gif, MagickFormat.WebP, MagickFormat.Avif,
            MagickFormat.Heic, MagickFormat.Tiff, MagickFormat.Bmp, MagickFormat.Ico, MagickFormat.Dng,
        }.ToFrozenSet();

        private static Codecs? _prepared;

        private Codecs()
        {
        }

        /// <summary>Limits it, once, before it is used at all.</summary>
        /// <exception cref="InvalidOperationException">It does not go by the limits.</exception>
        public static Codecs Prepare()
        {
            lock (Preparing)
            {
                if (_prepared == null)
                {
                    // Read from there as it is first used, when it is too late to change.
                    MagickNET.SetEnvironmentVariable("MAGICK_CONFIGURE_PATH", Path.GetDirectoryName(typeof(Codecs).Assembly.Location)!);
                    MagickNET.Initialize();

                    // Beyond the first two an image's pixels would go to a file, of which it may have none.
                    ResourceLimits.Area = ImageLimits.MaxPixels + 1;
                    ResourceLimits.Memory = ImageLimits.MaxMemoryBytes;
                    ResourceLimits.Disk = 0;
                    ResourceLimits.MaxMemoryRequest = ImageLimits.MaxMemoryBytes;
                    if (Problem() is { } problem)
                    {
                        throw new InvalidOperationException($"ImageMagick does not go by the plugin's limits: {problem}. Another policy.xml on this machine may be in the way.");
                    }

                    _prepared = new Codecs();
                }

                return _prepared;
            }
        }

        /// <summary>
        /// What ImageMagick does not go by; null when it does. Its policy is added to by whatever other policy.xml it finds,
        /// on Linux also in /tmp/ImageMagick, where anyone on the machine may put one.
        /// </summary>
        public static string? Problem()
        {
            foreach (MagickFormat format in Enum.GetValues<MagickFormat>())
            {
                if (format != MagickFormat.Unknown && Reads(format) != Allowed.Contains(format))
                {
                    return $"it {(Allowed.Contains(format) ? "does not read" : "reads")} {format}";
                }
            }

            return (ResourceLimits.Area, ResourceLimits.Memory, ResourceLimits.Disk, ResourceLimits.MaxMemoryRequest)
                != (ImageLimits.MaxPixels + 1, ImageLimits.MaxMemoryBytes, 0, ImageLimits.MaxMemoryBytes)
                || Math.Min(ResourceLimits.Width, ResourceLimits.Height) < ImageLimits.MaxPixels
                ? "its memory or its sizes are limited otherwise"
                : null;
        }

        /// <summary>Reads what <paramref name="contents"/> say of themselves, and decodes nothing.</summary>
        public void Ping(MagickImageCollection images, Stream contents, MagickReadSettings settings)
        {
            contents.Position = 0;
            images.Ping(contents, settings);
        }

        /// <summary>Decodes <paramref name="contents"/>.</summary>
        public void Read(MagickImageCollection images, Stream contents, MagickReadSettings settings)
        {
            contents.Position = 0;
            images.Read(contents, settings);
        }

        /// <summary>Whether it would read <paramref name="format"/>: it is asked to read a byte as one, which none can be.</summary>
        private static bool Reads(MagickFormat format)
        {
            try
            {
                using MagickImage image = new MagickImage([0], new MagickReadSettings { Format = format, Width = 1, Height = 1 });
                return true;
            }
            catch (Exception exception) when (exception is MagickPolicyErrorException or MagickMissingDelegateErrorException)
            {
                return false;
            }
            catch (MagickException)
            {
                // It tried.
                return true;
            }
        }
    }
}
