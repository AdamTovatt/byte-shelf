using ByteShelfCommon;

namespace ByteShelfClient
{
    /// <summary>An upload's path and options as every client checks them, before anything is sent or stored.</summary>
    internal sealed record NormalizedUpload(string Path, string ContentType, string? Description, bool Overwrite, bool Encrypted)
    {
        /// <exception cref="ArgumentException">The path, the content type or the description is not allowed.</exception>
        public static NormalizedUpload Of(string path, UploadOptions? options)
        {
            options ??= new UploadOptions();
            string normalizedPath = ShelfPath.Normalize(path);
            return new NormalizedUpload(
                normalizedPath,
                ContentTypes.ForUpload(options.ContentType, normalizedPath),
                FileDescription.Normalize(options.Description),
                options.Overwrite,
                options.Encrypted);
        }
    }
}
