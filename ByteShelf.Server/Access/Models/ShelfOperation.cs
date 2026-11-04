namespace ByteShelf.Server.Access.Models
{
    /// <summary>What an endpoint does to a shelf, which decides who may call it.</summary>
    public enum ShelfOperation
    {
        /// <summary>Lists, reads or downloads. Any access to the shelf allows it.</summary>
        Read,

        /// <summary>Stores something new without changing or removing anything: an add-only key may.</summary>
        Add,

        /// <summary>Overwrites, moves, deletes, restores or purges: read-write access only.</summary>
        Write,

        /// <summary>Changes the shelf itself, who may use it, or its keys: admins only.</summary>
        Administer,
    }
}
