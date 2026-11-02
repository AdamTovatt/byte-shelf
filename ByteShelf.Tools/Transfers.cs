using ByteShelfClient;
using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>Moving bytes between local files, streams and a shelf.</summary>
    internal static class Transfers
    {
        private const int CopyBufferBytes = 81920;

        /// <summary>
        /// Stores a local file at <paramref name="location"/>, under its own name when that names a
        /// directory, typed by its extension unless <paramref name="options"/> give a content type.
        /// </summary>
        /// <exception cref="UsageException">There is no such local file.</exception>
        public static Task<ShelfFile> UploadFileAsync(IShelfClient shelf, string localPath, ShelfLocation location, UploadOptions options, CancellationToken cancellationToken, Encryptor? encryptor = null)
        {
            return UploadFileAsync(shelf, localPath, location.FilePathFor(Path.GetFileName(localPath)), options, cancellationToken, encryptor);
        }

        /// <summary>
        /// Stores a local file as the file at <paramref name="path"/>, typed by its extension unless
        /// <paramref name="options"/> give a content type, encrypted by <paramref name="encryptor"/> when one is given.
        /// </summary>
        /// <exception cref="UsageException">There is no such local file.</exception>
        public static async Task<ShelfFile> UploadFileAsync(IShelfClient shelf, string localPath, string path, UploadOptions options, CancellationToken cancellationToken, Encryptor? encryptor = null)
        {
            if (!File.Exists(localPath))
            {
                throw new UsageException($"There is no file {Path.GetFullPath(localPath)}.");
            }

            await using FileStream content = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, useAsync: true);
            return await UploadAsync(shelf, path, content, options, cancellationToken, encryptor);
        }

        /// <summary>Stores <paramref name="content"/>, which has no name of its own, at <paramref name="location"/>, encrypted by <paramref name="encryptor"/> when one is given.</summary>
        /// <param name="source">What the content is, to start an error with, such as "Standard input".</param>
        /// <exception cref="UsageException"><paramref name="location"/> names a directory.</exception>
        public static Task<ShelfFile> UploadStreamAsync(IShelfClient shelf, ShelfLocation location, Stream content, string source, UploadOptions options, CancellationToken cancellationToken, Encryptor? encryptor = null)
        {
            if (location.NamesADirectory)
            {
                throw new UsageException($"{source} has no file name; end the path with one.");
            }

            return UploadAsync(shelf, location.Path, content, options, cancellationToken, encryptor);
        }

        /// <summary>Stores <paramref name="content"/> as it is, or as an age file of it made by <paramref name="encryptor"/>.</summary>
        /// <remarks>
        /// Encrypting is the tools' (Encryptor holds the password and work factor they chose), so these
        /// steps live here rather than in the client library, which stores bytes as it is given them.
        /// </remarks>
        public static async Task<ShelfFile> UploadAsync(IShelfClient shelf, string path, Stream content, UploadOptions options, CancellationToken cancellationToken, Encryptor? encryptor = null)
        {
            if (encryptor == null)
            {
                return await shelf.UploadAsync(path, content, options, cancellationToken);
            }

            // The size must be known before any byte is sent, so the encrypted bytes go to a side file first.
            await using FileStream encrypted = OpenSideFile();
            await encryptor.EncryptAsync(content, encrypted, cancellationToken);
            encrypted.Position = 0;

            // Typed as what it is once decrypted, so a client that decrypts it knows what it holds.
            return await shelf.UploadAsync(path, encrypted, options with { Encrypted = true }, cancellationToken);
        }

        /// <summary>
        /// A new file in the temp folder for bytes on their way, removed when it is closed. What passes
        /// through it may be decrypted contents, so it is readable by its owner only; a process killed
        /// before closing it leaves it there.
        /// </summary>
        public static FileStream OpenSideFile()
        {
            FileStreamOptions options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                BufferSize = CopyBufferBytes,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose,
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            return new FileStream(Path.Combine(Path.GetTempPath(), $"byteshelf-{Guid.NewGuid():N}"), options);
        }

        /// <summary>
        /// Saves <paramref name="file"/> to <paramref name="localPath"/>, or into it when it is a
        /// directory, under the name <paramref name="encrypted"/> gives it, and answers where. The bytes
        /// go to a side file first, so a failed download leaves nothing behind and replaces nothing. The
        /// side file's name is new for each download, so two downloads to one path never write into each
        /// other's, and it does not grow with the file's own name, which may already be as long as the
        /// disk allows. Read by id, so it is the file whose record the caller holds.
        /// </summary>
        /// <exception cref="UsageException">The local file exists and <paramref name="overwrite"/> is false, or an encrypted file is to be left out.</exception>
        /// <exception cref="AgeException">The password does not open the file, or it is damaged.</exception>
        public static async Task<string> DownloadToFileAsync(IShelfClient shelf, ShelfFile file, string localPath, bool overwrite, EncryptedFiles encrypted, CancellationToken cancellationToken)
        {
            string target = Path.GetFullPath(localPath);
            if (Directory.Exists(target))
            {
                target = Path.Combine(target, encrypted.NameFor(file, file.Name));
            }

            if (!overwrite && File.Exists(target))
            {
                throw new UsageException($"{target} exists already; ask to overwrite it.");
            }

            string partial = Path.Combine(Path.GetDirectoryName(target)!, $".byteshelf-{Guid.NewGuid():N}.partial");
            try
            {
                await using (FileStream output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferBytes, useAsync: true))
                {
                    await WriteAsync(shelf, file, output, encrypted, cancellationToken);
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

        /// <summary>Writes <paramref name="file"/>'s contents to <paramref name="output"/> as they arrive, as <paramref name="encrypted"/> says; read by id.</summary>
        /// <exception cref="UsageException">An encrypted file is to be left out.</exception>
        /// <exception cref="AgeException">The password does not open the file, or it is damaged.</exception>
        public static async Task WriteAsync(IShelfClient shelf, ShelfFile file, Stream output, EncryptedFiles encrypted, CancellationToken cancellationToken)
        {
            await using Stream content = await shelf.OpenReadAsync(file.Id, cancellationToken);
            await encrypted.WriteAsync(file, content, output, cancellationToken);
        }

        /// <summary>
        /// A file's whole content in memory, for an answer that carries it inline; the caller has checked
        /// its size. Read by id, so it is the file whose record the caller holds.
        /// </summary>
        public static async Task<byte[]> ReadAllAsync(IShelfClient shelf, ShelfFile file, CancellationToken cancellationToken)
        {
            using MemoryStream buffer = new MemoryStream(checked((int)file.SizeBytes));
            await using (Stream content = await shelf.OpenReadAsync(file.Id, cancellationToken))
            {
                await content.CopyToAsync(buffer, cancellationToken);
            }

            return buffer.ToArray();
        }
    }
}
