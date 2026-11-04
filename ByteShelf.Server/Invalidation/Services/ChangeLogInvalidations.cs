using ByteShelf.Server.Access.Models;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Invalidation.Services
{
    /// <summary>
    /// Which cached views a change-log entry makes stale. The one home for that mapping: every
    /// change is in the log, so nothing that changes storage, users or access can forget to tell the
    /// web UI.
    /// </summary>
    public static class ChangeLogInvalidations
    {
        public static IEnumerable<InvalidationEvent> For(ChangeLogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            switch (entry.EntityType)
            {
                case ChangeEntityType.User:
                    yield return new InvalidationEvent(InvalidationResources.Users);
                    break;

                case ChangeEntityType.Shelf:
                    yield return new InvalidationEvent(InvalidationResources.Shelves);
                    yield return new InvalidationEvent(InvalidationResources.ShelfContents, entry.EntityId.ToString("D"));
                    yield return new InvalidationEvent(InvalidationResources.ShelfAccess, entry.EntityId.ToString("D"));
                    break;

                case ChangeEntityType.Directory:
                case ChangeEntityType.File:
                    // Usage on the shelf list changes with the contents. A purged row has no state
                    // left to name its shelf, so every shelf's contents are stale.
                    yield return new InvalidationEvent(InvalidationResources.Shelves);
                    yield return new InvalidationEvent(InvalidationResources.ShelfContents, ShelfIdOf(entry));
                    break;

                case ChangeEntityType.ShelfGrant:
                    // A grant decides which shelves its user sees.
                    yield return new InvalidationEvent(InvalidationResources.Shelves);
                    yield return new InvalidationEvent(InvalidationResources.ShelfAccess, ShelfIdOf(entry));
                    break;

                case ChangeEntityType.ShelfKey:
                    yield return new InvalidationEvent(InvalidationResources.ShelfAccess, ShelfIdOf(entry));
                    break;

                default:
                    // A user's logins (a password change) change nothing the web UI shows.
                    break;
            }
        }

        /// <summary>The shelf in the entry's state, or null once the entity is gone.</summary>
        private static string? ShelfIdOf(ChangeLogEntry entry)
        {
            if (entry.Payload == null)
            {
                return null;
            }

            Guid shelfId = entry.EntityType switch
            {
                ChangeEntityType.File => ChangePayload.Deserialize<FileRecord>(entry.Payload).ShelfId,
                ChangeEntityType.Directory => ChangePayload.Deserialize<DirectoryRecord>(entry.Payload).ShelfId,
                ChangeEntityType.ShelfGrant => ChangePayload.Deserialize<ShelfGrant>(entry.Payload).ShelfId,
                ChangeEntityType.ShelfKey => ChangePayload.Deserialize<ShelfKey>(entry.Payload).ShelfId,
                _ => throw new ArgumentException($"A {entry.EntityType} change has no shelf.", nameof(entry)),
            };

            return shelfId.ToString("D");
        }
    }
}
