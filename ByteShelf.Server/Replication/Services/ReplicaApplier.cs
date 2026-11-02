using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelf.Server.Users.Services;
using EasyReasy.Database;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// Applies a primary's change-log entries to this replica, each by its entity's type: a state is
    /// written whole by id, a removal removes. Applying an entry twice is harmless, and so is applying
    /// one older than what is here: whatever it displaces is set aside until a later entry puts it back,
    /// so the log converges on the primary's state however far ahead of it a copy was. Every write is
    /// logged here as on any server, so open pages update and a promoted replica keeps a whole log.
    /// </summary>
    /// <remarks>
    /// A file or directory the primary purged is only moved to the trash here, so it is purged by this
    /// server's own maintenance once its restore window has passed: a mistaken purge on the primary does
    /// not reach the backup at once. A shelf's removal removes what it held at once, since the primary
    /// removes only an emptied shelf. So do a file's old contents when they were swapped (encrypting or
    /// decrypting it in place), the one change a person confirmed cannot be undone.
    /// </remarks>
    public sealed class ReplicaApplier
    {
        private readonly IReadOnlyList<EntityHandler> _handlers;
        private readonly IReplicationCopyRepository _copyRepository;
        private readonly TimeProvider _timeProvider;

        public ReplicaApplier(
            IUserRepository userRepository,
            IUserService userService,
            IUserLoginRepository loginRepository,
            IShelfRepository shelfRepository,
            IDirectoryRepository directoryRepository,
            IFileRepository fileRepository,
            IShelfGrantRepository grantRepository,
            IShelfKeyRepository keyRepository,
            IReplicationCopyRepository copyRepository,
            TimeProvider timeProvider)
        {
            _copyRepository = copyRepository;
            _timeProvider = timeProvider;

            // Every entity type the change log holds, with its table and how to write and remove one. The
            // order is the copy's removal pass: what refers to others first. A file or directory set aside
            // here goes to the trash as a deletion of its own.
            // Not done (review items 27 and 28, a taste call): each feature owning a handler for its
            // entities, keyed on typed descriptors such as ChangeEntity<FileRecord> rather than strings.
            // The list below works and keeps the removal order in one place.
            _handlers = new[]
            {
                new EntityHandler(
                    ChangeEntityType.ShelfKey,
                    "shelf_key",
                    (entry, at, session, _) => keyRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<ShelfKey>(entry.Payload!), entry.Action, at, session),
                    keyRepository.DeleteByIdAsync),
                new EntityHandler(
                    ChangeEntityType.ShelfGrant,
                    "shelf_grant",
                    (entry, at, session, _) => grantRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<ShelfGrant>(entry.Payload!), entry.Action, at, session),
                    grantRepository.DeleteByIdAsync),
                new EntityHandler(
                    ChangeEntityType.UserLogin,
                    "user_login",
                    (entry, at, session, _) => loginRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<UserLogin>(entry.Payload!), entry.Action, at, session),
                    loginRepository.DeleteByIdAsync),
                new EntityHandler(
                    ChangeEntityType.File,
                    "file",
                    async (entry, at, session, changes) =>
                    {
                        // A file's row points at other contents only when they were swapped in place
                        // (encrypting or decrypting it), whose old ones go at once here too.
                        FileRecord file = ChangePayload.Deserialize<FileRecord>(entry.Payload!);
                        if (await fileRepository.GetByIdAsync(file.Id, session) is FileRecord here && here.Sha256 != file.Sha256)
                        {
                            changes.ObjectReleased(here.Sha256);
                        }

                        await fileRepository.UpsertReplicatedAsync(file, entry.Action, Guid.NewGuid(), at, session);
                    },
                    (id, at, session) => fileRepository.SoftDeleteAsync(id, Guid.NewGuid(), at, session)),
                new EntityHandler(
                    ChangeEntityType.Directory,
                    "directory",
                    (entry, at, session, _) => directoryRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<DirectoryRecord>(entry.Payload!), entry.Action, Guid.NewGuid(), at, session),
                    (id, at, session) => directoryRepository.TrashIfLiveAsync(id, Guid.NewGuid(), at, session)),
                new EntityHandler(
                    ChangeEntityType.Shelf,
                    "shelf",
                    (entry, at, session, _) => shelfRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<Shelf>(entry.Payload!), entry.Action, at, session),
                    async (id, at, session) =>
                    {
                        if (await shelfRepository.GetByIdAsync(id, session) == null)
                        {
                            return false;
                        }

                        await fileRepository.PurgeForShelfAsync(id, at, session);
                        await directoryRepository.PurgeForShelfAsync(id, at, session);
                        await grantRepository.DeleteForShelfAsync(id, at, session);
                        await keyRepository.DeleteForShelfAsync(id, at, session);
                        await shelfRepository.DeleteAsync(id, at, session);
                        return true;
                    }),
                new EntityHandler(
                    ChangeEntityType.User,
                    "user_account",
                    (entry, at, session, _) => userRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<User>(entry.Payload!), entry.Action, at, session),
                    userService.RemoveUserAsync,
                    RemovalEndsSessions: true),
            };
        }

        /// <summary>The entity types this server can apply, in the order a copy removes what it no longer holds.</summary>
        public IReadOnlyList<string> EntityTypes => _handlers.Select(handler => handler.EntityType).ToList();

        /// <summary>
        /// Applies <paramref name="entries"/> in order, noting in <paramref name="changes"/> what is left
        /// to do once they commit.
        /// </summary>
        /// <exception cref="ReplicaOutdatedException">An entry is of a type this build does not know.</exception>
        public async Task ApplyAsync(IReadOnlyList<ChangeLogEntry> entries, IDbSession session, AppliedChanges changes)
        {
            // An entry may name a parent that a later one in the same batch writes.
            await session.DeferForeignKeysAsync();

            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (ChangeLogEntry entry in entries)
            {
                EntityHandler handler = _handlers.FirstOrDefault(candidate => candidate.EntityType == entry.EntityType)
                    ?? throw new ReplicaOutdatedException(entry.EntityType);

                if (entry.Payload == null)
                {
                    await RemoveAsync(handler, entry.EntityId, now, session, changes);
                }
                else
                {
                    await handler.WriteAsync(entry, now, session, changes);
                }
            }
        }

        /// <summary>
        /// Removes everything here that the whole copy in <c>replication_copy</c> does not hold, as removal
        /// entries for it would, noting in <paramref name="changes"/> what is left to do once it commits.
        /// </summary>
        public async Task RemoveMissingFromCopyAsync(IDbSession session, AppliedChanges changes)
        {
            await session.DeferForeignKeysAsync();

            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (EntityHandler handler in _handlers)
            {
                foreach (Guid id in await _copyRepository.GetIdsMissingAsync(handler.EntityType, handler.Table, session))
                {
                    await RemoveAsync(handler, id, now, session, changes);
                }
            }
        }

        private static async Task RemoveAsync(EntityHandler handler, Guid id, DateTimeOffset at, IDbSession session, AppliedChanges changes)
        {
            if (await handler.RemoveAsync(id, at, session) && handler.RemovalEndsSessions)
            {
                changes.UserRemoved(id);
            }
        }

        /// <param name="EntityType">The type as <see cref="ChangeEntityType"/> names it.</param>
        /// <param name="Table">The table its rows are in, for finding what a copy no longer holds.</param>
        /// <param name="WriteAsync">Writes an entry's state, noting the objects it leaves that entity no longer pointing at.</param>
        /// <param name="RemoveAsync">Removes one by id; false when there was none.</param>
        /// <param name="RemovalEndsSessions">Whether removing one ends the sessions of the user it is.</param>
        private sealed record EntityHandler(
            string EntityType,
            string Table,
            Func<ChangeLogEntry, DateTimeOffset, IDbSession, AppliedChanges, Task> WriteAsync,
            Func<Guid, DateTimeOffset, IDbSession, Task<bool>> RemoveAsync,
            bool RemovalEndsSessions = false);
    }

    /// <summary>What applying entries leaves to do once they commit, noted as they are applied.</summary>
    public sealed class AppliedChanges
    {
        private readonly List<Guid> _removedUsers = new List<Guid>();
        private readonly HashSet<string> _releasedObjects = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The users removed, whose sessions end.</summary>
        public IReadOnlyCollection<Guid> RemovedUsers => _removedUsers;

        /// <summary>Objects files pointed at before their contents were swapped, to remove unless a row still points at one.</summary>
        public IReadOnlyCollection<string> ReleasedObjects => _releasedObjects;

        public void UserRemoved(Guid userId)
        {
            _removedUsers.Add(userId);
        }

        public void ObjectReleased(string sha256)
        {
            _releasedObjects.Add(sha256);
        }
    }
}
