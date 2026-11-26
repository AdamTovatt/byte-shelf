using System.Text;
using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    /// <summary>A camera's raw (<see cref="DngFile"/>), whose kinds are made of the JPEG it carries when that is large enough, and else of it, decoded.</summary>
    public sealed class RawTests
    {
        /// <summary>Those made of a raw: all but full.</summary>
        private static readonly DerivedKind[] Kinds = [DerivedKind.Thumbnail, DerivedKind.Preview, DerivedKind.Agent];

        public static TheoryData<ushort> Orientations { get; } = [.. TestImages.Orientations];

        /// <summary>The turns a camera records. Of a raw stored mirrored, which none is, ImageMagick says another orientation.</summary>
        public static TheoryData<ushort> CameraOrientations { get; } = [1, 3, 6, 8];

        /// <summary>One of an agent's long side is as large as every kind but a preview at its largest.</summary>
        [Theory]
        [InlineData(1568)]
        [InlineData(3000)]
        public async Task MakeAsync_MakesThemOfTheCamerasJpeg_OfAnAgentsLongSideOrMore(int width)
        {
            MadeFiles made = await MadeFiles.MakeAsync(DngFile.Make(jpeg: DngFile.Jpeg(width, width / 2)), Kinds);

            ImageAssert.MadeOf(made, Kinds, width, width / 2, TestImages.QuarterColors);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(400)]
        [InlineData(1567)]
        public async Task MakeAsync_DecodesTheRaw_WhenTheCamerasJpegIsSmallerOrNone(int? width)
        {
            byte[] contents = DngFile.Make(jpeg: width is { } jpegWidth ? DngFile.Jpeg(jpegWidth, jpegWidth / 2) : null);

            MadeFiles made = await MadeFiles.MakeAsync(contents, Kinds);

            ImageAssert.MadeOf(made, Kinds, DngFile.Width, DngFile.Height, TestImages.QuarterColors);
        }

        /// <summary>LibRaw hands over a bitmap's pixels, in no format.</summary>
        [Fact]
        public async Task MakeAsync_DecodesTheRaw_WhenItsCameraMadeABitmap()
        {
            MadeFiles made = await MadeFiles.MakeAsync(DngFile.Make(hasBitmap: true), Kinds);

            ImageAssert.MadeOf(made, Kinds, DngFile.Width, DngFile.Height, TestImages.QuarterColors);
        }

        /// <summary>LibRaw gives the camera's JPEG the raw's orientation.</summary>
        [Theory]
        [MemberData(nameof(Orientations))]
        public async Task MakeAsync_TurnsTheCamerasJpegUpright(ushort orientation)
        {
            MadeFiles made = await MadeFiles.MakeAsync(DngFile.Make(orientation, DngFile.Jpeg(1600, 800)), Kinds);

            (int width, int height) = orientation < 5 ? (1600, 800) : (800, 1600);
            ImageAssert.MadeOf(made, Kinds, width, height, TestImages.UprightQuarterColors(orientation));
        }

        /// <summary>Its own describes its pixels, and LibRaw leaves it.</summary>
        [Fact]
        public async Task MakeAsync_TurnsTheCamerasJpegUpright_ByItsOwnExif()
        {
            using MagickImage stored = TestImages.QuartersStored(1600, 800, 6);

            MadeFiles made = await MadeFiles.MakeAsync(DngFile.Make(orientation: 1, jpeg: TestImages.Jpeg(stored)), Kinds);

            ImageAssert.MadeOf(made, Kinds, 800, 1600, TestImages.UprightQuarterColors(6));
        }

        [Theory]
        [MemberData(nameof(CameraOrientations))]
        public async Task MakeAsync_TurnsADecodedRawUpright(ushort orientation)
        {
            MadeFiles made = await MadeFiles.MakeAsync(DngFile.Make(orientation), Kinds);

            (int width, int height) = orientation < 5 ? (DngFile.Width, DngFile.Height) : (DngFile.Height, DngFile.Width);
            ImageAssert.MadeOf(made, Kinds, width, height, TestImages.UprightQuarterColors(orientation));
        }

        /// <summary>Of one stored as a type browsers do not show, as a TIFF, which full is asked for.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MakeAsync_MakesNoFullOfARaw(bool hasJpeg)
        {
            byte[] contents = DngFile.Make(jpeg: hasJpeg ? DngFile.Jpeg(1600, 800) : null);

            MadeFiles made = await MadeFiles.MakeAsync(contents, DerivedKind.Full, DerivedKind.Agent);

            Assert.Equal([DerivedKind.Agent], made.Kinds);
        }

        /// <summary>LibRaw gives the camera's JPEG, as EXIF, what the raw says of the camera, and of where it was taken.</summary>
        [Fact]
        public async Task MakeAsync_LeavesOutWhatTheRawSays()
        {
            byte[] contents = DngFile.Make(jpeg: DngFile.Jpeg(1600, 800), camera: "where it was taken");

            MadeFiles made = await MadeFiles.MakeAsync(contents, Kinds);

            Assert.Equal(Kinds, made.Kinds);
            Assert.All(Kinds, kind => Assert.DoesNotContain("where", Encoding.Latin1.GetString(made.BytesOf(kind)), StringComparison.Ordinal));
        }

        /// <summary>As a scanner's, which LibRaw would read as a raw for naming a camera, and brighten.</summary>
        [Fact]
        public async Task MakeAsync_MakesATiffOfGraysThatNamesACamera_AsATiff()
        {
            using MagickImage preview = await MadeFiles.MakeOneAsync(GrayTiff("ByteShelf"), DerivedKind.Preview);

            ImageAssert.Size(preview, 64, 32);
            ImageAssert.Color(new MagickColor(128, 128, 128), preview, tolerance: 2);
        }

        /// <summary>The camera's JPEG is before its pixels, as a camera lays them out, and whole.</summary>
        [Fact]
        public async Task MakeAsync_MakesThemOfTheCamerasJpeg_OfARawCutShortAfterIt()
        {
            byte[] contents = DngFile.Make(jpeg: DngFile.Jpeg(1600, 800));

            MadeFiles made = await MadeFiles.MakeAsync(contents[..^100], Kinds);

            ImageAssert.MadeOf(made, Kinds, 1600, 800, TestImages.QuarterColors);
        }

        /// <summary>LibRaw hands over what there is of a DNG's, which is made into what there is of it, as of any JPEG cut short.</summary>
        [Fact]
        public async Task MakeAsync_MakesThemOfWhatThereIsOfTheCamerasJpeg_OfARawCutShortInIt()
        {
            byte[] contents = DngFile.Make(jpeg: DngFile.Jpeg(1600, 800));

            MadeFiles made = await MadeFiles.MakeAsync(contents[..4000], Kinds);

            Assert.Equal(Kinds, made.Kinds);
            using MagickImage preview = made.Decode(DerivedKind.Preview);
            ImageAssert.Size(preview, 1600, 800);
        }

        /// <summary>LibRaw finds a DNG short, of which there is only a start, or not all of its pixels.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MakeAsync_FailsOnARawCutShort_WithNoCamerasJpeg(bool isOnlyItsStart)
        {
            byte[] contents = DngFile.Make();
            byte[] cut = isOnlyItsStart ? contents[..24] : contents[..(contents.Length / 2)];

            await Assert.ThrowsAnyAsync<MagickException>(() => MadeFiles.MakeAsync(cut, Kinds));
        }

        /// <summary>A TIFF of 16-bit grays half white, which names <paramref name="camera"/>.</summary>
        private static byte[] GrayTiff(string camera)
        {
            const int Width = 64;
            const int Height = 32;
            byte[] pixels = new byte[Width * Height * 2];
            for (int index = 0; index < pixels.Length; index += 2)
            {
                BitConverter.TryWriteBytes(pixels.AsSpan(index), (ushort)(128 * 257));
            }

            // Not compressed (1), of grays from black (1).
            return TiffFile.Write(new TiffFile.ImageDirectory(
                [
                    TiffFile.Entry.Of(TiffFile.ImageWidth, TiffFile.Long, Width),
                    TiffFile.Entry.Of(TiffFile.ImageLength, TiffFile.Long, Height),
                    TiffFile.Entry.Of(TiffFile.BitsPerSample, TiffFile.Short, 16),
                    TiffFile.Entry.Of(TiffFile.Compression, TiffFile.Short, 1),
                    TiffFile.Entry.Of(TiffFile.PhotometricInterpretation, TiffFile.Short, 1),
                    TiffFile.Entry.Of(TiffFile.Make, camera),
                    TiffFile.Entry.StripsAt(),
                    TiffFile.Entry.Of(TiffFile.SamplesPerPixel, TiffFile.Short, 1),
                    TiffFile.Entry.Of(TiffFile.RowsPerStrip, TiffFile.Long, Height),
                    TiffFile.Entry.StripLengths(),
                ],
                pixels,
                []));
        }
    }
}
