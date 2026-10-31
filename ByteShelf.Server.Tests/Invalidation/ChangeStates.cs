using ByteShelf.Server.Access.Models;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Invalidation
{
    /// <summary>Real entity states on a given shelf, as the repositories write them into the change log.</summary>
    internal static class ChangeStates
    {
        public static object For(string entityType, Guid shelfId)
        {
            return entityType switch
            {
                ChangeEntityType.File => File(shelfId),
                ChangeEntityType.Directory => Directory(shelfId),
                ChangeEntityType.ShelfGrant => Grant(shelfId),
                ChangeEntityType.ShelfKey => Key(shelfId),
                _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Not a shelf-scoped entity."),
            };
        }

        public static FileRecord File(Guid shelfId)
        {
            return new FileRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = shelfId,
                DirectoryId = Guid.NewGuid(),
                Name = "notes.txt",
                ContentType = "text/plain",
                SizeBytes = 5,
                Sha256 = new string('a', 64),
                CreatedAt = TestInstants.Now,
                UpdatedAt = TestInstants.Now,
            };
        }

        public static DirectoryRecord Directory(Guid shelfId)
        {
            return new DirectoryRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = shelfId,
                ParentId = Guid.NewGuid(),
                Name = "docs",
                CreatedAt = TestInstants.Now,
                UpdatedAt = TestInstants.Now,
            };
        }

        public static ShelfGrant Grant(Guid shelfId)
        {
            return new ShelfGrant
            {
                Id = Guid.NewGuid(),
                ShelfId = shelfId,
                UserId = Guid.NewGuid(),
                Permission = ShelfAccessPermission.Read,
                CreatedAt = TestInstants.Now,
                UpdatedAt = TestInstants.Now,
            };
        }

        public static ShelfKey Key(Guid shelfId)
        {
            return new ShelfKey
            {
                Id = Guid.NewGuid(),
                ShelfId = shelfId,
                Name = "backup",
                Permission = ShelfKeyPermission.Read,
                SecretSha256 = new string('b', 64),
                CreatedAt = TestInstants.Now,
            };
        }
    }
}
