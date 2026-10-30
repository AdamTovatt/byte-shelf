using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Models
{
    /// <summary>
    /// Names a file or directory on a shelf either by path or by id, never both. The one way a request
    /// says which entry it means, so no endpoint exists twice.
    /// </summary>
    public sealed class EntryReference
    {
        private EntryReference(string? path, Guid? id)
        {
            Path = path;
            Id = id;
        }

        /// <summary>The normalized path, when the entry is named by path.</summary>
        public string? Path { get; }

        /// <summary>The id, when the entry is named by id.</summary>
        public Guid? Id { get; }

        public static EntryReference ForPath(string path)
        {
            return new EntryReference(ShelfPath.Normalize(path), null);
        }

        public static EntryReference ForId(Guid id)
        {
            return new EntryReference(null, id);
        }

        /// <summary>Builds a reference from the <c>path</c> and <c>id</c> query parameters, exactly one of which must be set.</summary>
        /// <exception cref="ArgumentException">Both or neither are set, or the path is not allowed.</exception>
        public static EntryReference From(string? path, Guid? id)
        {
            if (path != null && id != null)
            {
                throw new ArgumentException("Name the entry by path or by id, not both.");
            }

            if (id != null)
            {
                return ForId(id.Value);
            }

            if (path != null)
            {
                return ForPath(path);
            }

            throw new ArgumentException("Name the entry by path or by id.");
        }

        public override string ToString()
        {
            return Path ?? Id!.Value.ToString("D");
        }
    }
}
