using System.Reflection;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>
    /// A replica replaying the log from before a copy applies older states over newer ones; whatever an
    /// older state displaces is set aside and put back by a later entry, so the result is the primary's.
    /// Each entity type has its own rule for what two of them cannot share.
    /// </summary>
    public class ReplicaApplierTests : ServiceTestBase
    {
        private long _sequence;

        [Fact]
        public void EntityTypes_CoverEveryTypeTheChangeLogHolds()
        {
            IEnumerable<string> logged = typeof(ChangeEntityType)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral)
                .Select(field => (string)field.GetRawConstantValue()!);

            Assert.Equal(logged.Order(), Services.GetRequiredService<ReplicaApplier>().EntityTypes.Order());
        }

        [Fact]
        public async Task ApplyAsync_OlderStateTakesAnotherFilesPlace_ConvergesOnTheNewest()
        {
            Shelf shelf = await CreateShelfAsync();
            Guid root = (await Services.GetRequiredService<IDirectoryRepository>().GetRootAsync(shelf.Id)).Id;
            FileRecord a = File(shelf, root, "x.txt");
            FileRecord cAtX = File(shelf, root, "x.txt");
            FileRecord cAtZ = Moved(cAtX, "z.txt");

            // The primary: C made at x, C moved to z, A made at x. The replica's copy held the newest
            // states; replaying the log from where the copy began brings the older ones again.
            await ApplyAsync(Entry(ChangeEntityType.File, a.Id, a), Entry(ChangeEntityType.File, cAtZ.Id, cAtZ));
            await ApplyAsync(Entry(ChangeEntityType.File, cAtX.Id, cAtX), Entry(ChangeEntityType.File, cAtZ.Id, cAtZ), Entry(ChangeEntityType.File, a.Id, a));

            IFileRepository files = Services.GetRequiredService<IFileRepository>();
            Assert.Equal(a.Id, (await files.GetLiveByNameAsync(root, "x.txt"))?.Id);
            Assert.Equal(cAtX.Id, (await files.GetLiveByNameAsync(root, "z.txt"))?.Id);
        }

        [Fact]
        public async Task ApplyAsync_FilePurged_IsOnlyMovedToTheTrash()
        {
            Shelf shelf = await CreateShelfAsync();
            Guid root = (await Services.GetRequiredService<IDirectoryRepository>().GetRootAsync(shelf.Id)).Id;
            FileRecord file = File(shelf, root, "a.txt");
            await ApplyAsync(Entry(ChangeEntityType.File, file.Id, file));

            await ApplyAsync(Removal(ChangeEntityType.File, file.Id));

            FileRecord? kept = await Services.GetRequiredService<IFileRepository>().GetByIdAsync(file.Id);
            Assert.Equal(Clock.GetUtcNow(), kept?.DeletedAt);
        }

        [Fact]
        public async Task ApplyAsync_DirectoryWhereAnotherIs_TrashesOnlyThatDirectory()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfDirectory here = await DirectoryService.CreateAsync(shelf, "/d");
            ShelfFile inside = await StoreTextAsync(shelf, "/d/a.txt", "a");
            IDirectoryRepository directories = Services.GetRequiredService<IDirectoryRepository>();
            DirectoryRecord root = await directories.GetRootAsync(shelf.Id);
            DirectoryRecord replicated = new DirectoryRecord { Id = Guid.NewGuid(), ShelfId = shelf.Id, ParentId = root.Id, Name = "d", CreatedAt = TestInstants.Now, UpdatedAt = TestInstants.Now };

            await ApplyAsync(Entry(ChangeEntityType.Directory, replicated.Id, replicated));

            Assert.Equal(replicated.Id, (await directories.GetLiveChildAsync(root.Id, "d"))?.Id);
            Assert.Equal(Clock.GetUtcNow(), (await directories.GetByIdAsync(here.Id))?.DeletedAt);
            Assert.Null((await Services.GetRequiredService<IFileRepository>().GetByIdAsync(inside.Id))?.DeletedAt);
        }

        [Fact]
        public async Task ApplyAsync_ShelfWithAnothersName_SetsTheOtherAsideUnderItsId()
        {
            Shelf existing = await CreateShelfAsync("docs");
            Shelf replicated = new Shelf { Id = Guid.NewGuid(), Name = "docs", CreatedAt = TestInstants.Now, UpdatedAt = TestInstants.Now };

            await ApplyAsync(Entry(ChangeEntityType.Shelf, replicated.Id, replicated));

            IShelfRepository shelves = Services.GetRequiredService<IShelfRepository>();
            Assert.Equal(replicated.Id, (await shelves.GetByNameAsync("docs"))?.Id);
            Assert.Equal($"~{existing.Id:D}", (await shelves.GetByIdAsync(existing.Id))?.Name);
        }

        [Fact]
        public async Task ApplyAsync_UserWithAnothersEmail_SetsTheOtherAsideAndKeepsWhenTheyWereMade()
        {
            User existing = await CreateUserAsync("same@example.com");
            DateTimeOffset madeOnThePrimary = TestInstants.Now.AddDays(-3);
            User replicated = new User
            {
                Id = Guid.NewGuid(),
                Email = "Same@example.com",
                DisplayName = "Replicated",
                Roles = Array.Empty<string>(),
                CreatedAt = madeOnThePrimary,
                UpdatedAt = madeOnThePrimary,
            };

            await ApplyAsync(Entry(ChangeEntityType.User, replicated.Id, replicated));

            IUserRepository users = Services.GetRequiredService<IUserRepository>();
            Assert.Equal($"{existing.Id:D}@set-aside.invalid", (await users.GetByIdAsync(existing.Id))?.Email);
            Assert.Equal(madeOnThePrimary, (await users.GetByIdAsync(replicated.Id))?.CreatedAt);
        }

        [Fact]
        public async Task ApplyAsync_LoginWithAnothersIdentity_RemovesTheOther()
        {
            await CreateUserAsync("a@example.com");
            User other = await CreateUserAsync("b@example.com");
            UserLogin replicated = new UserLogin
            {
                Id = Guid.NewGuid(),
                UserId = other.Id,
                Provider = UserLoginRepository.LocalProvider,
                ProviderUserId = "a@example.com",
                Credential = "hash",
                CreatedAt = TestInstants.Now,
            };

            await ApplyAsync(Entry(ChangeEntityType.UserLogin, replicated.Id, replicated));

            IUserLoginRepository logins = Services.GetRequiredService<IUserLoginRepository>();
            Assert.Equal(other.Id, (await logins.GetCredentialsByEmailAsync("a@example.com"))?.User.Id);
            Assert.Null(await logins.GetCredentialsByEmailAsync("b@example.com"));
        }

        [Fact]
        public async Task ApplyAsync_GrantForTheSameShelfAndUser_RemovesTheOther()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("reader@example.com");
            await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);
            ShelfGrant replicated = new ShelfGrant
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                UserId = user.Id,
                Permission = ShelfAccessPermission.ReadWrite,
                CreatedAt = TestInstants.Now,
                UpdatedAt = TestInstants.Now,
            };

            await ApplyAsync(Entry(ChangeEntityType.ShelfGrant, replicated.Id, replicated));

            ShelfGrant? held = await Services.GetRequiredService<IShelfGrantRepository>().GetAsync(shelf.Id, user.Id);
            Assert.Equal((replicated.Id, ShelfAccessPermission.ReadWrite), (held?.Id, held?.Permission));
        }

        [Fact]
        public async Task ApplyAsync_KeyWithAnothersSecret_RemovesTheOther()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey existing = await KeyService.CreateAsync(shelf, "old", ShelfKeyPermission.Read);
            ShelfKey replicated = new ShelfKey
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                Name = "new",
                Permission = ShelfKeyPermission.Read,
                SecretSha256 = SecretHash.Sha256Hex(existing.Secret),
                CreatedAt = TestInstants.Now,
            };

            await ApplyAsync(Entry(ChangeEntityType.ShelfKey, replicated.Id, replicated));

            IReadOnlyList<ShelfKey> keys = await Services.GetRequiredService<IShelfKeyRepository>().GetForShelfAsync(shelf.Id);
            Assert.Equal(replicated.Id, Assert.Single(keys).Id);
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddReplicationServices(AppBaseUrl);
        }

        private static FileRecord File(Shelf shelf, Guid directoryId, string name)
        {
            return new FileRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                DirectoryId = directoryId,
                Name = name,
                ContentType = "text/plain",
                SizeBytes = 1,
                Sha256 = new string('a', 64),
                CreatedAt = TestInstants.Now,
                UpdatedAt = TestInstants.Now,
            };
        }

        private static FileRecord Moved(FileRecord file, string name)
        {
            return new FileRecord
            {
                Id = file.Id,
                ShelfId = file.ShelfId,
                DirectoryId = file.DirectoryId,
                Name = name,
                ContentType = file.ContentType,
                SizeBytes = file.SizeBytes,
                Sha256 = file.Sha256,
                CreatedAt = file.CreatedAt,
                UpdatedAt = file.UpdatedAt,
            };
        }

        private ChangeLogEntry Entry<TState>(string entityType, Guid id, TState state)
        {
            return new ChangeLogEntry
            {
                Sequence = ++_sequence,
                EntityType = entityType,
                EntityId = id,
                Action = ChangeAction.Added,
                Payload = ChangePayload.Serialize(state),
                CreatedAt = TestInstants.Now,
            };
        }

        private ChangeLogEntry Removal(string entityType, Guid id)
        {
            return new ChangeLogEntry { Sequence = ++_sequence, EntityType = entityType, EntityId = id, Action = ChangeAction.Purged, CreatedAt = TestInstants.Now };
        }

        private async Task ApplyAsync(params ChangeLogEntry[] entries)
        {
            await using IDbTransactionSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            await Services.GetRequiredService<ReplicaApplier>().ApplyAsync(entries, session, new AppliedChanges());
            await session.CommitAsync();
        }
    }
}
