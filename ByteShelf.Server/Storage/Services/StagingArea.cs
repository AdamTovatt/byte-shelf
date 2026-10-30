using System.Security.Cryptography;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// The bytes of uploads in progress, one file per upload at <c>staging/&lt;upload id&gt;</c>. Staging
    /// sits in the root next to <c>objects/</c>, so a finished upload becomes an object by a rename.
    /// </summary>
    public sealed class StagingArea
    {
        private const int BufferSize = 81920;

        private readonly RootFolder _root;

        public StagingArea(RootFolder root)
        {
            _root = root;
        }

        public string GetPath(Guid uploadId)
        {
            return Path.Combine(_root.StagingPath, uploadId.ToString("D"));
        }

        /// <summary>
        /// Writes <paramref name="content"/> at <paramref name="offset"/>, refusing to go past
        /// <paramref name="maxBytes"/> in total. Anything past <paramref name="offset"/> already on disk
        /// (left by a request that wrote but never recorded it) is dropped first. Answers how many bytes
        /// were written; on any failure the file is cut back to <paramref name="offset"/>.
        /// </summary>
        /// <exception cref="UploadOverflowException">The content runs past <paramref name="maxBytes"/>.</exception>
        public async Task<long> WriteAsync(Guid uploadId, long offset, Stream content, long maxBytes, CancellationToken cancellationToken)
        {
            string path = GetPath(uploadId);

            await using FileStream file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous);

            if (file.Length < offset)
            {
                throw new InvalidOperationException($"Staging file for upload {uploadId} holds {file.Length} bytes, fewer than the {offset} recorded.");
            }

            file.SetLength(offset);
            file.Position = offset;

            byte[] buffer = new byte[BufferSize];
            long written = 0;

            try
            {
                while (true)
                {
                    int read = await content.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    if (offset + written + read > maxBytes)
                    {
                        throw new UploadOverflowException(maxBytes);
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                }

                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
                return written;
            }
            catch
            {
                file.SetLength(offset);
                throw;
            }
        }

        /// <summary>The lowercase hex SHA-256 of the staged bytes.</summary>
        public async Task<string> ComputeSha256Async(Guid uploadId, CancellationToken cancellationToken)
        {
            await using FileStream file = new FileStream(GetPath(uploadId), FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(file, cancellationToken);
            return Convert.ToHexStringLower(hash);
        }

        /// <summary>Makes sure an upload's staging file exists, so a zero-byte upload has something to finish.</summary>
        public void EnsureExists(Guid uploadId)
        {
            using FileStream file = new FileStream(GetPath(uploadId), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        }

        public void Delete(Guid uploadId)
        {
            string path = GetPath(uploadId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Everything in <c>staging/</c>, with the upload id each entry is named for (null when its name is not one).</summary>
        public IReadOnlyList<StagingEntry> List()
        {
            return Directory.EnumerateFileSystemEntries(_root.StagingPath)
                .Select(path => new StagingEntry(path, Guid.TryParseExact(Path.GetFileName(path), "D", out Guid parsed) ? parsed : null))
                .ToList();
        }

        /// <summary>Removes an entry <see cref="List"/> returned, whether a file or a directory.</summary>
        public void Remove(StagingEntry entry)
        {
            if (Directory.Exists(entry.Path))
            {
                Directory.Delete(entry.Path, recursive: true);
            }
            else if (File.Exists(entry.Path))
            {
                File.Delete(entry.Path);
            }
        }
    }
}
