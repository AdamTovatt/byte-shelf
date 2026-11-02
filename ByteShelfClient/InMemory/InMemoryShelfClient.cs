using System.Security.Cryptography;
using ByteShelfCommon;
using static ByteShelfClient.InMemory.InMemoryShelf;

namespace ByteShelfClient.InMemory
{
    /// <summary><see cref="IShelfClient"/> over an <see cref="InMemoryStore"/>, for <see cref="InMemoryByteShelfClient.Shelf"/>.</summary>
    internal sealed class InMemoryShelfClient : IShelfClient
    {
        private readonly InMemoryStore _store;

        public InMemoryShelfClient(InMemoryStore store, string name)
        {
            _store = store;
            Name = name;
        }

        public string Name { get; }

        public Task<ShelfInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf => shelf.ToInfo());
        }

        public Task<ShelfInfo> UpdateAsync(string name, long? quotaBytes, CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf =>
            {
                InMemoryErrors.Checked(() => ShelfSettings.Validate(name, quotaBytes));
                if (_store.Shelves.Any(other => other != shelf && other.Name == name))
                {
                    throw InMemoryErrors.Conflict($"A shelf called '{name}' already exists.");
                }

                shelf.Name = name;
                shelf.QuotaBytes = quotaBytes;
                return shelf.ToInfo();
            });
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf =>
            {
                if (shelf.Files.Count > 0 || shelf.Directories.Count > 1)
                {
                    throw InMemoryErrors.Conflict($"The shelf '{shelf.Name}' still holds files or directories, the trash included; delete them and empty the trash first.");
                }

                _store.Shelves.Remove(shelf);
            });
        }

        public Task<DirectoryListing> ListAsync(EntryReference? directory = null, CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf =>
            {
                DirectoryNode listed = directory == null ? shelf.Root : shelf.GetDirectory(directory);
                return new DirectoryListing
                {
                    Directory = shelf.ToShelfDirectory(listed),
                    Directories = shelf.LiveChildDirectories(listed.Id).OrderBy(child => child.Name, StoredOrder.Instance).Select(shelf.ToListedDirectory).ToList(),
                    Files = shelf.LiveFilesIn(listed.Id).OrderBy(file => file.Name, StoredOrder.Instance).Select(shelf.ToShelfFile).ToList(),
                    EncryptedBy = shelf.ToEncryptedFolder(shelf.EncryptedFolderOf(listed.Id)),
                };
            });
        }

        public Task<DirectoryTree> ListTreeAsync(TreeQuery? query = null, CancellationToken cancellationToken = default)
        {
            TreeQuery normalized = (query ?? new TreeQuery()).Normalize();

            return Run(cancellationToken, shelf =>
            {
                DirectoryNode under = shelf.GetDirectory(normalized.Under!);

                // A file is one level below the directory it is in, so its directories stop a level higher.
                IEnumerable<DirectoryNode> directories = shelf.LiveSubtree(under, normalized.Depth).Skip(1);
                IEnumerable<FileNode> files = shelf.LiveSubtree(under, normalized.Depth - 1).SelectMany(directory => shelf.LiveFilesIn(directory.Id));
                return DirectoryTree.Page(
                    shelf.ToShelfDirectory(under),
                    directories.Select(shelf.ToShelfDirectory).Where(entry => IsAfter(entry.Path, normalized.After)),
                    files.Select(shelf.ToShelfFile).Where(entry => IsAfter(entry.Path, normalized.After)),
                    normalized.Limit!.Value);
            });
        }

        public Task<ShelfFile> GetFileAsync(EntryReference file, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            return Run(cancellationToken, shelf => shelf.ToShelfFile(shelf.GetFile(file)));
        }

        public async Task<ShelfFile> UploadAsync(string path, Stream content, UploadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(content);
            NormalizedUpload upload = NormalizedUpload.Of(path, options);
            byte[] bytes = await ReadAllAsync(content, cancellationToken).ConfigureAwait(false);
            return await Run(cancellationToken, shelf => Store(shelf, upload, bytes)).ConfigureAwait(false);
        }

        public async Task<ShelfFile> SwapContentAsync(ShelfFile file, Stream content, bool encrypted, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(content);
            byte[] bytes = await ReadAllAsync(content, cancellationToken).ConfigureAwait(false);

            return await Run(cancellationToken, shelf =>
            {
                FileNode swapped = shelf.GetFile(EntryReference.ForId(file.Id));
                string path = shelf.ToShelfFile(swapped).Path;
                if (swapped.Sha256 != file.Sha256)
                {
                    throw InMemoryErrors.Conflict($"'{path}' has changed since its contents were read: read it again, then encrypt or decrypt it.");
                }

                shelf.RequireSwap(swapped, path, encrypted);
                InMemoryShelf.RequireAgeFile(encrypted, bytes, path: null);

                // Only what it adds, since the old contents go.
                shelf.RequireRoomFor(Math.Max(0, bytes.LongLength - swapped.Content.LongLength));

                // In place, never to the trash: the old contents are gone, as on the server.
                swapped.Content = bytes;
                swapped.Sha256 = Sha256Of(bytes);
                swapped.Encrypted = encrypted;
                swapped.UpdatedAt = _store.Now;
                return shelf.ToShelfFile(swapped);
            }).ConfigureAwait(false);
        }

        public Task<ShelfFile> SetDescriptionAsync(EntryReference file, string? description, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            string? normalized = FileDescription.Normalize(description);

            return Run(cancellationToken, shelf =>
            {
                FileNode described = shelf.GetFile(file);
                if (described.Description != normalized)
                {
                    described.Description = normalized;
                    described.UpdatedAt = _store.Now;
                }

                return shelf.ToShelfFile(described);
            });
        }

        public Task<FileSearchResults> SearchAsync(FileSearch search, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(search);
            FileSearch normalized = search.Normalize();

            return Run(cancellationToken, shelf =>
            {
                DirectoryNode under = shelf.GetDirectory(normalized.Under!);
                HashSet<Guid> directories = shelf.LiveSubtree(under).Select(directory => directory.Id).ToHashSet();

                // The one-more-than-a-page paging here is also in DirectoryTree.Page and the server's
                // FileService; left as is, since sharing it would reshape working code for a few lines.
                List<ShelfFile> found = shelf.Files
                    .Where(file => file.IsLive && directories.Contains(file.DirectoryId) && Matches(normalized, file))
                    .Select(shelf.ToShelfFile)
                    .Where(file => IsAfter(file.Path, normalized.After))
                    .OrderBy(file => file.Path, StoredOrder.Instance)
                    .Take(normalized.Limit!.Value + 1)
                    .ToList();

                List<ShelfFile> page = found.Take(normalized.Limit.Value).ToList();
                return new FileSearchResults { Files = page, NextCursor = found.Count > page.Count ? page[^1].Path : null };
            });
        }

        public Task<Stream> OpenReadAsync(EntryReference file, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            return Run<Stream>(cancellationToken, shelf => new MemoryStream(shelf.GetFile(file).Content, writable: false));
        }

        /// <summary>
        /// A link under the reserved <c>.invalid</c> domain: nothing serves it, but it names the file and
        /// runs out as a server's link would.
        /// </summary>
        public Task<DownloadLink> CreateDownloadLinkAsync(EntryReference file, TimeSpan? lifetime = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            TimeSpan normalized = DownloadLink.Lifetime.Normalize(lifetime);
            return Run(cancellationToken, shelf =>
            {
                FileNode linked = shelf.GetFile(file);
                if (linked.Encrypted)
                {
                    throw InMemoryErrors.Conflict($"'{shelf.ToShelfFile(linked).Path}' is encrypted with a password, so it is opened in a client that has the password, never by a link.");
                }

                return new DownloadLink
                {
                    Url = $"https://byteshelf.invalid/api/downloads/{linked.Id:N}{Guid.NewGuid():N}",
                    ExpiresAt = _store.Now + normalized,
                };
            });
        }

        public Task<UploadLink> CreateUploadLinkAsync(string path, UploadLinkOptions? options = null, CancellationToken cancellationToken = default)
        {
            NormalizedUploadLinkRequest request = (options ?? new UploadLinkOptions()).Normalize(path);
            return Run(cancellationToken, shelf =>
            {
                shelf.RequireLinkTarget(request.Path);
                return new UploadLink
                {
                    Url = $"https://byteshelf.invalid/api/upload-links/{Guid.NewGuid():N}",
                    ExpiresAt = _store.Now + request.Lifetime,
                    Path = request.Path,
                    Overwrite = request.Overwrite,
                };
            });
        }

        public Task<PathEncryption> GetEncryptionAsync(string path, CancellationToken cancellationToken = default)
        {
            string normalizedPath = ShelfPath.Normalize(path);
            return Run(cancellationToken, shelf => new PathEncryption { EncryptedBy = shelf.ToEncryptedFolder(shelf.EncryptedFolderFor(normalizedPath)) });
        }

        public Task<ShelfDirectory> EncryptDirectoryAsync(EntryReference directory, string check, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(directory);
            return Run(cancellationToken, shelf =>
            {
                // As the server's ShelfEncryption.EncryptAsync.
                byte[] bytes = InMemoryErrors.Checked(() => FileEncryption.DecodeCheck(check));
                DirectoryNode encrypted = shelf.GetDirectory(directory);
                string path = shelf.PathOf(encrypted);
                if (encrypted.EncryptionCheck != null)
                {
                    throw InMemoryErrors.Conflict($"'{path}' is encrypted already. Remove its encryption first to give it another password.");
                }

                if (encrypted.ParentId is Guid parentId && shelf.EncryptedFolderOf(parentId) is DirectoryNode above)
                {
                    throw InMemoryErrors.Conflict($"'{path}' is inside the encrypted folder '{shelf.PathOf(above)}', whose password covers it.");
                }

                if (shelf.Descendants(encrypted).Any(descendant => descendant.IsLive && descendant.EncryptionCheck != null))
                {
                    throw InMemoryErrors.Conflict($"A folder under '{path}' is encrypted with its own password. Remove its encryption first.");
                }

                encrypted.EncryptionCheck = bytes;
                return shelf.ToShelfDirectory(encrypted);
            });
        }

        public Task<ShelfDirectory> RemoveDirectoryEncryptionAsync(EntryReference directory, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(directory);
            return Run(cancellationToken, shelf =>
            {
                DirectoryNode plain = shelf.GetDirectory(directory);
                if (plain.EncryptionCheck == null)
                {
                    throw InMemoryErrors.Conflict($"'{shelf.PathOf(plain)}' is not an encrypted folder.");
                }

                plain.EncryptionCheck = null;
                return shelf.ToShelfDirectory(plain);
            });
        }

        public Task<ShelfDirectory> CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            string normalizedPath = ShelfPath.Normalize(path);
            return Run(cancellationToken, shelf => shelf.ToShelfDirectory(shelf.EnsureDirectory(normalizedPath, _store.Now)));
        }

        public Task<ShelfFile> MoveFileAsync(EntryReference file, string toPath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            string normalizedPath = ShelfPath.Normalize(toPath);

            return Run(cancellationToken, shelf =>
            {
                FileNode moved = shelf.GetFile(file);
                if (shelf.ToShelfFile(moved).Path == normalizedPath)
                {
                    return shelf.ToShelfFile(moved);
                }

                DateTimeOffset now = _store.Now;
                (DirectoryNode parent, string name) = shelf.PrepareDestination(normalizedPath, now);
                shelf.RequireMove(movedDirectoryId: null, moved.DirectoryId, parent.Id, shelf.ToShelfFile(moved).Path);
                moved.DirectoryId = parent.Id;
                moved.Name = name;
                moved.UpdatedAt = now;
                return shelf.ToShelfFile(moved);
            });
        }

        public Task<ShelfDirectory> MoveDirectoryAsync(EntryReference directory, string toPath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(directory);
            string normalizedPath = ShelfPath.Normalize(toPath);

            return Run(cancellationToken, shelf =>
            {
                DirectoryNode moved = shelf.GetDirectory(directory);
                if (moved == shelf.Root)
                {
                    throw InMemoryErrors.Invalid("The root directory cannot be moved.");
                }

                string currentPath = shelf.PathOf(moved);
                if (currentPath == normalizedPath)
                {
                    return shelf.ToShelfDirectory(moved);
                }

                if (normalizedPath.StartsWith(currentPath + ShelfPath.Separator, StringComparison.Ordinal))
                {
                    throw InMemoryErrors.Invalid($"'{currentPath}' cannot be moved into itself.");
                }

                EnsurePathsFit(shelf, moved, normalizedPath);
                (DirectoryNode parent, string name) = shelf.PrepareDestination(normalizedPath, _store.Now);
                shelf.RequireMove(moved.Id, moved.ParentId!.Value, parent.Id, currentPath);
                moved.ParentId = parent.Id;
                moved.Name = name;
                return shelf.ToShelfDirectory(moved);
            });
        }

        public Task<TrashEntry> DeleteFileAsync(EntryReference file, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            return Run(cancellationToken, shelf =>
            {
                FileNode deleted = shelf.GetFile(file);
                Guid deletionId = Guid.NewGuid();
                Trash(deleted, deletionId, _store.Now);
                return ToTrashEntry(shelf, deletionId);
            });
        }

        public Task<TrashEntry> DeleteDirectoryAsync(EntryReference directory, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(directory);
            return Run(cancellationToken, shelf =>
            {
                DirectoryNode deleted = shelf.GetDirectory(directory);
                if (deleted == shelf.Root)
                {
                    throw InMemoryErrors.Invalid("The root directory cannot be deleted; delete what is in it.");
                }

                // Only live entries are taken: anything under it already in the trash stays in its own
                // deletion, so restoring either brings back exactly what it took.
                Guid deletionId = Guid.NewGuid();
                DateTimeOffset now = _store.Now;
                List<DirectoryNode> taken = new[] { deleted }.Concat(shelf.Descendants(deleted)).Where(node => node.IsLive).ToList();
                foreach (DirectoryNode node in taken)
                {
                    node.DeletedAt = now;
                    node.DeletionId = deletionId;
                }

                foreach (FileNode file in taken.SelectMany(node => shelf.LiveFilesIn(node.Id)).ToList())
                {
                    Trash(file, deletionId, now);
                }

                return ToTrashEntry(shelf, deletionId);
            });
        }

        public Task<IReadOnlyList<TrashEntry>> GetTrashAsync(CancellationToken cancellationToken = default)
        {
            return Run<IReadOnlyList<TrashEntry>>(cancellationToken, shelf => DeletionIds(shelf)
                .Select(deletionId => ToTrashEntry(shelf, deletionId))
                .OrderByDescending(entry => entry.DeletedAt)
                .ThenBy(entry => ShelfPath.GetName(entry.Path), StringComparer.Ordinal)
                .ToList());
        }

        public Task RestoreAsync(Guid deletionId, CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf =>
            {
                if (!DeletionIds(shelf).Contains(deletionId))
                {
                    throw InMemoryErrors.NotFound($"There is no deletion {deletionId} to restore on this shelf.");
                }

                (Guid containerId, string name) = TopOf(shelf, deletionId);
                DirectoryNode? container = shelf.Directories.FirstOrDefault(directory => directory.Id == containerId);
                string containerPath = shelf.PathOf(containerId);
                string path = ShelfPath.Combine(containerPath, name);
                if (container == null || !container.IsLive)
                {
                    throw InMemoryErrors.Conflict($"'{containerPath}' is in the trash itself; restore it before '{path}'.");
                }

                shelf.EnsureNameAvailable(container.Id, name, path, mayReplaceFile: false);

                // As the server's ShelfEncryption.RequireRestoreAsync.
                bool holdsPlainFile = shelf.Files.Any(file => file.DeletionId == deletionId && !file.Encrypted);
                bool holdsEncryptedDirectory = shelf.Directories.Any(directory => directory.DeletionId == deletionId && directory.EncryptionCheck != null);
                if (shelf.EncryptedFolderOf(container.Id) is DirectoryNode folder && (holdsPlainFile || holdsEncryptedDirectory))
                {
                    throw InMemoryErrors.Conflict(
                        $"'{path}' cannot be restored: the folder it was in is now under the encrypted folder '{shelf.PathOf(folder)}', and what it holds is not encrypted with that folder's password.");
                }

                DateTimeOffset now = _store.Now;
                foreach (DirectoryNode directory in shelf.Directories.Where(directory => directory.DeletionId == deletionId))
                {
                    directory.DeletedAt = null;
                    directory.DeletionId = null;
                }

                foreach (FileNode file in shelf.Files.Where(file => file.DeletionId == deletionId))
                {
                    file.DeletedAt = null;
                    file.DeletionId = null;
                    file.UpdatedAt = now;
                }
            });
        }

        public Task EmptyTrashAsync(CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf =>
            {
                shelf.Files.RemoveAll(file => !file.IsLive);
                shelf.Directories.RemoveAll(directory => !directory.IsLive);
            });
        }

        public Task<IReadOnlyList<ShelfAccessGrant>> GetGrantsAsync(CancellationToken cancellationToken = default)
        {
            return Run<IReadOnlyList<ShelfAccessGrant>>(cancellationToken, shelf => shelf.Grants.ToList());
        }

        public Task<ShelfAccessGrant> SetGrantAsync(Guid userId, ShelfAccessPermission permission, CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf =>
            {
                if (!Enum.IsDefined(permission))
                {
                    throw InMemoryErrors.Invalid($"'{permission}' is not a shelf access permission.");
                }

                InMemoryStore.UserRow user = _store.GetUser(userId);
                ShelfAccessGrant? existing = shelf.Grants.FirstOrDefault(grant => grant.UserId == userId);
                if (existing?.Permission == permission)
                {
                    return existing;
                }

                DateTimeOffset now = _store.Now;
                ShelfAccessGrant granted = new ShelfAccessGrant
                {
                    ShelfId = shelf.Id,
                    UserId = userId,
                    Email = user.Email,
                    DisplayName = user.DisplayName,
                    Permission = permission,
                    CreatedAt = existing?.CreatedAt ?? now,
                    UpdatedAt = now,
                };

                int index = existing == null ? -1 : shelf.Grants.IndexOf(existing);
                if (index < 0)
                {
                    shelf.Grants.Add(granted);
                }
                else
                {
                    shelf.Grants[index] = granted;
                }

                return granted;
            });
        }

        public Task RemoveGrantAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf =>
            {
                if (shelf.Grants.RemoveAll(grant => grant.UserId == userId) == 0)
                {
                    throw InMemoryErrors.NotFound($"User {userId} has no access to the shelf '{shelf.Name}'.");
                }
            });
        }

        public Task<IReadOnlyList<ShelfKeyInfo>> GetKeysAsync(CancellationToken cancellationToken = default)
        {
            return Run<IReadOnlyList<ShelfKeyInfo>>(cancellationToken, shelf => shelf.Keys.ToList());
        }

        public Task<CreatedShelfKey> CreateKeyAsync(string name, ShelfKeyPermission permission, CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf =>
            {
                string trimmedName = InMemoryErrors.Checked(() => DisplayText.Validate(name, ShelfKeys.MaxNameLength, "A key's name"));
                if (!Enum.IsDefined(permission))
                {
                    throw InMemoryErrors.Invalid($"'{permission}' is not a shelf key permission.");
                }

                ShelfKeyInfo key = new ShelfKeyInfo { Id = Guid.NewGuid(), ShelfId = shelf.Id, Name = trimmedName, Permission = permission, CreatedAt = _store.Now };
                shelf.Keys.Add(key);

                string secret = ShelfKeys.SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                return new CreatedShelfKey { Key = key, Secret = secret };
            });
        }

        public Task DeleteKeyAsync(Guid keyId, CancellationToken cancellationToken = default)
        {
            return Run(cancellationToken, shelf =>
            {
                if (shelf.Keys.RemoveAll(key => key.Id == keyId) == 0)
                {
                    throw InMemoryErrors.NotFound($"The shelf '{shelf.Name}' has no key {keyId}.");
                }
            });
        }

        /// <summary>Runs <paramref name="action"/> on this shelf as <see cref="InMemoryStore.RunAsync{T}"/> does.</summary>
        private Task<T> Run<T>(CancellationToken cancellationToken, Func<InMemoryShelf, T> action)
        {
            return _store.RunAsync(() => Refusable(_store.GetShelf(Name), action), cancellationToken);
        }

        /// <inheritdoc cref="Run{T}(CancellationToken, Func{InMemoryShelf, T})"/>
        private Task Run(CancellationToken cancellationToken, Action<InMemoryShelf> action)
        {
            return _store.RunAsync(
                () => Refusable(_store.GetShelf(Name), shelf =>
                {
                    action(shelf);
                    return true;
                }),
                cancellationToken);
        }

        /// <summary>A stream's whole contents, read before the store is touched, as the server stages a body before it stores it.</summary>
        private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
        {
            using MemoryStream buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }

        /// <summary>
        /// Runs <paramref name="action"/>, and when it is refused removes the directories it created on the
        /// way, as the server's transaction rolls them back: a move or store creates its destination's
        /// directories before every rule about it is checked. Every other change an operation makes comes
        /// after its checks.
        /// </summary>
        private static T Refusable<T>(InMemoryShelf shelf, Func<InMemoryShelf, T> action)
        {
            int directories = shelf.Directories.Count;
            try
            {
                return action(shelf);
            }
            catch
            {
                shelf.Directories.RemoveRange(directories, shelf.Directories.Count - directories);
                throw;
            }
        }

        /// <summary>Stores a file as an upload does on the server: refuses up front, then replaces and creates what it needs.</summary>
        private ShelfFile Store(InMemoryShelf shelf, NormalizedUpload upload, byte[] bytes)
        {
            string normalizedPath = upload.Path;
            bool overwrite = upload.Overwrite;
            if (normalizedPath == ShelfPath.Root)
            {
                throw InMemoryErrors.Invalid("A file cannot be stored at the root itself; give it a name.");
            }

            shelf.RequireStore(normalizedPath, upload.Encrypted);
            InMemoryShelf.RequireAgeFile(upload.Encrypted, bytes, normalizedPath);

            DirectoryNode? existingDirectory = shelf.FindDirectory(ShelfPath.GetParent(normalizedPath));
            if (existingDirectory != null)
            {
                shelf.EnsureNameAvailable(existingDirectory.Id, ShelfPath.GetName(normalizedPath), normalizedPath, overwrite);
            }

            shelf.RequireRoomFor(bytes.LongLength);

            DateTimeOffset now = _store.Now;
            DirectoryNode directory = shelf.EnsureDirectory(ShelfPath.GetParent(normalizedPath), now);
            string name = ShelfPath.GetName(normalizedPath);
            FileNode? replaced = shelf.LiveFilesIn(directory.Id).FirstOrDefault(file => file.Name == name);
            if (replaced != null)
            {
                Trash(replaced, Guid.NewGuid(), now);
            }

            FileNode stored = new FileNode
            {
                Id = Guid.NewGuid(),
                DirectoryId = directory.Id,
                Name = name,
                ContentType = upload.ContentType,
                Content = bytes,
                Sha256 = Sha256Of(bytes),
                CreatedAt = now,
                UpdatedAt = now,
                Description = upload.Description ?? replaced?.Description,
                Encrypted = upload.Encrypted,
            };

            shelf.Files.Add(stored);
            return shelf.ToShelfFile(stored);
        }

        /// <summary>Whether <paramref name="path"/> belongs on a page after <paramref name="cursor"/>: the first page takes every path.</summary>
        private static bool IsAfter(string path, string? cursor)
        {
            return cursor == null || StoredOrder.Instance.Compare(path, cursor) > 0;
        }

        /// <summary>Whether a file meets every criterion of a normalized search but where it is, as the server's query decides.</summary>
        private static bool Matches(FileSearch search, FileNode file)
        {
            return (search.Text == null
                    || FileSearch.Fold(file.Name).Contains(search.Text, StringComparison.Ordinal)
                    || (file.Description != null && FileSearch.Fold(file.Description).Contains(search.Text, StringComparison.Ordinal)))
                && search.MatchesContentType(file.ContentType)
                && (search.MinSizeBytes == null || file.Content.LongLength >= search.MinSizeBytes)
                && (search.MaxSizeBytes == null || file.Content.LongLength <= search.MaxSizeBytes)
                && (search.CreatedFrom == null || file.CreatedAt >= search.CreatedFrom)
                && (search.CreatedBefore == null || file.CreatedAt < search.CreatedBefore)
                && (search.UpdatedFrom == null || file.UpdatedAt >= search.UpdatedFrom)
                && (search.UpdatedBefore == null || file.UpdatedAt < search.UpdatedBefore)
                && (search.Sha256 == null || file.Sha256 == search.Sha256);
        }

        private static void Trash(FileNode file, Guid deletionId, DateTimeOffset at)
        {
            file.DeletedAt = at;
            file.DeletionId = deletionId;
        }

        /// <summary>Refuses a move that would leave a path under the directory longer than <see cref="ShelfPath.MaxPathLength"/>.</summary>
        private static void EnsurePathsFit(InMemoryShelf shelf, DirectoryNode moved, string normalizedPath)
        {
            int prefix = shelf.PathOf(moved).Length;
            List<DirectoryNode> subtree = shelf.Descendants(moved).Prepend(moved).ToList();
            HashSet<Guid> subtreeIds = subtree.Select(node => node.Id).ToHashSet();
            IEnumerable<string> underneath = subtree.Select(shelf.PathOf)
                .Concat(shelf.Files.Where(file => subtreeIds.Contains(file.DirectoryId))
                    .Select(file => ShelfPath.Combine(shelf.PathOf(file.DirectoryId), file.Name)));

            if (underneath.Any(path => normalizedPath.Length + path.Length - prefix > ShelfPath.MaxPathLength))
            {
                throw InMemoryErrors.Invalid(
                    $"Moving to '{normalizedPath}' would make a path under it longer than {ShelfPath.MaxPathLength} characters.");
            }
        }

        private static IEnumerable<Guid> DeletionIds(InMemoryShelf shelf)
        {
            return shelf.Files.Select(file => file.DeletionId)
                .Concat(shelf.Directories.Select(directory => directory.DeletionId))
                .OfType<Guid>()
                .Distinct();
        }

        /// <summary>The directory a deletion was taken from and the name of what it took.</summary>
        private static (Guid ContainerId, string Name) TopOf(InMemoryShelf shelf, Guid deletionId)
        {
            List<DirectoryNode> directories = shelf.Directories.Where(directory => directory.DeletionId == deletionId).ToList();
            DirectoryNode? top = directories.FirstOrDefault(directory => !directories.Any(other => other.Id == directory.ParentId));
            if (top != null)
            {
                return (top.ParentId!.Value, top.Name);
            }

            FileNode file = shelf.Files.First(candidate => candidate.DeletionId == deletionId);
            return (file.DirectoryId, file.Name);
        }

        private TrashEntry ToTrashEntry(InMemoryShelf shelf, Guid deletionId)
        {
            (Guid containerId, string name) = TopOf(shelf, deletionId);
            List<FileNode> files = shelf.Files.Where(file => file.DeletionId == deletionId).ToList();
            DirectoryNode? topDirectory = shelf.Directories.FirstOrDefault(directory => directory.DeletionId == deletionId && directory.ParentId == containerId && directory.Name == name);
            DateTimeOffset deletedAt = topDirectory?.DeletedAt ?? files[0].DeletedAt!.Value;

            return new TrashEntry
            {
                DeletionId = deletionId,
                Kind = topDirectory != null ? TrashEntryKind.Directory : TrashEntryKind.File,
                EntryId = topDirectory?.Id ?? files[0].Id,
                Path = ShelfPath.Combine(shelf.PathOf(containerId), name),
                DeletedAt = deletedAt,
                PurgesAt = deletedAt + _store.RestoreWindow,
                FileCount = files.Count,
                SizeBytes = files.Sum(file => file.Content.LongLength),
            };
        }
    }
}
