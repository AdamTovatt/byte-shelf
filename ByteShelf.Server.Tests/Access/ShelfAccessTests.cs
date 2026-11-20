using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Access
{
    /// <summary>
    /// Who may go on with an upload: its starter while it may still store what the upload stores (a grant
    /// can change meanwhile), anyone else only with write access.
    /// </summary>
    public class ShelfAccessTests
    {
        private static readonly Shelf Shelf = new Shelf { Id = Guid.NewGuid(), Name = "docs", CreatedAt = TestInstants.Now, UpdatedAt = TestInstants.Now };
        private static readonly ShelfCaller Starter = ShelfCaller.ForKey(new ShelfKey { Id = Guid.NewGuid(), Name = "job", SecretSha256 = new string('0', 64), CreatedAt = TestInstants.Now });

        private static readonly StoreAtPath NewFile = new StoreAtPath("/a.txt", Overwrite: false, Description: null);
        private static readonly StoreAtPath Overwrite = new StoreAtPath("/a.txt", Overwrite: true, Description: null);
        private static readonly ContentSwap Swap = new ContentSwap(Guid.NewGuid(), new string('a', 64));

        public static TheoryData<UploadTarget, ShelfKeyPermission> StartersUploadsItMayStillStore => new TheoryData<UploadTarget, ShelfKeyPermission>
        {
            { NewFile, ShelfKeyPermission.AddOnly },
            { Overwrite, ShelfKeyPermission.ReadWrite },
            { Swap, ShelfKeyPermission.ReadWrite },
        };

        public static TheoryData<UploadTarget, ShelfKeyPermission> StartersUploadsItMayNoLongerStore => new TheoryData<UploadTarget, ShelfKeyPermission>
        {
            { NewFile, ShelfKeyPermission.Read },
            { Overwrite, ShelfKeyPermission.AddOnly },
            { Swap, ShelfKeyPermission.AddOnly },
        };

        [Theory]
        [MemberData(nameof(StartersUploadsItMayStillStore))]
        public void RequireUpload_ItsStarterThatMayStillStoreIt_Passes(UploadTarget target, ShelfKeyPermission permission)
        {
            ShelfAccess access = ShelfAccess.ForKey(Shelf, Starter, permission);

            access.RequireUpload(Upload(Starter.Id, target));
        }

        [Theory]
        [MemberData(nameof(StartersUploadsItMayNoLongerStore))]
        public void RequireUpload_ItsStarterThatMayNoLongerStoreIt_IsRefused(UploadTarget target, ShelfKeyPermission permission)
        {
            ShelfAccess access = ShelfAccess.ForKey(Shelf, Starter, permission);

            Assert.Throws<PermissionDeniedException>(() => access.RequireUpload(Upload(Starter.Id, target)));
        }

        [Fact]
        public void RequireUpload_SomeoneElsesWithoutWrite_IsRefused()
        {
            ShelfAccess access = ShelfAccess.ForKey(Shelf, Starter, ShelfKeyPermission.AddOnly);

            Assert.Throws<PermissionDeniedException>(() => access.RequireUpload(Upload("user:someone-else", NewFile)));
        }

        private static UploadRecord Upload(string startedBy, UploadTarget target)
        {
            return new UploadRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = Shelf.Id,
                StartedBy = startedBy,
                Target = target,
                ContentType = "text/plain",
                SizeBytes = 1,
                ReceivedBytes = 0,
                Encrypted = false,
                IsResumable = true,
                CreatedAt = TestInstants.Now,
                LastActivityAt = TestInstants.Now,
            };
        }
    }
}
