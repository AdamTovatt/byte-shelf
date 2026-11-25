using System.Text.Json;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Errors;
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
            IPasskeyRepository passkeyRepository,
            IShelfRepository shelfRepository,
            IDirectoryRepository directoryRepository,
            IFileRepository fileRepository,
            IObjectMetadataRepository metadataRepository,
            IContentPluginMetadataRepository contentPluginMetadataRepository,
            IFilePluginMetadataRepository filePluginMetadataRepository,
            IShelfGrantRepository grantRepository,
            ShelfKeyRemoval keyRemoval,
            IReplicationCopyRepository copyRepository,
            TimeProvider timeProvider)
        {
            _copyRepository = copyRepository;
            _timeProvider = timeProvider;

            // Every entity type the change log holds, with its table and how to write and remove one. The
            // order is the copy's removal pass: what refers to others first. A file or directory set aside
            // here goes to the trash as a deletion of its own. One list rather than a handler per feature:
            // the removal order spans the features, and here it stays in one place.
            _handlers = new[]
            {
                new EntityHandler(
                    ChangeEntityType.ShelfKeyGrant,
                    "shelf_key_grant",
                    (entry, at, session) => keyRemoval.UpsertReplicatedGrantAsync(ChangePayload.Deserialize<ShelfKeyGrant>(entry.Payload!), entry.Action, at, session),
                    keyRemoval.RemoveReplicatedGrantAsync),
                new EntityHandler(
                    ChangeEntityType.ShelfKey,
                    "shelf_key",
                    (entry, at, session) => keyRemoval.UpsertReplicatedKeyAsync(ChangePayload.Deserialize<ShelfKey>(entry.Payload!), entry.Action, at, session),
                    // The primary logs a key's grants going before the key; an older state applied since may
                    // have brought one back, so they go with it here too.
                    keyRemoval.DeleteKeyAsync),
                new EntityHandler(
                    ChangeEntityType.ShelfGrant,
                    "shelf_grant",
                    (entry, at, session) => grantRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<ShelfGrant>(entry.Payload!), entry.Action, at, session),
                    grantRepository.DeleteByIdAsync),
                new EntityHandler(
                    ChangeEntityType.UserLogin,
                    "user_login",
                    (entry, at, session) => loginRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<UserLogin>(entry.Payload!), entry.Action, at, session),
                    loginRepository.DeleteByIdAsync),
                new EntityHandler(
                    ChangeEntityType.ObjectMetadata,
                    "object_metadata",
                    (entry, at, session) => metadataRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<ObjectMetadata>(entry.Payload!), entry.Action, at, session),
                    metadataRepository.DeleteByIdAsync,
                    ReleaseAsync: metadataRepository.ReleaseByIdAsync),
                new EntityHandler(
                    ChangeEntityType.ContentPluginMetadata,
                    "content_plugin_metadata",
                    (entry, at, session) => contentPluginMetadataRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<ContentPluginMetadata>(entry.Payload!), entry.Action, at, session),
                    contentPluginMetadataRepository.DeleteByIdAsync,
                    ReleaseAsync: contentPluginMetadataRepository.ReleaseByIdAsync),
                new EntityHandler(
                    ChangeEntityType.FilePluginMetadata,
                    ChangeEntityType.Owned[ChangeEntityType.FilePluginMetadata].OwnedTable,
                    (entry, at, session) => filePluginMetadataRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<FilePluginMetadata>(entry.Payload!), entry.Action, at, session),
                    filePluginMetadataRepository.DeleteByIdAsync),
                new EntityHandler(
                    ChangeEntityType.Passkey,
                    "passkey",
                    (entry, at, session) => passkeyRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<Passkey>(entry.Payload!), entry.Action, at, session),
                    passkeyRepository.DeleteByIdAsync),
                new EntityHandler(
                    ChangeEntityType.File,
                    "file",
                    (entry, at, session) => fileRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<FileRecord>(entry.Payload!), entry.Action, Guid.NewGuid(), at, session),
                    (id, at, session) => fileRepository.SoftDeleteAsync(id, Guid.NewGuid(), at, session)),
                new EntityHandler(
                    ChangeEntityType.Directory,
                    "directory",
                    (entry, at, session) => directoryRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<DirectoryRecord>(entry.Payload!), entry.Action, Guid.NewGuid(), at, session),
                    (id, at, session) => directoryRepository.TrashIfLiveAsync(id, Guid.NewGuid(), at, session)),
                new EntityHandler(
                    ChangeEntityType.Shelf,
                    "shelf",
                    (entry, at, session) => shelfRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<Shelf>(entry.Payload!), entry.Action, at, session),
                    async (id, at, session) =>
                    {
                        if (await shelfRepository.GetByIdAsync(id, session) == null)
                        {
                            return false;
                        }

                        await fileRepository.PurgeForShelfAsync(id, at, session);
                        await directoryRepository.PurgeForShelfAsync(id, at, session);
                        // Only the grants on it, which refer to it. A key left without any goes by its own
                        // removal entry, which the primary logged before the shelf's.
                        await grantRepository.DeleteForShelfAsync(id, at, session);
                        await keyRemoval.RemoveReplicatedGrantsOnShelfAsync(id, at, session);
                        await shelfRepository.DeleteAsync(id, at, session);
                        return true;
                    }),
                new EntityHandler(
                    ChangeEntityType.User,
                    "user_account",
                    (entry, at, session) => userRepository.UpsertReplicatedAsync(ChangePayload.Deserialize<User>(entry.Payload!), entry.Action, at, session),
                    userService.RemoveUserAsync,
                    RemovalEndsSessions: true),
            };
        }

        /// <summary>The entity types this server can apply, in the order a copy removes what it no longer holds.</summary>
        public IReadOnlyList<string> EntityTypes => _handlers.Select(handler => handler.EntityType).ToList();

        /// <summary>
        /// Applies <paramref name="entries"/> in order, noting in <paramref name="changes"/> what is left
        /// to do. <see cref="SettleAsync"/> follows the last call in the same transaction, whose commit a row
        /// without its owner would otherwise fail.
        /// </summary>
        /// <exception cref="ReplicaOutdatedException">An entry is of a type, or by an action, this build does not know.</exception>
        /// <exception cref="PrimaryFailedException">An entry's state is in a shape this build does not take, as from a primary on another version.</exception>
        public async Task ApplyAsync(IReadOnlyList<ChangeLogEntry> entries, IContentsSession session, AppliedChanges changes)
        {
            // An entry may name a parent that a later one in the same batch writes.
            await session.DeferForeignKeysAsync();

            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (ChangeLogEntry entry in entries)
            {
                EntityHandler handler = _handlers.FirstOrDefault(candidate => candidate.EntityType == entry.EntityType)
                    ?? throw new ReplicaOutdatedException($"a change to '{entry.EntityType}'");
                if (!ChangeAction.All.Contains(entry.Action))
                {
                    throw new ReplicaOutdatedException($"a change to '{entry.EntityType}' by '{entry.Action}'");
                }

                if (entry.Payload == null)
                {
                    await RemoveAsync(handler, entry.EntityId, entry.Action == ChangeAction.Released, now, session, changes);
                }
                else
                {
                    try
                    {
                        await handler.WriteAsync(entry, now, session);
                        changes.Written(handler.EntityType);
                    }
                    catch (JsonException exception)
                    {
                        // Worded for the replica's admins, who see this message: the same types in another
                        // shape mean the two servers run different versions.
                        throw new PrimaryFailedException(
                            $"The primary sent a {entry.EntityType} change in a shape this server does not take; run the same version of ByteShelf on both.",
                            exception);
                    }
                }
            }
        }

        /// <summary>
        /// Removes everything here that the whole copy in <c>replication_copy</c> does not hold, as removal
        /// entries for it would, noting in <paramref name="changes"/> what is left to do once it commits.
        /// </summary>
        public async Task RemoveMissingFromCopyAsync(IContentsSession session, AppliedChanges changes)
        {
            await session.DeferForeignKeysAsync();

            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (EntityHandler handler in _handlers)
            {
                // What goes with an owner the copy lacks leaves no removal and no entries on the primary: it
                // stays until this server purges the owner.
                EntityOwner? owner = ChangeEntityType.Owned.GetValueOrDefault(handler.EntityType);
                // A copy cannot tell what was released from what was purged; it takes what was read from contents as
                // released, so a trace of contents encrypted since goes from this log as soon as from the primary's.
                foreach (Guid id in await _copyRepository.GetIdsMissingAsync(handler.EntityType, handler.Table, owner, session))
                {
                    await RemoveAsync(handler, id, released: true, now, session, changes);
                }
            }
        }

        /// <summary>
        /// Settles what only a transaction's last entry decides; called once, after it. A row that goes with an
        /// owner may come before it (a copy holds each entity's newest entry, and the owner may have changed
        /// after the row), so only now is one without its owner passed over. A plugin's document about a file:
        /// its file was purged, or passed over itself for contents the primary no longer holds. Where the file comes
        /// later after all, as it was swapped or restored, the primary logs its documents again after it
        /// (<see cref="IFilePluginMetadataRepository.LogAgainAsync"/>).
        /// </summary>
        public async Task SettleAsync(IContentsSession session, AppliedChanges changes)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (EntityHandler handler in _handlers.Where(handler => changes.WrittenTypes.Contains(handler.EntityType)))
            {
                if (ChangeEntityType.Owned.TryGetValue(handler.EntityType, out EntityOwner? owner))
                {
                    foreach (Guid id in await OwnedRows.GetIdsWithoutOwnerAsync(owner, session))
                    {
                        await RemoveAsync(handler, id, released: false, now, session, changes);
                    }
                }
            }
        }

        /// <param name="released">Whether to log it as released (<see cref="ChangeAction.Released"/>), where its type is ever released.</param>
        private static async Task RemoveAsync(EntityHandler handler, Guid id, bool released, DateTimeOffset at, IContentsSession session, AppliedChanges changes)
        {
            Func<Guid, DateTimeOffset, IContentsSession, Task<bool>> remove = released && handler.ReleaseAsync != null ? handler.ReleaseAsync : handler.RemoveAsync;
            if (await remove(id, at, session) && handler.RemovalEndsSessions)
            {
                changes.UserRemoved(id);
            }
        }

        /// <param name="EntityType">The type as <see cref="ChangeEntityType"/> names it.</param>
        /// <param name="Table">The table its rows are in, for finding what a copy no longer holds.</param>
        /// <param name="WriteAsync">Writes an entry's state.</param>
        /// <param name="RemoveAsync">Removes one by id; false when there was none.</param>
        /// <param name="RemovalEndsSessions">Whether removing one ends the sessions of the user it is.</param>
        /// <param name="ReleaseAsync">Removes one by id, logged as released; null for a type that is never released.</param>
        private sealed record EntityHandler(
            string EntityType,
            string Table,
            Func<ChangeLogEntry, DateTimeOffset, IContentsSession, Task> WriteAsync,
            Func<Guid, DateTimeOffset, IContentsSession, Task<bool>> RemoveAsync,
            bool RemovalEndsSessions = false,
            Func<Guid, DateTimeOffset, IContentsSession, Task<bool>>? ReleaseAsync = null);
    }
}
