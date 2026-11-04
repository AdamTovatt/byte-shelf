namespace ByteShelf.Tools.Mcp
{
    /// <summary>
    /// Makes <see cref="ImagePreview"/>s. Only the server's <c>/mcp</c> has one: through
    /// <c>byteshelf --mcp</c> the agent saves the whole image to its own machine with localPath.
    /// </summary>
    internal interface IImagePreviews
    {
        /// <summary>
        /// A preview of the image <paramref name="readContent"/> reads, or why there is none. The
        /// content is read only once this previewer is ready to decode it, so calls waiting their turn
        /// hold no image in memory.
        /// </summary>
        Task<PreviewOutcome> MakeAsync(Func<CancellationToken, Task<byte[]>> readContent, CancellationToken cancellationToken);
    }

    /// <summary>A preview, or why none was made.</summary>
    internal abstract record PreviewOutcome
    {
        private PreviewOutcome()
        {
        }

        internal sealed record Made(ImagePreview Preview) : PreviewOutcome;

        internal sealed record NotMade(NoPreview Why) : PreviewOutcome;
    }

    /// <summary>Why an image got no preview.</summary>
    internal enum NoPreview
    {
        /// <summary>It is not an image the previewer decodes, such as HEIC.</summary>
        NotDecodable,

        /// <summary>It decodes to more pixels than the previewer holds at once.</summary>
        TooManyPixels,

        /// <summary>Its detail leaves even the lowest-quality JPEG over <see cref="ByteShelfMcpTools.MaxInlineImageBytes"/>.</summary>
        TooDetailed,
    }
}
