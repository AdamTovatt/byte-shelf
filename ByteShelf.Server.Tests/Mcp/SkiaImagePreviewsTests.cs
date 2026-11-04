using ByteShelf.Server.Mcp;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Tools.Mcp;
using SkiaSharp;

namespace ByteShelf.Server.Tests.Mcp
{
    /// <summary>Previews of real images, decoded and drawn by SkiaSharp's native library on this machine.</summary>
    public sealed class SkiaImagePreviewsTests
    {
        private readonly SkiaImagePreviews _previews = new SkiaImagePreviews();

        [Fact]
        public async Task MakeAsync_ALargeImage_ScalesItToTheLongSideWithinTheCap()
        {
            ImagePreview preview = await PreviewOfAsync(TestImages.PhotoLikePng(2400, 1600));

            int height = ShortSideAtLongSide(1600, 2400);
            Assert.Equal((ImagePreview.LongSide, height, 2400, 1600, "image/png"), (preview.Width, preview.Height, preview.OriginalWidth, preview.OriginalHeight, preview.OriginalType));
            Assert.Equal((ImagePreview.LongSide, height), TestImages.SizeOf(preview.Jpeg));
            Assert.InRange(preview.Jpeg.Length, 1, ByteShelfMcpTools.MaxInlineImageBytes);
        }

        [Fact]
        public async Task MakeAsync_AnImageOfFineDetail_LowersTheQualityToFitTheCap()
        {
            Assert.True(TestImages.NoiseJpegSize(ImagePreview.LongSide, ImagePreview.LongSide, quality: 85) > ByteShelfMcpTools.MaxInlineImageBytes);

            ImagePreview preview = await PreviewOfAsync(TestImages.NoisePng(2000, 2000));

            Assert.Equal((ImagePreview.LongSide, ImagePreview.LongSide), TestImages.SizeOf(preview.Jpeg));
            Assert.InRange(preview.Jpeg.Length, 1, ByteShelfMcpTools.MaxInlineImageBytes);
        }

        [Fact]
        public async Task MakeAsync_APhotoTakenTurned_DrawsItUpright()
        {
            ImagePreview preview = await PreviewOfAsync(TestImages.JpegTurnedAQuarter(2000, 1000));

            int width = ShortSideAtLongSide(1000, 2000);
            Assert.Equal((width, ImagePreview.LongSide, 1000, 2000), (preview.Width, preview.Height, preview.OriginalWidth, preview.OriginalHeight));
            Assert.Equal((width, ImagePreview.LongSide), TestImages.SizeOf(preview.Jpeg));
            // The stored top-left quarter, white, is the top-right one once turned a quarter clockwise.
            Assert.True(TestImages.IsNearWhite(TestImages.PixelAt(preview.Jpeg, 0.75, 0.25)));
            Assert.False(TestImages.IsNearWhite(TestImages.PixelAt(preview.Jpeg, 0.25, 0.25)));
        }

        /// <summary>
        /// Two stored points, the top-left corner and the one beside it along the top edge, in an image
        /// upright 30 by 20: the corner alone lands in the same place for an orientation and its mirror
        /// turned (TopLeft and LeftTop), the second point does not.
        /// </summary>
        [Theory]
        [InlineData(SKEncodedOrigin.TopLeft, 0, 0, 1, 0)]
        [InlineData(SKEncodedOrigin.TopRight, 30, 0, 29, 0)]
        [InlineData(SKEncodedOrigin.BottomRight, 30, 20, 29, 20)]
        [InlineData(SKEncodedOrigin.BottomLeft, 0, 20, 1, 20)]
        [InlineData(SKEncodedOrigin.LeftTop, 0, 0, 0, 1)]
        [InlineData(SKEncodedOrigin.RightTop, 30, 0, 30, 1)]
        [InlineData(SKEncodedOrigin.RightBottom, 30, 20, 30, 19)]
        [InlineData(SKEncodedOrigin.LeftBottom, 0, 20, 0, 19)]
        public void ToUpright_PutsTheStoredTopEdgeWhereEachOrientationSays(SKEncodedOrigin origin, float cornerX, float cornerY, float nextX, float nextY)
        {
            SKMatrix upright = SkiaImagePreviews.ToUpright(origin, 30, 20);

            Assert.Equal((new SKPoint(cornerX, cornerY), new SKPoint(nextX, nextY)), (upright.MapPoint(0, 0), upright.MapPoint(1, 0)));
        }

        [Fact]
        public async Task MakeAsync_AnImageWithTransparency_DrawsItOnWhite()
        {
            ImagePreview preview = await PreviewOfAsync(TestImages.PhotoLikePngTransparentOnTheRight(2400, 1600));

            Assert.True(TestImages.IsNearWhite(TestImages.PixelAt(preview.Jpeg, 0.9, 0.5)));
            Assert.False(TestImages.IsNearWhite(TestImages.PixelAt(preview.Jpeg, 0.1, 0.5)));
        }

        [Fact]
        public async Task MakeAsync_AnImageOfAKindModelsCannotTake_PreviewsItAtItsOwnSize()
        {
            ImagePreview preview = await PreviewOfAsync(TestImages.Bmp(300, 200));

            Assert.Equal((300, 200, "image/bmp"), (preview.Width, preview.Height, preview.OriginalType));
        }

        [Fact]
        public async Task MakeAsync_AnImageSkiaCannotDecode_SaysSo()
        {
            PreviewOutcome outcome = await _previews.MakeAsync(_ => Task.FromResult(TestImages.HeicHeaderOnly(4096)), CancellationToken.None);

            Assert.Equal(new PreviewOutcome.NotMade(NoPreview.NotDecodable), outcome);
        }

        [Fact]
        public async Task MakeAsync_AnImageDecodingPastTheMemoryBudget_SaysSoWithoutDecodingIt()
        {
            Assert.True(9000L * 9000 * 4 > SkiaImagePreviews.MaxDecodedBytes);

            PreviewOutcome outcome = await _previews.MakeAsync(_ => Task.FromResult(TestImages.BlankRgbaPng(9000, 9000)), CancellationToken.None);

            Assert.Equal(new PreviewOutcome.NotMade(NoPreview.TooManyPixels), outcome);
        }

        [Fact]
        public async Task MakeAsync_WhileAnotherIsBeingMade_ReadsNothingUntilItsTurn()
        {
            TaskCompletionSource<byte[]> firstContent = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool secondRead = false;

            Task<PreviewOutcome> first = _previews.MakeAsync(_ => firstContent.Task, CancellationToken.None);
            Task<PreviewOutcome> second = _previews.MakeAsync(
                _ =>
                {
                    secondRead = true;
                    return Task.FromResult(TestImages.Bmp(30, 20));
                },
                CancellationToken.None);

            Assert.False(secondRead);
            firstContent.SetResult(TestImages.Bmp(30, 20));
            await Task.WhenAll(first, second);
            Assert.True(secondRead);
        }

        /// <summary>The short side of an image scaled until its long side is <see cref="ImagePreview.LongSide"/>.</summary>
        private static int ShortSideAtLongSide(int shortSide, int longSide)
        {
            return (int)Math.Round((double)shortSide * ImagePreview.LongSide / longSide);
        }

        private async Task<ImagePreview> PreviewOfAsync(byte[] image)
        {
            PreviewOutcome outcome = await _previews.MakeAsync(_ => Task.FromResult(image), CancellationToken.None);
            return Assert.IsType<PreviewOutcome.Made>(outcome).Preview;
        }
    }
}
