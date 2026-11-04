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

        /// <summary>
        /// This server's part in replication: its replicas and how many changes each has yet to apply,
        /// or, on a replica, the primary it follows and how far it has got; admins only.
        /// </summary>
        Task<ReplicationStatus> GetReplicationAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// A pairing code for one new replica, usable once until it runs out; admins only, and refused on
        /// a replica. It carries this server's address, so the replica needs nothing else to follow it.
        /// </summary>
        Task<PairingCodeResponse> CreatePairingCodeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Makes this server a replica of the primary that made the request's code, named as the request
        /// says there; admins only. Only a server without shelves or replicas of its own may: it takes
        /// the primary's shelves and users, its own users replaced, and refuses writes from then on.
        /// </summary>
        Task<ReplicationStatus> FollowPrimaryAsync(PairWithPrimaryRequest request, CancellationToken cancellationToken = default);

        /// <summary>Unpairs a replica of this server, which can no longer read its changes; admins only.</summary>
        Task RemoveReplicaAsync(Guid replicaId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops following the primary, so this replica takes writes as a primary of its own; admins
        /// only. Answers whether the old primary heard; when not, it lists the replica until it is removed.
        /// </summary>
        Task<PromoteResponse> PromoteAsync(CancellationToken cancellationToken = default);
    }
}
