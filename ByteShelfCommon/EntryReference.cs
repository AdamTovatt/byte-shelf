using System.Diagnostics.CodeAnalysis;

namespace ByteShelfCommon
{
    /// <summary>
    /// Names a file or directory on a shelf either by path or by id, never both: the one way a request
    /// says which entry it means. A path or an id converts to one implicitly, so a call can take either.
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

        /// <summary>The entry at <paramref name="path"/>, normalized by <see cref="ShelfPath.Normalize"/>.</summary>
        /// <exception cref="ArgumentException">The path is not allowed.</exception>
        public static EntryReference ForPath(string path)
        {
            return new EntryReference(ShelfPath.Normalize(path), null);
        }

        /// <summary>The entry with the id <paramref name="id"/>.</summary>
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

        /// <summary>The entry at <paramref name="path"/>, as <see cref="ForPath"/>.</summary>
        /// <exception cref="ArgumentException">The path is not allowed.</exception>
        [SuppressMessage("Usage", "CA2225", Justification = "ForPath is the named alternate.")]
        public static implicit operator EntryReference(string path)
        {
            return ForPath(path);
        }

        /// <summary>The entry with the id <paramref name="id"/>, as <see cref="ForId"/>.</summary>
        [SuppressMessage("Usage", "CA2225", Justification = "ForId is the named alternate.")]
        public static implicit operator EntryReference(Guid id)
        {
            return ForId(id);
        }

        /// <summary>The query string naming the entry: <c>path=…</c> or <c>id=…</c>, escaped.</summary>
        public string ToQuery()
        {
            return Path != null ? "path=" + Uri.EscapeDataString(Path) : "id=" + Id!.Value.ToString("D");
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Path ?? Id!.Value.ToString("D");
        }
    }
}
