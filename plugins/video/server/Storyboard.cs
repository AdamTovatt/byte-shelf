namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// The layout of a video's storyboard, its <c>preview</c>: one WebP of <see cref="Count"/> frames of
    /// <see cref="FrameWidth"/> by <see cref="FrameHeight"/> pixels, <see cref="Columns"/> to a row, left to right and
    /// then down. Frame <c>i</c> stands for the time from <c>i × interval</c> to the next, and is of the keyframe at or
    /// before its middle. The one rule of it is <see cref="For"/>, which the maker makes it by and the reader puts in its
    /// document, so the player reads it rather than working it out again.
    /// </summary>
    internal sealed record Storyboard(double IntervalSeconds, int Columns, int Rows, int FrameWidth, int FrameHeight, int Count)
    {
        internal const int Width = 160;

        internal const int MaxColumns = 10;

        /// <summary>The most time a frame stands for, of a video of up to <see cref="MaxCount"/> of it: a longer one has as many frames, each of longer.</summary>
        internal const double MaxIntervalSeconds = 2;

        /// <summary>As many as are found in the time a run has: each is a seek of its own.</summary>
        internal const int MaxCount = 100;

        /// <summary>So a frame of a video far taller than it is wide stays small; 10 rows of it are well within WebP's 16383 pixels.</summary>
        internal const int MaxFrameHeight = 480;

        /// <summary>The storyboard of a video that plays for <paramref name="durationSeconds"/> and is shown <paramref name="width"/> by <paramref name="height"/>.</summary>
        public static Storyboard For(double durationSeconds, int width, int height)
        {
            int count = (int)Math.Clamp(Math.Ceiling(durationSeconds / MaxIntervalSeconds), 1, MaxCount);
            int columns = Math.Min(count, MaxColumns);
            int frameHeight = (int)Math.Clamp(Math.Round((double)Width * height / width), 1, MaxFrameHeight);
            return new Storyboard(durationSeconds / count, columns, (count + columns - 1) / columns, Width, frameHeight, count);
        }

        /// <summary>The time frame <paramref name="frame"/> is taken at: the middle of the time it stands for.</summary>
        public double TimeOf(int frame)
        {
            return (frame + 0.5) * IntervalSeconds;
        }
    }
}
