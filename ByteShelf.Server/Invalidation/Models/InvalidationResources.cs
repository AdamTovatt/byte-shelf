namespace ByteShelf.Server.Invalidation.Models
{
    /// <summary>
    /// The resource vocabulary published through <see cref="Services.IInvalidationBroadcaster"/>.
    /// The web UI maps each constant to the queries it refreshes, so every constant here needs an
    /// entry there.
    /// </summary>
    public static class InvalidationResources
    {
        /// <summary>Users and their roles.</summary>
        public const string Users = "users";

        /// <summary>Which shelves exist, their settings and usage, and who reaches them.</summary>
        public const string Shelves = "shelves";

        /// <summary>A shelf's directories, files and trash; the metadata is the shelf's id, or absent for every shelf.</summary>
        public const string ShelfContents = "shelf-contents";

        /// <summary>A shelf's grants and keys; the metadata is the shelf's id.</summary>
        public const string ShelfAccess = "shelf-access";
    }
}
