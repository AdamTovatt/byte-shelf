using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// The rules of encrypted directories, in one place. Encryption happens in the clients; once a
    /// directory is encrypted the server lets no new plain file under it, and keeps one password per
    /// subtree. So a directory is never encrypted inside or above another, a plain file is never
    /// stored, moved or restored into one, and nothing comes into one from outside it (its password
    /// could differ). A file moved out keeps its encryption. Plain files already there when it was
    /// encrypted stay plain until a client encrypts them in place.
    /// </summary>
    public sealed class ShelfEncryption
    {
        private readonly IDirectoryRepository _directoryRepository;
        private readonly IFileRepository _fileRepository;
        private readonly ShelfTree _shelfTree;

        public ShelfEncryption(IDirectoryRepository directoryRepository, IFileRepository fileRepository, ShelfTree shelfTree)
        {
            _directoryRepository = directoryRepository;
            _fileRepository = fileRepository;
            _shelfTree = shelfTree;
        }

        /// <summary>The encrypted directory a file in <paramref name="normalizedDirectoryPath"/> is, or would be, under; null for none.</summary>
        public async Task<DirectoryRecord?> FolderForAsync(Guid shelfId, string normalizedDirectoryPath, IDbSession? session = null)
        {
            DirectoryRecord nearest = await _shelfTree.FindNearestDirectoryAsync(shelfId, normalizedDirectoryPath, session);
            return await _directoryRepository.GetEncryptedAncestorOrSelfAsync(nearest.Id, session);
        }

        /// <summary>The encrypted directory <paramref name="directoryId"/> is or is inside, as clients see it; null for none.</summary>
        public async Task<EncryptedFolder?> DescribeFolderOfAsync(Guid directoryId, IDbSession session)
        {
            return await DescribeAsync(await _directoryRepository.GetEncryptedAncestorOrSelfAsync(directoryId, session), session);
        }

        /// <summary><paramref name="folder"/> as clients see it, with its path; null for none.</summary>
        public async Task<EncryptedFolder?> DescribeAsync(DirectoryRecord? folder, IDbSession session)
        {
            return folder == null ? null : StorageContracts.ToEncryptedFolder(folder, await _shelfTree.GetDirectoryPathAsync(folder.Id, session));
        }

        /// <summary>Throws unless a file may be stored at <paramref name="normalizedPath"/>, encrypted or not as <paramref name="encrypted"/> says.</summary>
        /// <exception cref="ConflictException">A plain file under an encrypted directory.</exception>
        public async Task RequireStoreAsync(Guid shelfId, string normalizedPath, bool encrypted, IDbSession session)
        {
            if (encrypted)
            {
                return;
            }

            DirectoryRecord? folder = await FolderForAsync(shelfId, ShelfPath.GetParent(normalizedPath), session);
            if (folder != null)
            {
                string folderPath = await _shelfTree.GetDirectoryPathAsync(folder.Id, session);
                throw new ConflictException(
                    $"'{normalizedPath}' is under the encrypted folder '{folderPath}': store it encrypted with that folder's password, as the web UI and the byteshelf CLI do.");
            }
        }

        /// <summary>
        /// Throws unless a download link may be made to <paramref name="file"/>: never to an encrypted
        /// one, which is opened in a client that has its password.
        /// </summary>
        /// <exception cref="ConflictException">It is encrypted.</exception>
        public static void RequireDownloadLinkTarget(ShelfFile file)
        {
            if (file.Encrypted)
            {
                throw new ConflictException($"'{file.Path}' is encrypted with a password, so it is opened in a client that has the password, never by a link.");
            }
        }

        /// <summary>
        /// Throws unless an upload link may store at <paramref name="normalizedPath"/>: a link sends a plain
        /// file, so never under an encrypted directory nor over an encrypted file.
        /// </summary>
        /// <exception cref="ConflictException">It would.</exception>
        public async Task RequireLinkTargetAsync(Guid shelfId, string normalizedPath, IDbSession? session = null)
        {
            string parentPath = ShelfPath.GetParent(normalizedPath);
            if (await FolderForAsync(shelfId, parentPath, session) is DirectoryRecord folder)
            {
                throw new ConflictException(
                    $"An upload link sends a plain file, and '{normalizedPath}' is under the encrypted folder '{await _shelfTree.GetDirectoryPathAsync(folder.Id, session)}'.");
            }

            if (await _shelfTree.FindDirectoryAsync(shelfId, parentPath, session) is DirectoryRecord parent
                && await _fileRepository.GetLiveByNameAsync(parent.Id, ShelfPath.GetName(normalizedPath), session) is FileRecord { Encrypted: true })
            {
                throw new ConflictException($"'{normalizedPath}' is encrypted, and an upload link would replace it with a plain file.");
            }
        }

        /// <summary>
        /// Throws unless an entry may move from where it is to a place under <paramref name="destinationParentId"/>:
        /// into an encrypted directory only from inside that same one. <paramref name="movedDirectoryId"/> is
        /// the moved directory, null for a file in <paramref name="currentParentId"/>.
        /// </summary>
        /// <exception cref="ConflictException">It would come into an encrypted directory from outside it.</exception>
        public async Task RequireMoveAsync(Guid? movedDirectoryId, Guid currentParentId, Guid destinationParentId, string pathForMessage, IDbSession session)
        {
            DirectoryRecord? destination = await _directoryRepository.GetEncryptedAncestorOrSelfAsync(destinationParentId, session);
            if (destination == null)
            {
                return;
            }

            DirectoryRecord? source = await _directoryRepository.GetEncryptedAncestorOrSelfAsync(movedDirectoryId ?? currentParentId, session);
            if (source?.Id != destination.Id)
            {
                string folderPath = await _shelfTree.GetDirectoryPathAsync(destination.Id, session);
                throw new ConflictException(
                    $"'{pathForMessage}' cannot move into the encrypted folder '{folderPath}' from outside it, since it is not encrypted with that folder's password. Store it there anew instead.");
            }
        }

        /// <summary>
        /// Throws unless what the deletion <paramref name="deletionId"/> holds may come back under
        /// <paramref name="containerId"/>: under an encrypted directory, only encrypted files and no
        /// directory encrypted on its own.
        /// </summary>
        /// <exception cref="ConflictException">It would bring a plain file or another password into an encrypted directory.</exception>
        public async Task RequireRestoreAsync(Guid containerId, Guid deletionId, string pathForMessage, IDbSession session)
        {
            DirectoryRecord? folder = await _directoryRepository.GetEncryptedAncestorOrSelfAsync(containerId, session);
            if (folder != null
                && (await _fileRepository.DeletionHoldsPlainFileAsync(deletionId, session)
                    || await _directoryRepository.DeletionHoldsEncryptedDirectoryAsync(deletionId, session)))
            {
                string folderPath = await _shelfTree.GetDirectoryPathAsync(folder.Id, session);
                throw new ConflictException(
                    $"'{pathForMessage}' cannot be restored: the folder it was in is now under the encrypted folder '{folderPath}', and what it holds is not encrypted with that folder's password.");
            }
        }

        /// <summary>Encrypts <paramref name="directory"/>: from now on every file stored under it must be encrypted.</summary>
        /// <param name="check">The check, as base64 (<see cref="FileEncryption.DecodeCheck"/>).</param>
        /// <exception cref="ArgumentException">The check is not base64 of a short age file.</exception>
        /// <exception cref="ConflictException">It is encrypted already, or inside or above an encrypted directory.</exception>
        public async Task<DirectoryRecord> EncryptAsync(DirectoryRecord directory, string path, string check, DateTimeOffset at, IDbSession session)
        {
            byte[] checkBytes = FileEncryption.DecodeCheck(check);
            if (directory.EncryptionCheck != null)
            {
                throw new ConflictException($"'{path}' is encrypted already. Remove its encryption first to give it another password.");
            }

            if (directory.ParentId is Guid parentId && await _directoryRepository.GetEncryptedAncestorOrSelfAsync(parentId, session) is DirectoryRecord above)
            {
                throw new ConflictException($"'{path}' is inside the encrypted folder '{await _shelfTree.GetDirectoryPathAsync(above.Id, session)}', whose password covers it.");
            }

            if (await _directoryRepository.HasEncryptedLiveDescendantAsync(directory.Id, session))
            {
                throw new ConflictException($"A folder under '{path}' is encrypted with its own password. Remove its encryption first.");
            }

            return await _directoryRepository.SetEncryptionCheckAsync(directory.Id, checkBytes, at, session)
                ?? throw new NotFoundException($"There is no directory '{path}'.");
        }

        /// <summary>Stops encrypting <paramref name="directory"/>: files stored under it may be plain again, and those there stay encrypted.</summary>
        /// <exception cref="ConflictException">It is not encrypted itself.</exception>
        public async Task<DirectoryRecord> RemoveAsync(DirectoryRecord directory, string path, DateTimeOffset at, IDbSession session)
        {
            if (directory.EncryptionCheck == null)
            {
                throw new ConflictException($"'{path}' is not an encrypted folder.");
            }

            return await _directoryRepository.SetEncryptionCheckAsync(directory.Id, null, at, session)
                ?? throw new NotFoundException($"There is no directory '{path}'.");
        }
    }
}
