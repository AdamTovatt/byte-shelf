using System.Buffers.Binary;
using System.Text;
using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    /// <summary>
    /// The colours of what is made. Seen on a screen that shows Display P3, a pure red of it is (255, 0, 0); made into
    /// something without its profile, the same numbers are sRGB's red, which is (234, 51, 35) there.
    /// </summary>
    public sealed class ColorTests
    {
        private static readonly IMagickColor<byte> DullRed = new MagickColor(234, 51, 35);

        private static readonly ColorProfile DisplayP3 = new ColorProfile(DisplayP3Profile.Bytes);

        /// <summary>Display P3's (200, 100, 100) and (50, 120, 200) in sRGB, worked out from the two standards' primaries rather than by a profile.</summary>
        [Theory]
        [InlineData(200, 100, 100, 215, 93, 97)]
        [InlineData(50, 120, 200, 0, 122, 206)]
        [InlineData(128, 128, 128, 128, 128, 128)]
        public void DisplayP3Profile_IsDisplayP3(byte red, byte green, byte blue, int expectedRed, int expectedGreen, int expectedBlue)
        {
            using MagickImage image = TestImages.Plain(8, 8, red, green, blue);
            image.SetProfile(DisplayP3);

            image.TransformColorSpace(ColorProfiles.SRGB, ColorTransformMode.HighRes);

            ImageAssert.Color(new MagickColor((byte)expectedRed, (byte)expectedGreen, (byte)expectedBlue), image, tolerance: 2);
        }

        [Theory]
        [InlineData("jpeg")]
        [InlineData("png")]
        [InlineData("webp")]
        [InlineData("avif")]
        [InlineData("tiff")]
        [InlineData("heic")]
        public async Task MakeAsync_KeepsADisplayP3RedAndItsProfile(string format)
        {
            byte[] contents = format == "heic" ? HeicFile.Make(icc: DisplayP3Profile.Bytes) : DisplayP3Red(format);
            DerivedKind[] kinds = [DerivedKind.Thumbnail, DerivedKind.Preview, DerivedKind.Full];

            MadeFiles made = await MadeFiles.MakeAsync(contents, kinds);

            foreach (DerivedKind kind in kinds)
            {
                using MagickImage image = made.Decode(kind);
                Assert.Equal(DisplayP3Profile.Bytes, image.GetProfile("icc")?.ToByteArray());
                ImageAssert.Color(MagickColors.Red, image);
                AssertShownInDisplayP3(MagickColors.Red, image);
            }
        }

        [Fact]
        public void AssertShownInDisplayP3_SeesAProfileThatWasDropped()
        {
            using MagickImage image = new MagickImage(DisplayP3Red("png"), MagickFormat.Png);
            image.RemoveProfile("icc");

            AssertShownInDisplayP3(DullRed, image);
        }

        /// <summary>An iPhone's has a profile; other HEICs, and AVIFs, may say it in numbers.</summary>
        [Fact]
        public async Task MakeAsync_GivesOneMarkedAsDisplayP3TheProfile()
        {
            MadeFiles made = await MadeFiles.MakeAsync(HeicFile.Make(primaries: HeicFile.DisplayP3Primaries), DerivedKind.Preview, DerivedKind.Full, DerivedKind.Agent);

            foreach (DerivedKind kind in new[] { DerivedKind.Preview, DerivedKind.Full })
            {
                using MagickImage image = made.Decode(kind);
                AssertShownInDisplayP3(MagickColors.Red, image);
            }

            using MagickImage agent = made.Decode(DerivedKind.Agent);
            Assert.Empty(agent.ProfileNames);
        }

        [Fact]
        public async Task MakeAsync_GivesAnAvifMarkedAsDisplayP3TheProfile()
        {
            using MagickImage image = TestImages.Plain(40, 20, 255, 0, 0);
            image.Settings.SetDefine("heic:cicp", "12/13/6/1");

            using MagickImage preview = await MadeFiles.MakeOneAsync(TestImages.Of("avif", image), DerivedKind.Preview);

            AssertShownInDisplayP3(MagickColors.Red, preview);
        }

        /// <summary>sRGB, and Display P3's primaries with an HDR transfer (PQ), whose numbers are no Display P3's.</summary>
        [Theory]
        [InlineData(HeicFile.SrgbPrimaries, HeicFile.SrgbTransfer)]
        [InlineData(HeicFile.DisplayP3Primaries, HeicFile.PqTransfer)]
        public async Task MakeAsync_GivesOneMarkedAsAnythingElseNoProfile(ushort primaries, ushort transfer)
        {
            using MagickImage preview = await MadeFiles.MakeOneAsync(HeicFile.Make(primaries: primaries, transfer: transfer), DerivedKind.Preview);

            Assert.Empty(preview.ProfileNames);
        }

        [Fact]
        public async Task MakeAsync_TurnsAnAgentsIntoSrgb()
        {
            using MagickImage image = TestImages.Plain(40, 20, 200, 100, 100);
            image.SetProfile(DisplayP3);

            using MagickImage agent = await MadeFiles.MakeOneAsync(TestImages.Jpeg(image), DerivedKind.Agent);

            Assert.Empty(agent.ProfileNames);
            ImageAssert.Color(new MagickColor(215, 93, 97), agent);
        }

        [Theory]
        [InlineData("jpeg")]
        [InlineData("tiff")]
        public async Task MakeAsync_TurnsAPrintIntoSrgb(string format)
        {
            using MagickImage image = TestImages.Plain(40, 20, 200, 100, 100);
            image.SetProfile(ColorProfiles.SRGB);
            image.TransformColorSpace(ColorProfiles.USWebCoatedSWOP);
            Assert.Equal(ColorSpace.CMYK, image.ColorSpace);

            MadeFiles made = await MadeFiles.MakeAsync(TestImages.Of(format, image));

            // There and back through a press's inks, which are of fewer colours than sRGB, rounds more than a lossy file does.
            AssertSrgb(new MagickColor(200, 100, 100), made, tolerance: 12);
        }

        [Fact]
        public async Task MakeAsync_TurnsAPrintWithoutAProfileIntoSrgb()
        {
            using MagickImage image = TestImages.Plain(40, 20, 255, 0, 0);
            image.ColorSpace = ColorSpace.CMYK;

            MadeFiles made = await MadeFiles.MakeAsync(TestImages.Jpeg(image));

            AssertSrgb(MagickColors.Red, made, tolerance: 6);
        }

        [Fact]
        public async Task MakeAsync_MakesAGray()
        {
            MadeFiles made = await MadeFiles.MakeAsync(GrayJpeg(100));

            AssertSrgb(new MagickColor(100, 100, 100), made, tolerance: 3);
        }

        /// <summary>Its one profile is of grays, which a WebP cannot have. Those of this one are linear: half the light is sRGB's 188.</summary>
        [Fact]
        public async Task MakeAsync_TurnsAGrayWithAProfileIntoSrgb()
        {
            MadeFiles made = await MadeFiles.MakeAsync(WithProfile(GrayJpeg(128), LinearProfile("GRAY")));

            // A gray is one number, so a lossy file of it is off by less than a colour's three.
            AssertSrgb(new MagickColor(188, 188, 188), made, tolerance: 4);
        }

        /// <summary>As <see cref="MakeAsync_TurnsAGrayWithAProfileIntoSrgb"/>, of a PNG.</summary>
        [Fact]
        public async Task MakeAsync_TurnsAGrayPngWithAProfileIntoSrgb()
        {
            using MagickImage image = TestImages.Plain(40, 20, 128, 128, 128);
            image.ColorSpace = ColorSpace.Gray;
            image.SetProfile(new ImageProfile("icc", LinearProfile("GRAY")));

            MadeFiles made = await MadeFiles.MakeAsync(image.ToByteArray(MagickFormat.Png));

            AssertSrgb(new MagickColor(188, 188, 188), made, tolerance: 4);
        }

        /// <summary>A profile of three numbers is kept only with three, and is gone through for an agent's.</summary>
        [Fact]
        public async Task MakeAsync_KeepsAGraysRgbProfile_WithThreeNumbersAPixel()
        {
            byte[] profile = LinearProfile("RGB ");

            MadeFiles made = await MadeFiles.MakeAsync(WithProfile(GrayJpeg(128), profile));

            using MagickImage full = made.Decode(DerivedKind.Full);
            Assert.Equal((3u, ColorSpace.sRGB), (full.ChannelCount, full.ColorSpace));
            Assert.Equal(profile, full.GetProfile("icc")?.ToByteArray());
            ImageAssert.Color(new MagickColor(128, 128, 128), full, tolerance: 3);
            using MagickImage agent = made.Decode(DerivedKind.Agent);
            ImageAssert.Color(new MagickColor(188, 188, 188), agent, tolerance: 4);
        }

        /// <summary>Kept, it would be the most of every thumbnail, or more than one may be.</summary>
        [Theory]
        [InlineData(64 * 1024, true)]
        [InlineData((64 * 1024) + 4, false)]
        [InlineData(1_500_000, false)]
        public async Task MakeAsync_TurnsOneWithALargeProfileIntoSrgb(int length, bool isKept)
        {
            using MagickImage image = TestImages.Plain(40, 20, 200, 100, 100);
            image.SetProfile(new ImageProfile("icc", Padded(DisplayP3Profile.Bytes, length)));

            MadeFiles made = await MadeFiles.MakeAsync(TestImages.Of("tiff", image));

            Assert.Equal(MadeFiles.AllKinds.Order(), made.Kinds);
            Assert.All(new[] { DerivedKind.Thumbnail, DerivedKind.Preview, DerivedKind.Full }, kind =>
            {
                using MagickImage decoded = made.Decode(kind);
                Assert.Equal(isKept, decoded.ProfileNames.Any());
                ImageAssert.Color(isKept ? new MagickColor(200, 100, 100) : new MagickColor(215, 93, 97), decoded);
            });
        }

        /// <summary>One too short to be any, one that is all header, and one cut short after it: what it is in is taken for sRGB.</summary>
        [Theory]
        [InlineData(4)]
        [InlineData(19)]
        [InlineData(100)]
        [InlineData(128)]
        [InlineData(200)]
        public async Task MakeAsync_LeavesOutADamagedProfile(int length)
        {
            using MagickImage image = TestImages.Plain(40, 20, 200, 100, 100);

            MadeFiles made = await MadeFiles.MakeAsync(WithProfile(TestImages.Jpeg(image), DisplayP3Profile.Bytes[..length]));

            AssertSrgb(new MagickColor(200, 100, 100), made, tolerance: 6);
        }

        /// <summary>It is as long as it says, which is no header's length.</summary>
        [Fact]
        public async Task MakeAsync_LeavesOutAProfileShorterThanAHeader()
        {
            byte[] profile = DisplayP3Profile.Bytes[..30];
            BinaryPrimitives.WriteInt32BigEndian(profile, profile.Length);
            using MagickImage image = TestImages.Plain(40, 20, 200, 100, 100);

            MadeFiles made = await MadeFiles.MakeAsync(WithProfile(TestImages.Jpeg(image), profile));

            AssertSrgb(new MagickColor(200, 100, 100), made, tolerance: 6);
        }

        [Fact]
        public async Task MakeAsync_LeavesOutWhatIsNoProfile()
        {
            byte[] profile = [.. DisplayP3Profile.Bytes];
            profile.AsSpan(36, 4).Clear();
            using MagickImage image = TestImages.Plain(40, 20, 200, 100, 100);

            MadeFiles made = await MadeFiles.MakeAsync(WithProfile(TestImages.Jpeg(image), profile));

            AssertSrgb(new MagickColor(200, 100, 100), made, tolerance: 6);
        }

        /// <summary>Its header is whole, and nothing in it is.</summary>
        [Fact]
        public async Task MakeAsync_MakesAnAgentsOfOneWhoseProfileSaysNothing()
        {
            byte[] profile = [.. DisplayP3Profile.Bytes];
            profile.AsSpan(128).Clear();
            using MagickImage image = TestImages.Plain(40, 20, 200, 100, 100);

            using MagickImage agent = await MadeFiles.MakeOneAsync(WithProfile(TestImages.Jpeg(image), profile), DerivedKind.Agent);

            ImageAssert.Color(new MagickColor(200, 100, 100), agent);
        }

        private static byte[] DisplayP3Red(string format)
        {
            using MagickImage image = TestImages.Plain(40, 20, 255, 0, 0);
            image.SetProfile(DisplayP3);
            return TestImages.Of(format, image);
        }

        /// <summary><paramref name="jpeg"/> with an ICC profile, whatever it is, in the one segment after its first two bytes.</summary>
        private static byte[] WithProfile(byte[] jpeg, byte[] icc)
        {
            byte[] name = "ICC_PROFILE\0\u0001\u0001"u8.ToArray();
            int length = 2 + name.Length + icc.Length;
            return [.. jpeg[..2], 0xFF, 0xE2, (byte)(length >> 8), (byte)length, .. name, .. icc, .. jpeg[2..]];
        }

        private static byte[] GrayJpeg(byte gray)
        {
            using MagickImage image = TestImages.Plain(40, 20, gray, gray, gray);
            image.ColorSpace = ColorSpace.Gray;
            return TestImages.Jpeg(image);
        }

        /// <summary>Display P3's profile with no curve, for numbers of <paramref name="space"/>.</summary>
        private static byte[] LinearProfile(string space)
        {
            byte[] profile = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(DisplayP3Profile.Bytes)
                .Replace("RGB ", space, StringComparison.Ordinal)
                .Replace("gTRC", space == "GRAY" ? "kTRC" : "gTRC", StringComparison.Ordinal));
            for (int curve = profile.AsSpan().IndexOf("para"u8); curve >= 0; curve = NextCurve(profile, curve))
            {
                // A curve of the fewest parameters, an exponent, of one.
                BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(curve + 8), 0);
                BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(curve + 12), 65536);
            }

            return profile;
        }

        private static int NextCurve(byte[] profile, int curve)
        {
            int next = profile.AsSpan(curve + 4).IndexOf("para"u8);
            return next < 0 ? next : curve + 4 + next;
        }

        /// <summary><paramref name="profile"/> with zeroes after it, which it says are its own.</summary>
        private static byte[] Padded(byte[] profile, int length)
        {
            byte[] padded = [.. profile, .. new byte[length - profile.Length]];
            BinaryPrimitives.WriteInt32BigEndian(padded, length);
            return padded;
        }

        private static void AssertSrgb(IMagickColor<byte> expected, MadeFiles made, int tolerance)
        {
            Assert.Equal(MadeFiles.AllKinds.Order(), made.Kinds);
            Assert.All(MadeFiles.AllKinds, kind =>
            {
                using MagickImage image = made.Decode(kind);
                Assert.Empty(image.ProfileNames);
                Assert.Contains(image.ColorSpace, new[] { ColorSpace.sRGB, ColorSpace.Gray });
                ImageAssert.Color(expected, image, tolerance);
            });
        }

        /// <summary>What a screen that shows Display P3 shows of a corner of <paramref name="image"/>: one without a profile is taken for sRGB.</summary>
        private static void AssertShownInDisplayP3(IMagickColor<byte> expected, IMagickImage<byte> image)
        {
            using IMagickImage<byte> shown = image.Clone();
            if (shown.GetProfile("icc") == null)
            {
                shown.SetProfile(ColorProfiles.SRGB);
            }

            shown.TransformColorSpace(DisplayP3, ColorTransformMode.HighRes);
            ImageAssert.Color(expected, shown);
        }
    }
}
