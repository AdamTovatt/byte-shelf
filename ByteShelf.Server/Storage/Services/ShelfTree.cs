using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// Walks a shelf's directory tree: resolves paths and ids to live entries, creates missing
    /// directories, and answers full paths. Files and directories share one namespace per directory, as
    /// on a file system: a name is taken by either.
    /// </summary>
    public class ShelfTree
    {
        private readonly IDirectoryRepository _directoryRepository;
        private readonly IFileRepository _fileRepository;

        public ShelfTree(IDirectoryRepository directoryRepository, IFileRepository fileRepository)
        {
            _directoryRepository = directoryRepository;
            _fileRepository = fileRepository;
        }

        /// <summary>The live directory at <paramref name="normalizedPath"/>, or null when any part of it is missing.</summary>
        public async Task<DirectoryRecord?> FindDirectoryAsync(Guid shelfId, string normalizedPath, IDbSession? session = null)
        {
            DirectoryRecord current = await _directoryRepository.GetRootAsync(shelfId, session);

            foreach (string name in ShelfPath.GetNames(normalizedPath))
            {
                DirectoryRecord? child = await _directoryRepository.GetLiveChildAsync(current.Id, name, session);
                if (child == null)
                {
                    return null;
                }

                current = child;
            }

            return current;
        }

        /// <summary>The live directory a reference names on the shelf.</summary>
        /// <exception cref="NotFoundException">There is none.</exception>
        public async Task<DirectoryRecord> GetDirectoryAsync(Guid shelfId, EntryReference reference, IDbSession? session = null)
        {
            DirectoryRecord? directory;

            if (reference.Id != null)
            {
                directory = await _directoryRepository.GetByIdAsync(reference.Id.Value, session);
                if (directory != null && (directory.ShelfId != shelfId || directory.DeletedAt != null))
                {
                    directory = null;
                }
            }
            else
            {
                directory = await FindDirectoryAsync(shelfId, reference.Path!, session);
            }

            return directory ?? throw new NotFoundException($"There is no directory '{reference}' on this shelf.");
        }

        /// <summary>The live file a reference names on the shelf.</summary>
        /// <exception cref="NotFoundException">There is none.</exception>
        public async Task<FileRecord> GetFileAsync(Guid shelfId, EntryReference reference, IDbSession? session = null)
        {
            FileRecord? file = null;

            if (reference.Id != null)
            {
                file = await _fileRepository.GetByIdAsync(reference.Id.Value, session);
                if (file != null && (file.ShelfId != shelfId || file.DeletedAt != null))
                {
                    file = null;
                }
            }
            else if (reference.Path != ShelfPath.Root)
            {
                DirectoryRecord? directory = await FindDirectoryAsync(shelfId, ShelfPath.GetParent(reference.Path!), session);
                if (directory != null)
                {
                    file = await _fileRepository.GetLiveByNameAsync(directory.Id, ShelfPath.GetName(reference.Path!), session);
                }
            }

            return file ?? throw new NotFoundException($"There is no file '{reference}' on this shelf.");
        }

        /// <summary>
        /// The live directory at <paramref name="normalizedPath"/>, creating every missing directory on the
        /// way with <paramref name="at"/> as its creation time. Run inside the caller's write transaction.
        /// </summary>
        /// <exception cref="ConflictException">A file holds the name of one of the directories.</exception>
        public async Task<DirectoryRecord> EnsureDirectoryAsync(Guid shelfId, string normalizedPath, DateTimeOffset at, IDbSession session)
        {
            DirectoryRecord current = await _directoryRepository.GetRootAsync(shelfId, session);
            string currentPath = ShelfPath.Root;

            foreach (string name in ShelfPath.GetNames(normalizedPath))
            {
                currentPath = ShelfPath.Combine(currentPath, name);
                DirectoryRecord? child = await _directoryRepository.GetLiveChildAsync(current.Id, name, session);

                if (child == null)
                {
                    if (await _fileRepository.GetLiveByNameAsync(current.Id, name, session) != null)
                    {
                        throw new ConflictException($"'{currentPath}' is a file, not a directory.");
                    }

                    child = new DirectoryRecord
                    {
                        Id = Guid.NewGuid(),
                        ShelfId = shelfId,
                        ParentId = current.Id,
                        Name = name,
                        CreatedAt = at,
                        UpdatedAt = at,
                    };

                    await _directoryRepository.InsertAsync(child, session);
                }

                current = child;
            }

            return current;
        }

        /// <summary>
        /// Throws unless <paramref name="name"/> can be used inside <paramref name="directoryId"/>: no live
        /// directory has it, and no live file either unless <paramref name="mayReplaceFile"/>.
        /// </summary>
        /// <exception cref="ConflictException">The name is taken.</exception>
        public async Task EnsureNameAvailableAsync(Guid directoryId, string name, string pathForMessage, bool mayReplaceFile, IDbSession? session = null)
        {
            if (await _directoryRepository.GetLiveChildAsync(directoryId, name, session) != null)
            {
                throw new ConflictException($"'{pathForMessage}' is a directory.");
            }

            if (!mayReplaceFile && await _fileRepository.GetLiveByNameAsync(directoryId, name, session) != null)
            {
                throw new ConflictException($"A file already exists at '{pathForMessage}'.");
            }
        }

        /// <summary>
        /// Readies <paramref name="normalizedPath"/> to receive a moved entry: creates the missing
        /// directories above it and checks that nothing has its name. Answers the directory it goes in
        /// and the name it gets. Run inside the caller's write transaction.
        /// </summary>
        /// <exception cref="ArgumentException">The path is the root.</exception>
        /// <exception cref="ConflictException">The name is taken, or a file is in the way.</exception>
        public async Task<(DirectoryRecord Parent, string Name)> PrepareDestinationAsync(Guid shelfId, string normalizedPath, DateTimeOffset at, IDbSession session)
        {
            if (normalizedPath == ShelfPath.Root)
            {
                throw new ArgumentException("Nothing can be moved to the root itself; give it a name.");
            }

            DirectoryRecord parent = await EnsureDirectoryAsync(shelfId, ShelfPath.GetParent(normalizedPath), at, session);
            string name = ShelfPath.GetName(normalizedPath);
            await EnsureNameAvailableAsync(parent.Id, name, normalizedPath, mayReplaceFile: false, session);

            return (parent, name);
        }

        /// <summary>The full path of a directory, live or not.</summary>
        public async Task<string> GetDirectoryPathAsync(Guid directoryId, IDbSession? session = null)
        {
            IReadOnlyList<string> names = await _directoryRepository.GetPathNamesAsync(directoryId, session);
            return ShelfPath.Root + string.Join(ShelfPath.Separator, names);
        }

        /// <summary>The contract form of a file, with its full path.</summary>
        public async Task<ShelfFile> ToShelfFileAsync(FileRecord file, IDbSession? session = null)
        {
            return StorageContracts.ToShelfFile(file, await GetDirectoryPathAsync(file.DirectoryId, session));
        }
    }
}
