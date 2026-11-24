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
        /// <param name="entry">The entry.</param>
        /// <param name="shelfOfFile">The shelf of the file the entry names by its id alone (<see cref="FileOf"/>); null when it names none or the file is gone.</param>
        public static IEnumerable<InvalidationEvent> For(ChangeLogEntry entry, Guid? shelfOfFile)
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

                case ChangeEntityType.ShelfKeyGrant:
                    // A shelf's key list shows each key's other shelves, so a grant on one shelf changes
                    // the list of every shelf the key works on: every shelf's access is stale.
                    yield return new InvalidationEvent(InvalidationResources.ShelfAccess);
                    break;

                case ChangeEntityType.FilePluginMetadata when entry.Payload == null:
                    // One that is gone names no file, so no shelf either. One whose state the log forgot for a
                    // newer says nothing: the newer follows.
                    if (entry.Action == ChangeAction.Purged)
                    {
                        yield return new InvalidationEvent(InvalidationResources.ShelfContents);
                    }

                    break;

                case ChangeEntityType.FilePluginMetadata:
                    // Listings carry fields of it, and search finds the file by them. A file gone since has
                    // made its shelf's contents stale itself.
                    if (shelfOfFile is Guid shelfId)
                    {
                        yield return new InvalidationEvent(InvalidationResources.ShelfContents, shelfId.ToString("D"));
                    }

                    break;

                default:
                    // A user's logins (a password change) change nothing the web UI shows, and a key is
                    // shown only through its grants, which are logged beside it when it comes and goes.
                    // Nor does what was read from contents (Invalidation.md).
                    break;
            }
        }

        /// <summary>The file whose shelf <see cref="For"/> needs to know of for <paramref name="entry"/>; null for most, which name their own or none.</summary>
        public static Guid? FileOf(ChangeLogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return entry is { EntityType: ChangeEntityType.FilePluginMetadata, Payload: not null }
                ? ChangePayload.Deserialize<DocumentOwner>(entry.Payload).FileId
                : null;
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
                _ => throw new ArgumentException($"A {entry.EntityType} change has no shelf.", nameof(entry)),
            };

            return shelfId.ToString("D");
        }

        /// <summary>The one part read here of a <see cref="Metadata.Models.FilePluginMetadata"/>, whose document may be large.</summary>
        private sealed record DocumentOwner(Guid FileId);
    }
}
