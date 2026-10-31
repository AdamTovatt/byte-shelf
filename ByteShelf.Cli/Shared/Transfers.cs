using System.Text;
using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Cli.Shared
{
    /// <summary>Moving bytes between local files, streams and a shelf.</summary>
    internal static class Transfers
    {
        private const int CopyBufferBytes = 81920;

        /// <summary>
        /// Stores a local file at <paramref name="location"/>, under its own name when that names a
        /// directory, typed by its extension unless <paramref name="contentType"/> says otherwise.
        /// </summary>
        /// <exception cref="UsageException">There is no such local file.</exception>
        public static async Task<ShelfFile> UploadFileAsync(IShelfClient shelf, string localPath, ShelfLocation location, bool overwrite, string? contentType, CancellationToken cancellationToken)
        {
            if (!File.Exists(localPath))
            {
                throw new UsageException($"There is no file {Path.GetFullPath(localPath)}.");
            }

            await using FileStream content = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, useAsync: true);
            return await UploadAsync(shelf, location.FilePathFor(Path.GetFileName(localPath)), content, overwrite, contentType, cancellationToken);
        }

        /// <summary>Stores <paramref name="content"/>, which has no name of its own, at <paramref name="location"/>.</summary>
        /// <param name="source">What the content is, to start an error with, such as "Standard input".</param>
        /// <exception cref="UsageException"><paramref name="location"/> names a directory.</exception>
        public static Task<ShelfFile> UploadStreamAsync(IShelfClient shelf, ShelfLocation location, Stream content, string source, bool overwrite, string? contentType, CancellationToken cancellationToken)
        {
            if (location.NamesADirectory)
            {
                throw new UsageException($"{source} has no file name; end the path with one.");
            }

            return UploadAsync(shelf, location.Path, content, overwrite, contentType, cancellationToken);
        }

        /// <summary>
        /// Saves a file from the shelf to <paramref name="localPath"/>, or into it when it is a
        /// directory, and answers where. The bytes go to a side file first, so a failed download
        /// leaves nothing behind and replaces nothing.
        /// </summary>
        /// <exception cref="UsageException">The local file exists and <paramref name="overwrite"/> is false.</exception>
        public static async Task<string> DownloadToFileAsync(IShelfClient shelf, string path, string localPath, bool overwrite, CancellationToken cancellationToken)
        {
            string target = Path.GetFullPath(localPath);
            if (Directory.Exists(target))
            {
                target = Path.Combine(target, ShelfPath.GetName(path));
            }

            if (!overwrite && File.Exists(target))
            {
                throw new UsageException($"{target} exists already; ask to overwrite it.");
            }

            string partial = target + ".byteshelf-partial";
            try
            {
                await using (Stream content = await shelf.OpenReadAsync(path, cancellationToken))
                await using (FileStream file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferBytes, useAsync: true))
                {
                    await content.CopyToAsync(file, cancellationToken);
                }

                File.Move(partial, target, overwrite);
                return target;
            }
            catch
            {
                File.Delete(partial);
                throw;
            }
        }

        /// <summary>A file's content as text, for an answer that carries it inline.</summary>
        /// <exception cref="UsageException">It is larger than <paramref name="maxBytes"/> or not UTF-8 text.</exception>
        public static async Task<string> ReadTextAsync(IShelfClient shelf, string path, int maxBytes, CancellationToken cancellationToken)
        {
            ShelfFile file = await shelf.GetFileAsync(path, cancellationToken);
            if (file.SizeBytes > maxBytes)
            {
                throw new UsageException($"{path} is {file.SizeBytes} bytes, more than the {maxBytes} answered inline; save it to a local file instead.");
            }

            using MemoryStream buffer = new MemoryStream();
            await using (Stream content = await shelf.OpenReadAsync(file.Id, cancellationToken))
            {
                await content.CopyToAsync(buffer, cancellationToken);
            }

            try
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
            catch (DecoderFallbackException exception)
            {
                throw new UsageException($"{path} is not UTF-8 text; save it to a local file instead.", exception);
            }
        }

        private static Task<ShelfFile> UploadAsync(IShelfClient shelf, string path, Stream content, bool overwrite, string? contentType, CancellationToken cancellationToken)
        {
            UploadOptions options = new UploadOptions { Overwrite = overwrite, ContentType = contentType ?? ContentTypes.ForFileName(path) };
            return shelf.UploadAsync(path, content, options, cancellationToken);
        }
    }
}
