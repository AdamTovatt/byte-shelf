namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class StoryboardTests
    {
        [Fact]
        public void For_AShortVideo_TakesAFrameEvery2Seconds()
        {
            Storyboard storyboard = Storyboard.For(10, 1920, 1080);

            Assert.Equal(new Storyboard(IntervalSeconds: 2, Columns: 5, Rows: 1, FrameWidth: 160, FrameHeight: 90, Count: 5), storyboard);
        }

        [Fact]
        public void For_ALongVideo_TakesAtMost100FramesSpreadOverIt()
        {
            Storyboard storyboard = Storyboard.For(540, 1920, 1080);

            Assert.Equal(new Storyboard(IntervalSeconds: 5.4, Columns: 10, Rows: 10, FrameWidth: 160, FrameHeight: 90, Count: 100), storyboard);
        }

        [Fact]
        public void For_AnyLength_HasRowsEnoughForEveryFrame()
        {
            Storyboard storyboard = Storyboard.For(25, 1920, 1080);

            Assert.Equal((13, 10, 2), (storyboard.Count, storyboard.Columns, storyboard.Rows));
        }

        [Fact]
        public void For_LessThanAnInterval_TakesOneFrame()
        {
            Storyboard storyboard = Storyboard.For(0.5, 640, 480);

            Assert.Equal((1, 1, 1, 0.5), (storyboard.Count, storyboard.Columns, storyboard.Rows, storyboard.IntervalSeconds));
        }

        /// <summary>160 wide, as every frame is, so 160 × 1920 / 1080 high, rounded.</summary>
        [Fact]
        public void For_AnUprightVideo_HasFramesOfItsShape()
        {
            Assert.Equal(284, Storyboard.For(10, 1080, 1920).FrameHeight);
        }

        [Fact]
        public void For_AVeryTallVideo_HasFramesOfAtMost480Pixels()
        {
            Assert.Equal(480, Storyboard.For(10, 100, 1000).FrameHeight);
        }

        [Fact]
        public void For_AVeryWideVideo_HasFramesOfAPixelAtLeast()
        {
            Assert.Equal(1, Storyboard.For(10, 10000, 10).FrameHeight);
        }

        [Fact]
        public void TimeOf_IsTheMiddleOfTheTimeAFrameStandsFor()
        {
            Storyboard storyboard = Storyboard.For(10, 1920, 1080);

            Assert.Equal([1, 3, 9], [storyboard.TimeOf(0), storyboard.TimeOf(1), storyboard.TimeOf(4)]);
        }
    }
}
