using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelfClient.Http
{
    /// <summary>
    /// <see cref="IByteShelfClient"/> over HTTP. The <see cref="HttpClient"/> is the caller's, with the
    /// server's address as its base address, ending in <c>/</c>; it is not disposed here.
    /// </summary>
    public sealed class ByteShelfHttpClient : IByteShelfClient
    {
        private readonly ApiConnection _connection;
        private readonly ByteShelfHttpClientOptions _options;

        /// <param name="http">A client whose base address is the server's, ending in <c>/</c>.</param>
        /// <param name="credentials">A <see cref="ShelfKeyCredentials"/> or a <see cref="UserSession"/>.</param>
        /// <param name="options">How files are sent; the defaults when left out.</param>
        public ByteShelfHttpClient(HttpClient http, ByteShelfCredentials credentials, ByteShelfHttpClientOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(credentials);

            if (http.BaseAddress == null || !http.BaseAddress.AbsolutePath.EndsWith('/'))
            {
                throw new ArgumentException("The client's base address must be the server's address, ending in '/'.", nameof(http));
            }

            _options = options ?? new ByteShelfHttpClientOptions();
            if (_options.ChunkBytes <= 0 || _options.ChunkBytes > UploadLimits.MaxChunkBytes || _options.SingleRequestMaxBytes < 0)
            {
                throw new ArgumentException($"Chunks must be 1 to {UploadLimits.MaxChunkBytes} bytes, and the single-request limit cannot be negative.", nameof(options));
            }

            _connection = new ApiConnection(http, credentials);
        }

        /// <inheritdoc />
        public IShelfClient Shelf(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return new HttpShelfClient(_connection, _options, name);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ShelfInfo>> GetShelvesAsync(CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Get, "api/shelves", ByteShelfJsonContext.Default.IReadOnlyListShelfInfo, cancellationToken);
        }

        /// <inheritdoc />
        public Task<ShelfInfo> CreateShelfAsync(string name, long? quotaBytes = null, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(
                HttpMethod.Post,
                "api/shelves",
                new CreateShelfRequest { Name = name, QuotaBytes = quotaBytes },
                ByteShelfJsonContext.Default.CreateShelfRequest,
                ByteShelfJsonContext.Default.ShelfInfo,
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<UserInfo> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Get, "api/users/me", ByteShelfJsonContext.Default.UserInfo, cancellationToken);
        }

        /// <inheritdoc />
        public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(
                HttpMethod.Put,
                "api/users/me/password",
                new UpdatePasswordRequest { CurrentPassword = currentPassword, NewPassword = newPassword },
                ByteShelfJsonContext.Default.UpdatePasswordRequest,
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Get, "api/users", ByteShelfJsonContext.Default.IReadOnlyListUserInfo, cancellationToken);
        }

        /// <inheritdoc />
        public Task<UserInfo> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(
                HttpMethod.Post,
                "api/users",
                request,
                ByteShelfJsonContext.Default.CreateUserRequest,
                ByteShelfJsonContext.Default.UserInfo,
                cancellationToken);
        }

        /// <inheritdoc />
        public Task SetUserRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(
                HttpMethod.Put,
                $"api/users/{userId:D}/roles",
                new UpdateRolesRequest { Roles = roles },
                ByteShelfJsonContext.Default.UpdateRolesRequest,
                cancellationToken);
        }

        /// <inheritdoc />
        public Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(HttpMethod.Delete, $"api/users/{userId:D}", cancellationToken);
        }
    }
}
