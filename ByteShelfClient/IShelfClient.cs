using ByteShelfCommon;

namespace ByteShelfClient
{
    /// <summary>
    /// One shelf: its files and directories, its trash, and for admins its settings, grants and keys.
    /// Files and directories are named by an <see cref="EntryReference"/>, which a path
    /// (<c>"/invoices/march.pdf"</c>) or an id converts to. A path that breaks the rules of
    /// <see cref="ShelfPath"/>, or a content type that is not a media type, throws
    /// <see cref="ArgumentException"/> before anything is sent; every
    /// failure the server reports is a <see cref="ByteShelfException"/> with one of
    /// <see cref="ErrorCodes"/>.
    /// </summary>
    public interface IShelfClient
    {
        /// <summary>The shelf's name, as it was given to <see cref="IByteShelfClient.Shelf"/>.</summary>
        string Name { get; }

        /// <summary>The shelf with its quota and what it uses.</summary>
        Task<ShelfInfo> GetInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Renames the shelf and sets its quota (null for none); admins only. After a rename, reach the
        /// shelf through <see cref="IByteShelfClient.Shelf"/> with the new name.
        /// </summary>
        Task<ShelfInfo> UpdateAsync(string name, long? quotaBytes, CancellationToken cancellationToken = default);

        /// <summary>Deletes the shelf, which must hold nothing, the trash included; admins only.</summary>
        Task DeleteAsync(CancellationToken cancellationToken = default);

        /// <summary>The directories and files in a directory, by name; the root when none is given.</summary>
        Task<DirectoryListing> ListAsync(EntryReference? directory = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// One page of the live directories and files under a directory, at any depth or down to
        /// <see cref="TreeQuery.Depth"/>, by path; the whole shelf from its root when
        /// <paramref name="query"/> is null. A query <see cref="TreeQuery.Normalize"/> refuses throws
        /// <see cref="ArgumentException"/> before anything is sent.
        /// </summary>
        Task<DirectoryTree> ListTreeAsync(TreeQuery? query = null, CancellationToken cancellationToken = default);

        /// <summary>A file's record.</summary>
        Task<ShelfFile> GetFileAsync(EntryReference file, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores <paramref name="content"/>, read from its current position to its end, as the file at
        /// <paramref name="path"/>, creating any missing directory above it. Large content is sent in
        /// chunks; content of unknown length is first buffered to a temporary file. A file already at
        /// the path is refused unless <see cref="UploadOptions.Overwrite"/> is set, which moves it to the
        /// trash.
        /// </summary>
        Task<ShelfFile> UploadAsync(string path, Stream content, UploadOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a file's contents for reading. Dispose the stream when done. Reading it to the end
        /// checks the bytes against the file's SHA-256 and throws <see cref="IOException"/> if they differ.
        /// </summary>
        Task<Stream> OpenReadAsync(EntryReference file, CancellationToken cancellationToken = default);

        /// <summary>
        /// A link to a file's contents that works without signing in for <paramref name="lifetime"/>
        /// (<c>DownloadLink.Lifetime.Default</c> when null, whole minutes up to
        /// <c>DownloadLink.Lifetime.Max</c>), to share the file with someone who has no access.
        /// Making one is a read. A lifetime outside those bounds throws <see cref="ArgumentException"/>
        /// before anything is sent.
        /// </summary>
        Task<DownloadLink> CreateDownloadLinkAsync(EntryReference file, TimeSpan? lifetime = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// A link that stores one file at <paramref name="path"/> when anyone holding it <c>PUT</c>s the
        /// contents to it, without signing in, acting with this client's access (checked again then).
        /// Making one is a store, so a key that may only read cannot, and one that may overwrite needs
        /// write access. Options the rules refuse throw <see cref="ArgumentException"/> before anything
        /// is sent.
        /// </summary>
        Task<UploadLink> CreateUploadLinkAsync(string path, UploadLinkOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>Creates a directory and any missing directory above it; an existing one is answered as it is.</summary>
        Task<ShelfDirectory> CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Moves or renames a file to <paramref name="toPath"/>; the stored contents stay where they are.</summary>
        Task<ShelfFile> MoveFileAsync(EntryReference file, string toPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets a file's description, at most <see cref="FileDescription.MaxLength"/> characters; null or
        /// blank removes it.
        /// </summary>
        Task<ShelfFile> SetDescriptionAsync(EntryReference file, string? description, CancellationToken cancellationToken = default);

        /// <summary>
        /// One page of the live files that match <paramref name="search"/>, by path. A criterion
        /// <see cref="FileSearch.Normalize"/> refuses throws <see cref="ArgumentException"/> before anything is sent.
        /// </summary>
        Task<FileSearchResults> SearchAsync(FileSearch search, CancellationToken cancellationToken = default);

        /// <summary>Moves or renames a directory, with everything in it, to <paramref name="toPath"/>.</summary>
        Task<ShelfDirectory> MoveDirectoryAsync(EntryReference directory, string toPath, CancellationToken cancellationToken = default);

        /// <summary>Moves a file to the trash.</summary>
        Task<TrashEntry> DeleteFileAsync(EntryReference file, CancellationToken cancellationToken = default);

        /// <summary>Moves a directory and everything in it to the trash, as one deletion.</summary>
        Task<TrashEntry> DeleteDirectoryAsync(EntryReference directory, CancellationToken cancellationToken = default);

        /// <summary>Every deletion that can still be restored, newest first.</summary>
        Task<IReadOnlyList<TrashEntry>> GetTrashAsync(CancellationToken cancellationToken = default);

        /// <summary>Puts back what one deletion removed, where it was.</summary>
        Task RestoreAsync(Guid deletionId, CancellationToken cancellationToken = default);

        /// <summary>Removes everything in the trash for good.</summary>
        Task EmptyTrashAsync(CancellationToken cancellationToken = default);

        /// <summary>Every user granted access to the shelf; admins only.</summary>
        Task<IReadOnlyList<ShelfAccessGrant>> GetGrantsAsync(CancellationToken cancellationToken = default);

        /// <summary>Grants a user access to the shelf, or changes the access they have; admins only.</summary>
        Task<ShelfAccessGrant> SetGrantAsync(Guid userId, ShelfAccessPermission permission, CancellationToken cancellationToken = default);

        /// <summary>Takes a user's access to the shelf away; admins only.</summary>
        Task RemoveGrantAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>The shelf's keys, never their secrets; admins only.</summary>
        Task<IReadOnlyList<ShelfKeyInfo>> GetKeysAsync(CancellationToken cancellationToken = default);

        /// <summary>Creates a key for a program, answering its secret, the one time it is shown; admins only.</summary>
        Task<CreatedShelfKey> CreateKeyAsync(string name, ShelfKeyPermission permission, CancellationToken cancellationToken = default);

        /// <summary>Deletes a key; requests with it fail from then on. Admins only.</summary>
        Task DeleteKeyAsync(Guid keyId, CancellationToken cancellationToken = default);
    }
}
