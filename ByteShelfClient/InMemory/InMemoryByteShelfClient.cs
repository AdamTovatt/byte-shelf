using ByteShelfCommon;

namespace ByteShelfClient.InMemory
{
    /// <summary>
    /// <see cref="IByteShelfClient"/> held in memory, for an app's tests to run without a server. It
    /// follows the server's rules for paths, names, conflicts, quotas and the trash, and acts as a
    /// signed-in admin, <see cref="CurrentUser"/>: it does not model access, so nothing is refused for
    /// lack of permission. Replication is modelled by its rules and state alone: one in-memory client can
    /// follow another in the same process, and then refuses writes until promoted, but a follower copies
    /// no data.
    /// </summary>
    public sealed class InMemoryByteShelfClient : IByteShelfClient
    {
        /// <summary>The restore window, as the server's default.</summary>
        public static readonly TimeSpan DefaultRestoreWindow = TimeSpan.FromDays(30);

        private readonly InMemoryStore _store;

        /// <param name="email">The email of the admin the client acts as.</param>
        /// <param name="password">That admin's password, for <see cref="ChangePasswordAsync"/>.</param>
        /// <param name="timeProvider">The clock stamped on everything and the restore window is judged by; the system's when left out.</param>
        /// <param name="restoreWindow">How long deleted things can be restored; <see cref="DefaultRestoreWindow"/> when left out.</param>
        public InMemoryByteShelfClient(
            string email = "admin@byteshelf.test",
            string password = "password-for-tests",
            TimeProvider? timeProvider = null,
            TimeSpan? restoreWindow = null)
        {
            _store = new InMemoryStore(timeProvider ?? TimeProvider.System, restoreWindow ?? DefaultRestoreWindow);
            CurrentUser = AddUser(new CreateUserRequest { Email = email, DisplayName = "Admin", Password = password, Roles = new[] { UserRoles.Admin } });
        }

        /// <summary>The admin the client acts as.</summary>
        public UserInfo CurrentUser { get; }

        /// <summary>What stands for this server's public URL: the primary a follower's status names.</summary>
        public string Address => _store.Address;

