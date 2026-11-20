using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Access.Models
{
    /// <summary>A caller's access to one shelf: what it may do there, worked out once per request.</summary>
    public sealed class ShelfAccess
    {
        private readonly IReadOnlySet<ShelfOperation> _allowed;

        /// <param name="keyPermission">A key's permission on the shelf, which <paramref name="allowed"/> was worked out from; null for a user.</param>
        public ShelfAccess(Shelf shelf, ShelfCaller caller, IReadOnlySet<ShelfOperation> allowed, ShelfKeyPermission? keyPermission = null)
        {
            Shelf = shelf;
            Caller = caller;
            _allowed = allowed;
            KeyPermission = keyPermission;
        }

        public Shelf Shelf { get; }

        public ShelfCaller Caller { get; }

        /// <summary>A key's permission on the shelf, to name it (as <c>/mcp</c> tells an agent); null for a user. What it allows is <see cref="Allows"/>'s to say.</summary>
        public ShelfKeyPermission? KeyPermission { get; }

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
