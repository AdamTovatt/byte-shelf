using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>
    /// The colours of decoded images: what is made of one shows those it has. Dropping a profile does not, as the same
    /// numbers then mean sRGB's: a wide colour turns dull.
    /// </summary>
    internal static class Colors
    {
        public const string ProfileName = "icc";

        /// <summary>An ICC profile's header, and where in it are the profile's length, what its numbers are of, and what says it is one.</summary>
        private const int IccHeaderBytes = 128;
        private const int IccSpaceOffset = 16;
        private const int IccSignatureOffset = 36;

        private const string IccRgb = "RGB ";
        private const string IccGray = "GRAY";
        private const string IccCmyk = "CMYK";

        /// <summary>
        /// Gives <paramref name="image"/> the profile of what it is marked as without one, if that is Display P3: an AVIF or
        /// HEIC says it by numbers of H.273 (primaries 12, transfer 13). One marked as anything else is taken for sRGB, as is
        /// a PNG marked by such numbers (cICP), which ImageMagick does not read.
        /// </summary>
        public static void AddDisplayP3ProfileIfMarked(IMagickImage<byte> image)
        {
            if (image.GetProfile(ProfileName) == null && image.GetAttribute("heic:cicp")?.Split('/') is ["12", "13", ..])
            {
                image.SetProfile(new ImageProfile(ProfileName, DisplayP3Profile.Bytes));
            }
        }

        /// <summary>
        /// Leaves <paramref name="image"/> its numbers and the profile that says what they mean, where they are RGB or have
        /// none. One whose are not, as in a CMYK print, or whose profile is large, becomes sRGB.
        /// </summary>
        public static void KeepRgbProfile(IMagickImage<byte> image)
        {
            byte[]? profile = ProfileOf(image);
            if (IsRgbOrGray(image) && (profile == null || (SpaceOf(profile) == IccRgb && profile.Length <= ImageLimits.MaxProfileBytes)))
            {
                return;
            }

            ToSrgb(image);
        }

        /// <summary>Turns <paramref name="image"/> into sRGB, with no profile: one without, or with a damaged one, is taken for sRGB.</summary>
        public static void ToSrgb(IMagickImage<byte> image)
        {
            if (ProfileOf(image) is { } profile && SpaceOf(profile) is { } space)
            {
                if (space == IccRgb && image.ColorSpace == ColorSpace.Gray)
                {
                    // A profile of three numbers is for an image of three, and is lost as it becomes one.
                    image.ColorSpace = ColorSpace.sRGB;
                    image.SetProfile(new ImageProfile(ProfileName, profile));
                }

                if ((space, image.ColorSpace) is (IccRgb, ColorSpace.sRGB) or (IccGray, ColorSpace.Gray) or (IccCmyk, ColorSpace.CMYK))
                {
                    Transform(image);
                }
            }

            image.RemoveProfile(ProfileName);
            image.ColorSpace = ColorSpace.sRGB;
        }

        private static bool IsRgbOrGray(IMagickImage<byte> image)
        {
            return image.ColorSpace is ColorSpace.sRGB or ColorSpace.Gray;
        }

        private static byte[]? ProfileOf(IMagickImage<byte> image)
        {
            return image.GetProfile(ProfileName)?.ToByteArray();
        }

        /// <summary>What the numbers of <paramref name="profile"/> are of; null when it is damaged, cut short for one.</summary>
        private static string? SpaceOf(byte[] profile)
        {
            return profile.Length >= IccHeaderBytes
                && BinaryPrimitives.ReadUInt32BigEndian(profile) <= (uint)profile.Length
                && profile.AsSpan(IccSignatureOffset, 4).SequenceEqual("acsp"u8)
                ? Encoding.Latin1.GetString(profile, IccSpaceOffset, 4)
                : null;
        }

        private static void Transform(IMagickImage<byte> image)
        {
            try
            {
                image.TransformColorSpace(ColorProfiles.SRGB, ColorTransformMode.HighRes);
            }
            catch (MagickException)
            {
                // A damaged profile says nothing.
            }
        }
    }
}