        /// <inheritdoc />
        public IShelfClient Shelf(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            ShelfName.Validate(name);
            return new InMemoryShelfClient(_store, name);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ShelfInfo>> GetShelvesAsync(CancellationToken cancellationToken = default)
        {
            return _store.RunAsync<IReadOnlyList<ShelfInfo>>(
                () => _store.Shelves
                    .OrderBy(shelf => shelf.Name, StringComparer.Ordinal)
                    .Select(shelf => _store.GetShelf(shelf.Name).ToInfo())
                    .ToList(),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<ShelfInfo> CreateShelfAsync(string name, long? quotaBytes = null, CancellationToken cancellationToken = default)
        {
            return _store.WriteAsync(
                () =>
                {
                    InMemoryErrors.Checked(() => ShelfSettings.Validate(name, quotaBytes));
                    if (_store.Shelves.Any(shelf => shelf.Name == name))
                    {
                        throw InMemoryErrors.Conflict($"A shelf called '{name}' already exists.");
                    }

                    InMemoryShelf created = new InMemoryShelf(name, quotaBytes, _store.Now);
                    _store.Shelves.Add(created);
                    return created.ToInfo();
                },
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<UserInfo> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            return _store.RunAsync(() => _store.GetUser(CurrentUser.Id).ToInfo(), cancellationToken);
        }

        /// <inheritdoc />
        public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        {
            return _store.WriteAsync(
                () =>
                {
                    InMemoryErrors.Checked(() => PasswordPolicy.Validate(newPassword));
                    InMemoryStore.UserRow user = _store.GetUser(CurrentUser.Id);
                    if (user.Password != currentPassword)
                    {
                        throw new ByteShelfException("The current password is incorrect.", ErrorCodes.InvalidCredentials);
                    }

                    user.Password = newPassword;
                },
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            return _store.RunAsync<IReadOnlyList<UserInfo>>(() => _store.Users.Select(user => user.ToInfo()).ToList(), cancellationToken);
        }

        /// <inheritdoc />
        public Task<UserInfo> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _store.WriteAsync(() => AddUser(request), cancellationToken);
        }

        /// <inheritdoc />
        public Task SetUserRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(roles);
            return _store.WriteAsync(
                () =>
                {
                    IReadOnlyList<string> distinct = InMemoryErrors.Checked(() => UserRoles.Normalize(roles));
                    InMemoryStore.UserRow user = _store.GetUser(userId);
                    if (user.Roles.Contains(UserRoles.Admin) && !distinct.Contains(UserRoles.Admin) && IsLastAdmin(user))
                    {
                        throw InMemoryErrors.Conflict("This is the only admin; make someone else an admin first.");
                    }

                    user.Roles = distinct;
                },
                cancellationToken);
        }

        /// <inheritdoc />
        public Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _store.WriteAsync(
                () =>
                {
                    InMemoryStore.UserRow user = _store.GetUser(userId);
                    if (user.Roles.Contains(UserRoles.Admin) && IsLastAdmin(user))
                    {
                        throw InMemoryErrors.Conflict("This is the only admin; make someone else an admin first.");
                    }

                    foreach (InMemoryShelf shelf in _store.Shelves)
                    {
                        shelf.Grants.RemoveAll(grant => grant.UserId == userId);
                    }

                    _store.Users.Remove(user);
                },
                cancellationToken);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Modelled without data: no change log, so every sequence is 0 and a replica is never behind; it
        /// counts as seen, and caught up, from the moment it paired.
        /// </remarks>
        public Task<ReplicationStatus> GetReplicationAsync(CancellationToken cancellationToken = default)
        {
            return _store.RunAsync(_store.Replication.Status, cancellationToken);
        }

        /// <inheritdoc />
        /// <remarks>The code works only with another <see cref="InMemoryByteShelfClient"/> in this process.</remarks>
        public Task<PairingCodeResponse> CreatePairingCodeAsync(CancellationToken cancellationToken = default)
        {
            return _store.WriteAsync(_store.Replication.NewCode, cancellationToken);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Follows another <see cref="InMemoryByteShelfClient"/> in this process by the server's rules,
        /// and refuses writes from then on, but copies no data: its shelves stay empty and its users its own.
        /// </remarks>
        public Task<ReplicationStatus> FollowPrimaryAsync(PairWithPrimaryRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _store.WriteAsync(() => _store.Replication.Follow(request), cancellationToken);
        }

        /// <inheritdoc />
        public Task RemoveReplicaAsync(Guid replicaId, CancellationToken cancellationToken = default)
        {
            return _store.WriteAsync(() => _store.Replication.RemoveReplica(replicaId), cancellationToken);
        }

        /// <inheritdoc />
        public Task<PromoteResponse> PromoteAsync(CancellationToken cancellationToken = default)
        {
            return _store.RunAsync(_store.Replication.Promote, cancellationToken);
        }

        private bool IsLastAdmin(InMemoryStore.UserRow user)
        {
            return !_store.Users.Any(other => other != user && other.Roles.Contains(UserRoles.Admin));
        }

        private UserInfo AddUser(CreateUserRequest request)
        {
            InMemoryErrors.Checked(() => UserFields.ValidateEmail(request.Email));
            string displayName = InMemoryErrors.Checked(() => UserFields.NormalizeDisplayName(request.DisplayName));
            InMemoryErrors.Checked(() => PasswordPolicy.Validate(request.Password));
            IReadOnlyList<string> roles = InMemoryErrors.Checked(() => UserRoles.Normalize(request.Roles));

            if (_store.Users.Any(user => string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase)))
            {
                throw InMemoryErrors.Conflict($"A user with the email '{request.Email}' already exists.");
            }

            InMemoryStore.UserRow added = new InMemoryStore.UserRow
            {
                Id = Guid.NewGuid(),
                Email = request.Email,
                DisplayName = displayName,
                Roles = roles,
                Password = request.Password,
            };

            _store.Users.Add(added);
            return added.ToInfo();
        }
    }
}
