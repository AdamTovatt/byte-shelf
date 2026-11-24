using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>What is done to a decoded image, and how it is written.</summary>
    internal static class Pictures
    {
        /// <summary>Makes <paramref name="image"/> smaller, keeping its shape, until its long side is at most <paramref name="longSide"/>, and turns it upright.</summary>
        public static void ShrinkUpright(IMagickImage<byte> image, int longSide)
        {
            if (Math.Max(image.Width, image.Height) > longSide)
            {
                image.Resize(new MagickGeometry((uint)longSide, (uint)longSide));
            }

            // After, when there is less to turn.
            image.AutoOrient();
        }

        /// <summary>Puts <paramref name="image"/> on white, as a JPEG has no transparent parts.</summary>
        public static void OnWhite(IMagickImage<byte> image)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
            image.Alpha(AlphaAction.Off);
        }

        /// <summary>The bytes of <paramref name="image"/>, with nothing it came with, such as where it was taken, but its colour profile.</summary>
        public static byte[] Encode(IMagickImage<byte> image, MagickFormat format, uint quality)
        {
            Prepare(image, quality);
            return image.ToByteArray(format);
        }

        /// <summary>Writes what <see cref="Encode"/> answers to <paramref name="stream"/>, without all of it in memory.</summary>
        public static void Write(IMagickImage<byte> image, MagickFormat format, uint quality, Stream stream)
        {
            Prepare(image, quality);
            WatchedStream watched = new WatchedStream(stream);
            try
            {
                image.Write(watched, format);
            }
            catch (MagickException)
            {
                // What the stream threw says more than what ImageMagick makes of it.
                watched.ThrowIfFailed();
                throw;
            }

            watched.ThrowIfFailed();
        }

        private static void Prepare(IMagickImage<byte> image, uint quality)
        {
            foreach (string name in image.ProfileNames.Where(name => name != Colors.ProfileName).ToList())
            {
                image.RemoveProfile(name);
            }

            image.RemoveAttribute("comment");
            image.Quality = quality;
        }
    }
}
