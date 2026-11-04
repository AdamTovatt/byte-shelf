using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>The one place storage rows become the contract types clients see.</summary>
    public static class StorageContracts
    {
        public static ShelfFile ToShelfFile(FileRecord file, string directoryPath)
        {
            return new ShelfFile
            {
                Id = file.Id,
                ShelfId = file.ShelfId,
                DirectoryId = file.DirectoryId,
                Path = ShelfPath.Combine(directoryPath, file.Name),
                Name = file.Name,
                ContentType = file.ContentType,
                SizeBytes = file.SizeBytes,
                Sha256 = file.Sha256,
                CreatedAt = file.CreatedAt,
                UpdatedAt = file.UpdatedAt,
                Description = file.Description,
                Encrypted = file.Encrypted,
            };
        }

        public static ShelfDirectory ToShelfDirectory(DirectoryRecord directory, string path)
        {
            return new ShelfDirectory
            {
                Id = directory.Id,
                ShelfId = directory.ShelfId,
                ParentId = directory.ParentId,
                Path = path,
                Name = directory.Name,
                CreatedAt = directory.CreatedAt,
                Encrypted = directory.EncryptionCheck != null,
            };
        }

        /// <summary>An encrypted directory with its check; null for one that is not encrypted.</summary>
        public static EncryptedFolder? ToEncryptedFolder(DirectoryRecord? directory, string path)
        {
            return directory?.EncryptionCheck is byte[] check
                ? new EncryptedFolder { Directory = ToShelfDirectory(directory, path), Check = Convert.ToBase64String(check) }
                : null;
        }

        public static ListedDirectory ToListedDirectory(DirectoryWithTotals listed, string path)
        {
            return new ListedDirectory
            {
                Directory = ToShelfDirectory(listed.Directory, path),
                SizeBytes = listed.SizeBytes,
                FileCount = listed.FileCount,
                EncryptedFileCount = listed.EncryptedFileCount,
            };
        }

        public static UploadSession ToUploadSession(UploadRecord upload, StorageOptions options)
        {
            return new UploadSession
            {
                Id = upload.Id,
                ShelfId = upload.ShelfId,
                Path = (upload.Target as StoreAtPath)?.Path,
                SwapFileId = (upload.Target as ContentSwap)?.FileId,
                ContentType = upload.ContentType,
                SizeBytes = upload.SizeBytes,
                ReceivedBytes = upload.ReceivedBytes,
                Overwrite = upload.Target is StoreAtPath { Overwrite: true },
                CreatedAt = upload.CreatedAt,
                ExpiresAt = upload.LastActivityAt + options.AbandonedUploadAge,
            };
        }

        public static TrashEntry ToTrashEntry(TrashedEntry entry, string containerPath, StorageOptions options)
        {
            return new TrashEntry
            {
                DeletionId = entry.DeletionId,
                Kind = entry.Kind,
                EntryId = entry.EntryId,
                Path = ShelfPath.Combine(containerPath, entry.Name),
                DeletedAt = entry.DeletedAt,
                PurgesAt = entry.DeletedAt + options.RestoreWindow,
                FileCount = entry.FileCount,
                SizeBytes = entry.SizeBytes,
            };
        }

        public static ShelfInfo ToShelfInfo(Shelf shelf, ShelfUsage usage)
        {
            return new ShelfInfo
            {
                Id = shelf.Id,
                Name = shelf.Name,
                QuotaBytes = shelf.QuotaBytes,
                UsedBytes = usage.UsedBytes,
                ReservedBytes = usage.ReservedBytes,
                FileCount = usage.FileCount,
                CreatedAt = shelf.CreatedAt,
            };
        }
    }
}
