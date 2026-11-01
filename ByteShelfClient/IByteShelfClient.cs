using ByteShelfCommon;

namespace ByteShelfClient
{
    /// <summary>
    /// A ByteShelf server as one caller sees it: the shelves it can use and, for a signed-in user, the
    /// users. A program with a shelf key sees only its key's shelf. Every failure the server reports is
    /// a <see cref="ByteShelfException"/> with one of <see cref="ErrorCodes"/>.
    /// </summary>
    public interface IByteShelfClient
    {
        /// <summary>The shelf called <paramref name="name"/>. Nothing is sent until it is used.</summary>
        /// <exception cref="ArgumentException">It is not a shelf name, by <see cref="ShelfName"/>'s rules.</exception>
        IShelfClient Shelf(string name);

        /// <summary>Every shelf the caller can use, by name.</summary>
        Task<IReadOnlyList<ShelfInfo>> GetShelvesAsync(CancellationToken cancellationToken = default);

        /// <summary>Creates a shelf with an optional quota in bytes; admins only.</summary>
        Task<ShelfInfo> CreateShelfAsync(string name, long? quotaBytes = null, CancellationToken cancellationToken = default);

        /// <summary>The signed-in user.</summary>
        Task<UserInfo> GetCurrentUserAsync(CancellationToken cancellationToken = default);

        /// <summary>Changes the signed-in user's password.</summary>
        Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

        /// <summary>Every user; admins only.</summary>
        Task<IReadOnlyList<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default);

        /// <summary>Creates a user; admins only.</summary>
        Task<UserInfo> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

        /// <summary>Replaces a user's roles; admins only. The last admin cannot lose the role.</summary>
        Task SetUserRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default);

        /// <summary>Deletes a user with their grants and sessions; admins only. The last admin cannot be deleted.</summary>
        Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
