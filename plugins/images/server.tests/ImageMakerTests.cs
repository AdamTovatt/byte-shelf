using System.Text;
using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    public sealed class ImageMakerTests
    {
        /// <summary>The formats that have EXIF's orientation, by its values.</summary>
        public static TheoryData<string, ushort> Orientations { get; } = new MatrixTheoryData<string, ushort>(TestImages.FormatsWithOrientation, TestImages.Orientations);

        public static TheoryData<DerivedKind, string> TransparentInWebP { get; } =
            new MatrixTheoryData<DerivedKind, string>([DerivedKind.Thumbnail, DerivedKind.Preview], ["png", "gif", "webp", "x-icon"]);

        public static TheoryData<DerivedKind, string> TransparentInJpeg { get; } =
            new MatrixTheoryData<DerivedKind, string>([DerivedKind.Full, DerivedKind.Agent], ["png", "gif", "webp", "tiff", "x-icon"]);

        [Theory]
        [MemberData(nameof(TestImages.Formats), MemberType = typeof(TestImages))]
        public async Task MakeAsync_MakesEveryKindOfEveryFormat(string format)
        {
            using MagickImage image = TestImages.Quarters(80, 40);

            MadeFiles made = await MadeFiles.MakeAsync(TestImages.Of(format, image));

            ImageAssert.AllOf(made, 80, 40, TestImages.QuarterColors);
        }

        [Fact]
        public async Task MakeAsync_MakesEveryKindOfASixteenBitPng()
        {
            using MagickImage image = TestImages.Quarters(80, 40);
            image.Depth = 16;
            image.Settings.SetDefine("png:bit-depth", "16");
            image.Settings.SetDefine("png:color-type", "2");
            byte[] png = image.ToByteArray(MagickFormat.Png);

            // The bit depth in its header.
            Assert.Equal(16, png[24]);

            MadeFiles made = await MadeFiles.MakeAsync(png);

            ImageAssert.AllOf(made, 80, 40, TestImages.QuarterColors);
        }

        [Fact]
        public async Task MakeAsync_MakesEveryKindOfAHeic()
        {
            MadeFiles made = await MadeFiles.MakeAsync(HeicFile.Make());

            ImageAssert.AllOf(made, 64, 48, [MagickColors.Red, MagickColors.Blue, MagickColors.Red, MagickColors.Blue]);
        }

        [Theory]
        [InlineData(DerivedKind.Thumbnail, "jpeg", 320, 160)]
        [InlineData(DerivedKind.Thumbnail, "bmp", 320, 160)]
        [InlineData(DerivedKind.Preview, "jpeg", 2048, 1024)]
        [InlineData(DerivedKind.Preview, "bmp", 2048, 1024)]
        [InlineData(DerivedKind.Agent, "jpeg", 1568, 784)]
        [InlineData(DerivedKind.Agent, "bmp", 1568, 784)]
        [InlineData(DerivedKind.Full, "jpeg", 3000, 1500)]
        [InlineData(DerivedKind.Full, "bmp", 3000, 1500)]
        public async Task MakeAsync_MakesItOfTheKindsLongSide(DerivedKind kind, string format, int width, int height)
        {
            using MagickImage image = TestImages.Quarters(3000, 1500);

            MadeFiles made = await MadeFiles.MakeAsync(TestImages.Of(format, image), kind);

            using MagickImage decoded = made.Decode(kind);
            ImageAssert.Size(decoded, width, height);
        }

        [Fact]
        public async Task MakeAsync_MakesFullOfTheLongestSideAJpegHas()
        {
            using MagickImage image = TestImages.Plain(70_000, 10, 255, 0, 0);

            MadeFiles made = await MadeFiles.MakeAsync(image.ToByteArray(MagickFormat.Bmp), DerivedKind.Full);

            using MagickImage decoded = made.Decode(DerivedKind.Full);
            Assert.Equal(65_500u, decoded.Width);
        }

        /// <summary>A JPEG is decoded smaller, and never smaller than what is made of it.</summary>
        [Theory]
        [InlineData(2049)]
        [InlineData(2300)]
        [InlineData(4000)]
        [InlineData(4100)]
        [InlineData(9000)]
        public async Task MakeAsync_MakesAJpegOfTheLongSide_WhateverItsSize(int width)
        {
            using MagickImage image = TestImages.Plain(width, 8, 255, 0, 0);

            MadeFiles made = await MadeFiles.MakeAsync(TestImages.Jpeg(image), DerivedKind.Thumbnail, DerivedKind.Preview);

            using MagickImage thumbnail = made.Decode(DerivedKind.Thumbnail);
            using MagickImage preview = made.Decode(DerivedKind.Preview);
            Assert.Equal((320u, 2048u), (thumbnail.Width, preview.Width));
        }

        [Fact]
        public async Task MakeAsync_MakesNoneLarger()
        {
            using MagickImage image = TestImages.Plain(20, 10, 255, 0, 0);

            MadeFiles made = await MadeFiles.MakeAsync(image.ToByteArray(MagickFormat.Png));

            Assert.All(MadeFiles.AllKinds, kind =>
            {
                using MagickImage decoded = made.Decode(kind);
                ImageAssert.Size(decoded, 20, 10);
            });
        }

        [Fact]
        public async Task MakeAsync_MakesAStripOneInHeight()
        {
            using MagickImage image = TestImages.Plain(4000, 2, 255, 0, 0);

            using MagickImage made = await MadeFiles.MakeOneAsync(image.ToByteArray(MagickFormat.Png), DerivedKind.Thumbnail);

            ImageAssert.Size(made, 320, 1);
        }

        [Theory]
        [MemberData(nameof(Orientations))]
        public async Task MakeAsync_TurnsItUpright(string format, ushort orientation)
        {
            using MagickImage image = TestImages.QuartersStored(400, 200, orientation);

            MadeFiles made = await MadeFiles.MakeAsync(TestImages.Of(format, image));

            (int width, int height) = orientation < 5 ? (400, 200) : (200, 400);
            ImageAssert.AllOf(made, width, height, TestImages.UprightQuarterColors(orientation));
        }

        [Theory]
        [MemberData(nameof(TransparentInWebP))]
        public async Task MakeAsync_KeepsWhatIsTransparent_InWebP(DerivedKind kind, string format)
        {
            using MagickImage image = TestImages.HalfTransparent(40, 20);

            using MagickImage made = await MadeFiles.MakeOneAsync(TestImages.Of(format, image), kind);

            Assert.Equal((0, 255), (ImageAssert.ColorAt(made, 5, 5).A, ImageAssert.ColorAt(made, 35, 5).A));
        }

        [Theory]
        [MemberData(nameof(TransparentInJpeg))]
        public async Task MakeAsync_PutsWhatIsTransparentOnWhite_InJpeg(DerivedKind kind, string format)
        {
            using MagickImage image = TestImages.HalfTransparent(40, 20);

            using MagickImage made = await MadeFiles.MakeOneAsync(TestImages.Of(format, image), kind);

            ImageAssert.Color(MagickColors.White, ImageAssert.ColorAt(made, 5, 5));
            ImageAssert.Color(MagickColors.Red, ImageAssert.ColorAt(made, 35, 5));
        }

        [Fact]
        public async Task MakeAsync_MakesAnAgentsNoLargerThanIsKept_OfNoise()
        {
            using MagickImage image = TestImages.Noise(1600, 1600);

            MadeFiles made = await MadeFiles.MakeAsync(image.ToByteArray(MagickFormat.Bmp), DerivedKind.Agent);

            Assert.InRange(made.BytesOf(DerivedKind.Agent).Length, 1, 1024 * 1024);
            using MagickImage agent = made.Decode(DerivedKind.Agent);
            Assert.InRange(agent.Quality, 1u, 84u);
        }

        [Fact]
        public async Task MakeAsync_MakesAnAgentsAsGoodAsItMay()
        {
            using MagickImage image = TestImages.Quarters(400, 200);

            using MagickImage agent = await MadeFiles.MakeOneAsync(image.ToByteArray(MagickFormat.Bmp), DerivedKind.Agent);

            Assert.Equal(85u, agent.Quality);
        }

        [Fact]
        public async Task MakeAsync_LeavesOutWhatCameWithTheImage()
        {
            using MagickImage image = TestImages.Plain(40, 20, 255, 0, 0);
            image.SetProfile(new ImageProfile("exif", TestImages.ExifWithGps("where on earth")));
            image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'>where</x:xmpmeta>")));
            image.SetAttribute("comment", "where it was taken");
            byte[] contents = TestImages.Jpeg(image);
            Assert.Contains("where it was taken", Encoding.Latin1.GetString(contents), StringComparison.Ordinal);
            Assert.Contains("where on earth", Encoding.Latin1.GetString(contents), StringComparison.Ordinal);

            MadeFiles made = await MadeFiles.MakeAsync(contents);

            Assert.All(MadeFiles.AllKinds, kind =>
            {
                Assert.DoesNotContain("where", Encoding.Latin1.GetString(made.BytesOf(kind)), StringComparison.Ordinal);
                using MagickImage decoded = made.Decode(kind);
                Assert.Empty(decoded.ProfileNames);
            });
        }

        [Theory]
        [InlineData(MagickFormat.Gif)]
        [InlineData(MagickFormat.WebP)]
        [InlineData(MagickFormat.Tiff)]
        public async Task MakeAsync_MakesTheFirstFrameOrPage(MagickFormat format)
        {
            using MagickImageCollection frames = [TestImages.Plain(40, 20, 255, 0, 0), TestImages.Plain(40, 20, 0, 0, 255)];
            if (format == MagickFormat.Tiff)
            {
                // A page may be larger than the first.
                frames.Add(TestImages.Plain(400, 200, 0, 255, 0));
            }

            foreach (IMagickImage<byte> frame in frames)
            {
                frame.AnimationDelay = 10;
            }

            using MagickImage made = await MadeFiles.MakeOneAsync(frames.ToByteArray(format), DerivedKind.Preview);

            ImageAssert.Color(MagickColors.Red, made);
        }

        [Fact]
        public async Task MakeAsync_PutsAFirstFrameWhereItIsShown()
        {
            using MagickImage frame = TestImages.Plain(10, 10, 255, 0, 0);
            frame.Page = new MagickGeometry(30, 10, 40, 20);

            MadeFiles made = await MadeFiles.MakeAsync(frame.ToByteArray(MagickFormat.Gif), DerivedKind.Preview);

            using MagickImage preview = made.Decode(DerivedKind.Preview);
            ImageAssert.Size(preview, 40, 20);
            Assert.Equal(0, ImageAssert.ColorAt(preview, 5, 5).A);
            ImageAssert.Color(MagickColors.Red, ImageAssert.ColorAt(preview, 35, 15));
        }

        [Fact]
        public async Task MakeAsync_MakesTheLargestOfAnIcon()
        {
            using MagickImage small = TestImages.Plain(16, 16, 255, 0, 0);
            using MagickImage large = TestImages.Plain(64, 64, 0, 0, 255);
            using MagickImage middle = TestImages.Plain(32, 32, 0, 255, 0);
            byte[] icon = TestImages.Ico([.. new[] { small, large, middle }.Select(image => image.ToByteArray(MagickFormat.Png))]);

            using MagickImage made = await MadeFiles.MakeOneAsync(icon, DerivedKind.Preview);

            ImageAssert.Size(made, 64, 64);
            ImageAssert.Color(MagickColors.Blue, ImageAssert.ColorAt(made, 32, 32));
        }

        [Fact]
        public async Task MakeAsync_MakesABitmapIcon()
        {
            using MagickImage image = TestImages.Quarters(48, 48);
            byte[] icon = image.ToByteArray(MagickFormat.Ico);
            Assert.DoesNotContain("PNG", Encoding.Latin1.GetString(icon), StringComparison.Ordinal);

            using MagickImage made = await MadeFiles.MakeOneAsync(icon, DerivedKind.Preview);

            ImageAssert.Size(made, 48, 48);
        }
    }
}
