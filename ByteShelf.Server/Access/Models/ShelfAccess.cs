using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Access.Models
{
    /// <summary>A caller's access to one shelf: what it may do there, worked out once per request.</summary>
    public sealed class ShelfAccess
    {
        private readonly IReadOnlySet<ShelfOperation> _allowed;

        public ShelfAccess(Shelf shelf, ShelfCaller caller, IReadOnlySet<ShelfOperation> allowed)
        {
            Shelf = shelf;
            Caller = caller;
            _allowed = allowed;
        }

        public Shelf Shelf { get; }

        public ShelfCaller Caller { get; }

        public bool Allows(ShelfOperation operation)
        {
            return _allowed.Contains(operation);
        }

        /// <summary>Throws unless the caller may store a file here: one that may replace a file needs <see cref="ShelfOperation.Write"/>.</summary>
        /// <exception cref="PermissionDeniedException">It may not.</exception>
        public void RequireStore(bool overwrite)
        {
            Require(overwrite ? ShelfOperation.Write : ShelfOperation.Add);
        }

        /// <summary>
        /// Throws unless the caller may look at, continue, complete or abort <paramref name="upload"/>: the
        /// caller that started it may, anyone else needs <see cref="ShelfOperation.Write"/>. Its starter
        /// passed <see cref="RequireStore"/> when it started, and keeps that right: a key's permission
        /// never changes, and a user who may still add may also write.
        /// </summary>
        /// <exception cref="PermissionDeniedException">It may not.</exception>
        public void RequireUpload(UploadRecord upload)
        {
            if (upload.StartedBy != Caller.Id)
            {
                Require(ShelfOperation.Write);
            }
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
