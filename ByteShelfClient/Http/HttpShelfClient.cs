using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelfClient.Http
{
    /// <summary><see cref="IShelfClient"/> over HTTP, for <see cref="ByteShelfHttpClient.Shelf"/>.</summary>
    internal sealed class HttpShelfClient : IShelfClient
    {
        /// <summary>How long aborting a failed upload may hold up reporting the failure.</summary>
        private static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(10);

        private readonly ApiConnection _connection;
        private readonly ByteShelfHttpClientOptions _options;
        private readonly string _base;

        public HttpShelfClient(ApiConnection connection, ByteShelfHttpClientOptions options, string name)
        {
            _connection = connection;
            _options = options;
            Name = name;
            _base = "api/shelves/" + Uri.EscapeDataString(name);
        }

        public string Name { get; }

        public Task<ShelfInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Get, _base, ByteShelfJsonContext.Default.ShelfInfo, cancellationToken);
        }

        public Task<ShelfInfo> UpdateAsync(string name, long? quotaBytes, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(
                HttpMethod.Put,
                _base,
                new UpdateShelfRequest { Name = name, QuotaBytes = quotaBytes },
                ByteShelfJsonContext.Default.UpdateShelfRequest,
                ByteShelfJsonContext.Default.ShelfInfo,
                cancellationToken);
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Delete, _base, cancellationToken);
        }

        public Task<DirectoryListing> ListAsync(EntryReference? directory = null, CancellationToken cancellationToken = default)
        {
            string uri = directory == null ? $"{_base}/entries" : $"{_base}/entries?{directory.ToQuery()}";
            return _connection.SendAsync(HttpMethod.Get, uri, ByteShelfJsonContext.Default.DirectoryListing, cancellationToken);
        }

        public Task<ShelfFile> GetFileAsync(EntryReference file, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            return _connection.SendAsync(HttpMethod.Get, $"{_base}/files?{file.ToQuery()}", ByteShelfJsonContext.Default.ShelfFile, cancellationToken);
        }

        public async Task<ShelfFile> UploadAsync(string path, Stream content, UploadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(content);
            string normalizedPath = ShelfPath.Normalize(path);
            options ??= new UploadOptions();
            string contentType = ContentTypes.Normalize(options.ContentType);
            string? description = FileDescription.Normalize(options.Description);

            await using UploadSource source = await UploadSource.OpenAsync(content, cancellationToken).ConfigureAwait(false);

            return source.SizeBytes <= _options.SingleRequestMaxBytes && description == null
                ? await PutAsync(normalizedPath, source, contentType, options.Overwrite, cancellationToken).ConfigureAwait(false)
                : await UploadInChunksAsync(normalizedPath, source, contentType, options.Overwrite, description, cancellationToken).ConfigureAwait(false);
        }

        public Task<ShelfFile> SetDescriptionAsync(EntryReference file, string? description, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            return _connection.SendAsync(
                HttpMethod.Put,
                $"{_base}/files/description?{file.ToQuery()}",
                new SetDescriptionRequest { Description = FileDescription.Normalize(description) },
                ByteShelfJsonContext.Default.SetDescriptionRequest,
                ByteShelfJsonContext.Default.ShelfFile,
                cancellationToken);
        }

        public Task<FileSearchResults> SearchAsync(FileSearch search, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(search);
            return _connection.SendAsync(HttpMethod.Get, $"{_base}/search?{search.Normalize().ToQuery()}", ByteShelfJsonContext.Default.FileSearchResults, cancellationToken);
        }

        public async Task<Stream> OpenReadAsync(EntryReference file, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            HttpResponseMessage response = await _connection.SendRawAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"{_base}/content?{file.ToQuery()}"),
                cancellationToken,
                completion: HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            try
            {
                // The server's ETag is the contents' SHA-256, quoted.
                string? sha256 = response.StatusCode == HttpStatusCode.OK ? response.Headers.ETag?.Tag.Trim('"') : null;
                return new ResponseStream(response, await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), sha256);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        public Task<ShelfDirectory> CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(
                HttpMethod.Post,
                $"{_base}/directories",
                new CreateDirectoryRequest { Path = ShelfPath.Normalize(path) },
                ByteShelfJsonContext.Default.CreateDirectoryRequest,
                ByteShelfJsonContext.Default.ShelfDirectory,
                cancellationToken);
        }

        public Task<ShelfFile> MoveFileAsync(EntryReference file, string toPath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            return _connection.SendAsync(
                HttpMethod.Post,
                $"{_base}/files/move?{file.ToQuery()}",
                new MoveRequest { ToPath = ShelfPath.Normalize(toPath) },
                ByteShelfJsonContext.Default.MoveRequest,
                ByteShelfJsonContext.Default.ShelfFile,
                cancellationToken);
        }

        public Task<ShelfDirectory> MoveDirectoryAsync(EntryReference directory, string toPath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(directory);
            return _connection.SendAsync(
                HttpMethod.Post,
                $"{_base}/directories/move?{directory.ToQuery()}",
                new MoveRequest { ToPath = ShelfPath.Normalize(toPath) },
                ByteShelfJsonContext.Default.MoveRequest,
                ByteShelfJsonContext.Default.ShelfDirectory,
                cancellationToken);
        }

        public Task<TrashEntry> DeleteFileAsync(EntryReference file, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            return _connection.SendAsync(HttpMethod.Delete, $"{_base}/files?{file.ToQuery()}", ByteShelfJsonContext.Default.TrashEntry, cancellationToken);
        }

        public Task<TrashEntry> DeleteDirectoryAsync(EntryReference directory, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(directory);
            return _connection.SendAsync(HttpMethod.Delete, $"{_base}/directories?{directory.ToQuery()}", ByteShelfJsonContext.Default.TrashEntry, cancellationToken);
        }

        public Task<IReadOnlyList<TrashEntry>> GetTrashAsync(CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Get, $"{_base}/trash", ByteShelfJsonContext.Default.IReadOnlyListTrashEntry, cancellationToken);
        }

        public Task RestoreAsync(Guid deletionId, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Post, $"{_base}/trash/{deletionId:D}/restore", cancellationToken);
        }

        public Task EmptyTrashAsync(CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Delete, $"{_base}/trash", cancellationToken);
        }

        public Task<IReadOnlyList<ShelfAccessGrant>> GetGrantsAsync(CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Get, $"{_base}/access", ByteShelfJsonContext.Default.IReadOnlyListShelfAccessGrant, cancellationToken);
        }

        public Task<ShelfAccessGrant> SetGrantAsync(Guid userId, ShelfAccessPermission permission, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(
                HttpMethod.Put,
                $"{_base}/access/{userId:D}",
                new SetShelfAccessRequest { Permission = permission },
                ByteShelfJsonContext.Default.SetShelfAccessRequest,
                ByteShelfJsonContext.Default.ShelfAccessGrant,
                cancellationToken);
        }

        public Task RemoveGrantAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Delete, $"{_base}/access/{userId:D}", cancellationToken);
        }

        public Task<IReadOnlyList<ShelfKeyInfo>> GetKeysAsync(CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Get, $"{_base}/keys", ByteShelfJsonContext.Default.IReadOnlyListShelfKeyInfo, cancellationToken);
        }

        public Task<CreatedShelfKey> CreateKeyAsync(string name, ShelfKeyPermission permission, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(
                HttpMethod.Post,
                $"{_base}/keys",
                new CreateShelfKeyRequest { Name = name, Permission = permission },
                ByteShelfJsonContext.Default.CreateShelfKeyRequest,
                ByteShelfJsonContext.Default.CreatedShelfKey,
                cancellationToken);
        }

        public Task DeleteKeyAsync(Guid keyId, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Delete, $"{_base}/keys/{keyId:D}", cancellationToken);
        }

        /// <summary>Sends the whole file in one streamed request, which cannot be repeated after a refused token.</summary>
        private async Task<ShelfFile> PutAsync(string path, UploadSource source, string contentType, bool overwrite, CancellationToken cancellationToken)
        {
            string uri = $"{_base}/content?{EntryReference.ForPath(path).ToQuery()}&overwrite={(overwrite ? "true" : "false")}";

            using HttpResponseMessage response = await _connection.SendRawAsync(
                () =>
                {
                    StreamContent body = new StreamContent(source.Stream);
                    body.Headers.ContentLength = source.SizeBytes;
                    body.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    return new HttpRequestMessage(HttpMethod.Put, uri) { Content = body };
                },
                cancellationToken,
                canRetry: false).ConfigureAwait(false);

            return await ApiConnection.ReadAsync(response, ByteShelfJsonContext.Default.ShelfFile, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a resumable upload, sends the content a chunk at a time, and completes it. A failure
        /// aborts the upload, so its reservation against the quota does not linger until it expires.
        /// </summary>
        private async Task<ShelfFile> UploadInChunksAsync(string path, UploadSource source, string contentType, bool overwrite, string? description, CancellationToken cancellationToken)
        {
            UploadSession session = await _connection.SendAsync(
                HttpMethod.Post,
                $"{_base}/uploads",
                new StartUploadRequest { Path = path, SizeBytes = source.SizeBytes, ContentType = contentType, Overwrite = overwrite, Description = description },
                ByteShelfJsonContext.Default.StartUploadRequest,
                ByteShelfJsonContext.Default.UploadSession,
                cancellationToken).ConfigureAwait(false);

            string uploadUri = $"{_base}/uploads/{session.Id:D}";
            try
            {
                byte[] chunk = new byte[(int)Math.Min(_options.ChunkBytes, Math.Max(source.SizeBytes, 1))];
                long offset = 0;
                while (offset < source.SizeBytes)
                {
                    int length = (int)Math.Min(chunk.Length, source.SizeBytes - offset);
                    await source.Stream.ReadExactlyAsync(chunk.AsMemory(0, length), cancellationToken).ConfigureAwait(false);

                    string chunkUri = $"{uploadUri}?offset={offset.ToString(CultureInfo.InvariantCulture)}";
                    using HttpResponseMessage sent = await _connection.SendRawAsync(
                        () => new HttpRequestMessage(HttpMethod.Put, chunkUri) { Content = new ByteArrayContent(chunk, 0, length) },
                        cancellationToken).ConfigureAwait(false);

                    offset += length;
                }

                return await _connection.SendAsync(HttpMethod.Post, $"{uploadUri}/complete", ByteShelfJsonContext.Default.ShelfFile, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await AbortAsync(uploadUri).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Aborts an upload after a failure, as far as it can within <see cref="AbortTimeout"/> and even
        /// when the caller cancelled. The upload expires on its own anyway, and the failure that led here
        /// is the one to report, so nothing this throws escapes.
        /// </summary>
        private async Task AbortAsync(string uploadUri)
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(AbortTimeout);
            try
            {
                await _connection.SendAsync(HttpMethod.Delete, uploadUri, timeout.Token).ConfigureAwait(false);
            }
            catch (ByteShelfException)
            {
            }
            catch (HttpRequestException)
            {
            }
            catch (OperationCanceledException)
            {
                // The timeout above, or HttpClient.Timeout.
            }
        }
    }
}
