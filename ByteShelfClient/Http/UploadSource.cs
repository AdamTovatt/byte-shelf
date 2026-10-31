namespace ByteShelfClient.Http
{
    /// <summary>
    /// An upload's content with its length known, which the server needs up front: the caller's stream
    /// from its position when it can seek, or else a temporary copy deleted on dispose.
    /// </summary>
    internal sealed class UploadSource : IAsyncDisposable
    {
        private const int CopyBufferBytes = 81920;

        private readonly FileStream? _copy;

        private UploadSource(Stream stream, long sizeBytes, FileStream? copy)
        {
            Stream = stream;
            SizeBytes = sizeBytes;
            _copy = copy;
        }

        public Stream Stream { get; }

        public long SizeBytes { get; }

        public static async Task<UploadSource> OpenAsync(Stream content, CancellationToken cancellationToken)
        {
            if (content.CanSeek)
            {
                StreamTail tail = new StreamTail(content);
                return new UploadSource(tail, tail.Length, copy: null);
            }

            FileStream copy = new FileStream(
                Path.GetTempFileName(),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                CopyBufferBytes,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            try
            {
                await content.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
                copy.Position = 0;
                return new UploadSource(copy, copy.Length, copy);
            }
            catch
            {
                await copy.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            return _copy?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
