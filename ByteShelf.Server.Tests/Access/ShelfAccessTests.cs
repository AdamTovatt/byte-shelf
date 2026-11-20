using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Access
{
    /// <summary>
    /// Who may go on with an upload: its starter while it may still store what the upload stores (a grant
    /// can change meanwhile), anyone else only with write access.
    /// </summary>
    public class ShelfAccessTests
    {
        private static readonly Shelf Shelf = new Shelf { Id = Guid.NewGuid(), Name = "docs", CreatedAt = TestInstants.Now, UpdatedAt = TestInstants.Now };
        private static readonly ShelfCaller Starter = ShelfCaller.ForUser(Guid.NewGuid(), isAdmin: false);

        public static TheoryData<string, ShelfOperation[], bool> StartersUploads => new TheoryData<string, ShelfOperation[], bool>
        {
            { "new", new[] { ShelfOperation.Read, ShelfOperation.Add }, true },
            { "new", new[] { ShelfOperation.Read }, false },
            { "overwrite", new[] { ShelfOperation.Read, ShelfOperation.Add, ShelfOperation.Write }, true },
            { "overwrite", new[] { ShelfOperation.Read, ShelfOperation.Add }, false },
            { "swap", new[] { ShelfOperation.Read, ShelfOperation.Add, ShelfOperation.Write }, true },
            { "swap", new[] { ShelfOperation.Read, ShelfOperation.Add }, false },
        };

        [Theory]
        [MemberData(nameof(StartersUploads))]
        public void RequireUpload_ItsStarter_PassesOnlyWhileItMayStillStoreWhatTheUploadStores(string kind, ShelfOperation[] allowed, bool passes)
        {
            ShelfAccess access = new ShelfAccess(Shelf, Starter, allowed.ToHashSet());
            UploadRecord upload = Upload(Starter.Id, kind);

            Exception? refused = Record.Exception(() => access.RequireUpload(upload));

            if (passes)
            {
                Assert.Null(refused);
            }
            else
            {
                Assert.IsType<PermissionDeniedException>(refused);
            }
        }

        [Fact]
        public void RequireUpload_SomeoneElsesWithoutWrite_IsRefused()
        {
            ShelfAccess access = new ShelfAccess(Shelf, Starter, new HashSet<ShelfOperation> { ShelfOperation.Read, ShelfOperation.Add });

            Assert.Throws<PermissionDeniedException>(() => access.RequireUpload(Upload("user:someone-else", "new")));
        }

        private static UploadRecord Upload(string startedBy, string kind)
        {
            UploadTarget target = kind switch
            {
                "new" => new StoreAtPath("/a.txt", Overwrite: false, Description: null),
                "overwrite" => new StoreAtPath("/a.txt", Overwrite: true, Description: null),
                _ => new ContentSwap(Guid.NewGuid(), new string('a', 64)),
            };

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
