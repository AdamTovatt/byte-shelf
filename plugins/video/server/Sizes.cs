namespace ByteShelf.Plugins.Video
{
    internal static class Sizes
    {
        /// <summary>
        /// <paramref name="width"/> by <paramref name="height"/>, made smaller to fit within <paramref name="maxWidth"/> by
        /// <paramref name="maxHeight"/> and never larger, in even pixels when <paramref name="even"/>, as H.264's 4:2:0 needs.
        /// </summary>
        public static (int Width, int Height) Fit(int width, int height, int maxWidth, int maxHeight, bool even)
        {
            double scale = Math.Min(1, Math.Min((double)maxWidth / width, (double)maxHeight / height));
            int step = even ? 2 : 1;
            return (Round(width * scale, step), Round(height * scale, step));
        }

        private static int Round(double size, int step)
        {
            return Math.Max(step, (int)Math.Round(size / step) * step);
        }
    }
}
