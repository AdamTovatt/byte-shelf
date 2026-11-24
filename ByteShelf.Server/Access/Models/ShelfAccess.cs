using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Access.Models
{
    /// <summary>
    /// A caller's access to one shelf: what it may do there, worked out once per request from the grant
    /// that gives it (<see cref="Services.ShelfAccessResolver"/> finds which), or from being an admin.
    /// </summary>
    public sealed class ShelfAccess
    {
        private static readonly IReadOnlySet<ShelfOperation> ReadOnly = new HashSet<ShelfOperation> { ShelfOperation.Read };
        private static readonly IReadOnlySet<ShelfOperation> AddOnly = new HashSet<ShelfOperation> { ShelfOperation.Read, ShelfOperation.Add };
        private static readonly IReadOnlySet<ShelfOperation> ReadWrite = new HashSet<ShelfOperation> { ShelfOperation.Read, ShelfOperation.Add, ShelfOperation.Write };
        private static readonly IReadOnlySet<ShelfOperation> Everything = new HashSet<ShelfOperation>(Enum.GetValues<ShelfOperation>());

        private readonly IReadOnlySet<ShelfOperation> _allowed;

        private ShelfAccess(Shelf shelf, ShelfCaller caller, IReadOnlySet<ShelfOperation> allowed)
        {
            Shelf = shelf;
            Caller = caller;
            _allowed = allowed;
        }

        /// <summary>A key's access by its grant's permission on the shelf.</summary>
        public static ShelfAccess ForKey(Shelf shelf, ShelfCaller caller, ShelfPermission permission)
        {
            IReadOnlySet<ShelfOperation> allowed = permission switch
            {
                ShelfPermission.Read => ReadOnly,
                ShelfPermission.AddOnly => AddOnly,
                ShelfPermission.ReadWrite => ReadWrite,
                _ => throw new InvalidOperationException($"No access is defined for a {permission} key."),
            };

            return new ShelfAccess(shelf, caller, allowed);
        }

        /// <summary>A user's (not an admin's) access by their grant's permission on the shelf.</summary>
        public static ShelfAccess ForUser(Shelf shelf, ShelfCaller caller, ShelfAccessPermission permission)
        {
            IReadOnlySet<ShelfOperation> allowed = permission switch
            {
                ShelfAccessPermission.Read => ReadOnly,
                ShelfAccessPermission.ReadWrite => ReadWrite,
                _ => throw new InvalidOperationException($"No access is defined for {permission} access."),
            };

            return new ShelfAccess(shelf, caller, allowed);
        }

        /// <summary>An admin's access, to everything on every shelf.</summary>
        public static ShelfAccess ForAdmin(Shelf shelf, ShelfCaller caller)
        {
            return new ShelfAccess(shelf, caller, Everything);
        }

        public Shelf Shelf { get; }

        public ShelfCaller Caller { get; }

        /// <summary>What the caller may do with the shelf's files, to tell it (as <c>/mcp</c> tells an agent); whether it may is <see cref="Allows"/>'s to say. A replica allows less (<c>ReplicationState</c>).</summary>
        public ShelfPermission Permission => Allows(ShelfOperation.Write) ? ShelfPermission.ReadWrite
            : Allows(ShelfOperation.Add) ? ShelfPermission.AddOnly
            : ShelfPermission.Read;

        public bool Allows(ShelfOperation operation)
        {
            return _allowed.Contains(operation);
        }

        /// <summary>Whether the caller may store a file here: one that may replace a file needs <see cref="ShelfOperation.Write"/>.</summary>
        public bool AllowsStore(bool overwrite)
        {
            return Allows(StoreOperation(overwrite));
        }

        /// <summary>Throws unless <see cref="AllowsStore"/>.</summary>
        /// <exception cref="PermissionDeniedException">It may not.</exception>
        public void RequireStore(bool overwrite)
        {
            Require(StoreOperation(overwrite));
        }

        private static ShelfOperation StoreOperation(bool overwrite)
        {
            return overwrite ? ShelfOperation.Write : ShelfOperation.Add;
        }

        /// <summary>
        /// Throws unless the caller may look at, continue, complete or abort <paramref name="upload"/>: the
        /// caller that started it may while it may still store what the upload stores (a key's or user's
        /// permission can change meanwhile), anyone else needs <see cref="ShelfOperation.Write"/>.
        /// </summary>
        /// <exception cref="PermissionDeniedException">It may not.</exception>
        public void RequireUpload(UploadRecord upload)
        {
            if (upload.StartedBy != Caller.Id)
            {
                Require(ShelfOperation.Write);
                return;
            }

            Require(upload.Target switch
            {
                StoreAtPath store => StoreOperation(store.Overwrite),
                ContentSwap => ShelfOperation.Write,
                _ => throw new InvalidOperationException($"No operation is defined for an upload to {upload.Target.GetType().Name}."),
            });
        }

        /// <summary>Throws unless the caller may do <paramref name="operation"/> here.</summary>
        /// <exception cref="PermissionDeniedException">It may not.</exception>
        public void Require(ShelfOperation operation)
        {
            if (!Allows(operation))
            {
                throw new PermissionDeniedException(operation switch
                {
                    ShelfOperation.Read => $"You may not read the shelf '{Shelf.Name}'.",
                    ShelfOperation.Add => $"You may not store files on the shelf '{Shelf.Name}'.",
                    ShelfOperation.Write => $"You may not overwrite, move or delete anything on the shelf '{Shelf.Name}', or touch someone else's upload.",
                    _ => $"Only an admin may change the shelf '{Shelf.Name}', its access or its keys.",
                });
            }
        }
    }
}
