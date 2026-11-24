using System.Reflection;
using System.Text.Json;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
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
        public async Task ApplyAsync_PasskeyRenamed_KeepsThisServersSignCountAndLastUse()
        {
            User user = await CreateUserAsync("a@example.com");
            Passkey passkey = Passkey(user, "credential-a", "Laptop");
            await ApplyAsync(Entry(ChangeEntityType.Passkey, passkey.Id, passkey));
            IPasskeyRepository passkeys = Services.GetRequiredService<IPasskeyRepository>();
            await passkeys.RecordSignInAsync(passkey.Id, 9, TestInstants.Now);

            Passkey renamed = Passkey(user, "credential-a", "Old laptop", passkey.Id);
            await ApplyAsync(Entry(ChangeEntityType.Passkey, renamed.Id, renamed));

            StoredPasskey? stored = await passkeys.GetByCredentialIdAsync("credential-a");
            Assert.Equal(("Old laptop", 9u, TestInstants.Now), (stored?.Passkey.Name, stored?.SignCount, stored?.LastUsedAt));
        }

        [Fact]
        public async Task ApplyAsync_PasskeyWithAnothersCredentialId_RemovesTheOther()
        {
            User user = await CreateUserAsync("a@example.com");
            Passkey earlier = Passkey(user, "credential-a", "Laptop");
            await ApplyAsync(Entry(ChangeEntityType.Passkey, earlier.Id, earlier));

            Passkey replicated = Passkey(user, "credential-a", "Phone");
            await ApplyAsync(Entry(ChangeEntityType.Passkey, replicated.Id, replicated));

            StoredPasskey listed = Assert.Single(await Services.GetRequiredService<IPasskeyRepository>().GetForUserAsync(user.Id));
            Assert.Equal(replicated.Id, listed.Passkey.Id);
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
        public async Task ApplyAsync_KeyGrantForTheSameKeyAndShelf_RemovesTheOther()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);
            ShelfKeyGrant replicated = KeyGrant(created.Key.Id, shelf.Id, ShelfKeyPermission.ReadWrite);

            await ApplyAsync(Entry(ChangeEntityType.ShelfKeyGrant, replicated.Id, replicated));

            ShelfKeyGrant? held = await Services.GetRequiredService<IShelfKeyGrantRepository>().GetAsync(shelf.Id, created.Key.Id);
            Assert.Equal((replicated.Id, ShelfKeyPermission.ReadWrite), (held?.Id, held?.Permission));
        }

        [Fact]
        public async Task ApplyAsync_KeyWithAnothersSecret_RemovesTheOtherAndItsGrants()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey existing = await KeyService.CreateAsync(shelf, "old", ShelfKeyPermission.Read);
            ShelfKey replicated = new ShelfKey
            {
                Id = Guid.NewGuid(),
                Name = "new",
                SecretSha256 = SecretHash.Sha256Hex(existing.Secret),
                CreatedAt = TestInstants.Now,
            };

            await ApplyAsync(Entry(ChangeEntityType.ShelfKey, replicated.Id, replicated));

            Assert.Equal(replicated.Id, (await KeyService.FindBySecretAsync(existing.Secret))?.Id);
            Assert.Null(await Services.GetRequiredService<IShelfKeyRepository>().GetByIdAsync(existing.Key.Id));
            Assert.Empty(await Services.GetRequiredService<IShelfKeyGrantRepository>().GetForKeyAsync(existing.Key.Id));
        }

        [Fact]
        public async Task ApplyAsync_KeyThenItsGrants_GivesTheKeyEachShelf()
        {
            Shelf docs = await CreateShelfAsync("docs");
            Shelf photos = await CreateShelfAsync("photos");
            ShelfKey key = new ShelfKey { Id = Guid.NewGuid(), Name = "app", SecretSha256 = new string('c', 64), CreatedAt = TestInstants.Now };
            ShelfKeyGrant onDocs = KeyGrant(key.Id, docs.Id, ShelfKeyPermission.Read);
            ShelfKeyGrant onPhotos = KeyGrant(key.Id, photos.Id, ShelfKeyPermission.AddOnly);

            await ApplyAsync(
                Entry(ChangeEntityType.ShelfKey, key.Id, key),
                Entry(ChangeEntityType.ShelfKeyGrant, onDocs.Id, onDocs),
                Entry(ChangeEntityType.ShelfKeyGrant, onPhotos.Id, onPhotos));

            Assert.Equal(
                new[] { ("docs", ShelfKeyPermission.Read), ("photos", ShelfKeyPermission.AddOnly) },
                (await KeyService.GetGrantsAsync(key.Id)).Select(grant => (grant.ShelfName, grant.Permission)).OrderBy(grant => grant.ShelfName));
        }

        [Fact]
        public async Task ApplyAsync_ReadingFromAPrimaryThatKeepsNoVersions_StopsTheReplica()
        {
            string sha256 = new string('a', 64);
            Guid id = ObjectMetadata.IdFor(sha256);

            await Assert.ThrowsAsync<PrimaryFailedException>(() => ApplyAsync(
                Entry(ChangeEntityType.ObjectMetadata, id, new { Id = id, Sha256 = sha256, ReadAt = TestInstants.Now, Metadata = new { Camera = "Nikon" } })));
        }

        [Fact]
        public async Task ApplyAsync_KeysRemoval_RemovesTheGrantsItStillHasHere()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);

            await ApplyAsync(Removal(ChangeEntityType.ShelfKey, created.Key.Id));

            Assert.Null(await KeyService.FindBySecretAsync(created.Secret));
            Assert.Empty(await Services.GetRequiredService<IShelfKeyGrantRepository>().GetForKeyAsync(created.Key.Id));
        }

        /// <summary>A key state in a shape this build does not take, as a primary on another version sends, is refused in words the replica's admins see, not applied as a key that works nowhere.</summary>
        [Fact]
        public async Task ApplyAsync_KeyInTheShapeBeforeGrants_IsRefusedAsFromAPrimaryOnAnotherVersion()
        {
            Shelf shelf = await CreateShelfAsync();
            Guid keyId = Guid.NewGuid();
            string oldShape = $$"""{"id":"{{keyId:D}}","shelfId":"{{shelf.Id:D}}","name":"app","permission":"read","secretSha256":"{{new string('d', 64)}}","createdAt":"2026-01-01T00:00:00+00:00"}""";

            ChangeLogEntry entry = new ChangeLogEntry
            {
                Sequence = ++_sequence,
                EntityType = ChangeEntityType.ShelfKey,
                EntityId = keyId,
                Action = ChangeAction.Added,
                Payload = oldShape,
                CreatedAt = TestInstants.Now,
            };

            PrimaryFailedException refused = await Assert.ThrowsAsync<PrimaryFailedException>(() => ApplyAsync(entry));

            Assert.IsType<JsonException>(refused.InnerException);
            Assert.Contains("run the same version of ByteShelf on both", refused.Message, StringComparison.Ordinal);

            Assert.Null(await Services.GetRequiredService<IShelfKeyRepository>().GetByIdAsync(keyId));
        }

        /// <summary>A replica writes what it is sent into its own table and log: a document its rules refuse stops it rather than being kept.</summary>
        [Theory]
        [InlineData("[1]")]
        [InlineData("\"text\"")]
        [InlineData(null)]
        public async Task ApplyAsync_PluginDocumentThatBreaksTheRules_IsRefusedAsFromAPrimaryOnAnotherVersion(string? document)
        {
            string sent = document ?? $"{{\"t\":\"{new string('x', PluginDocument.MaxBytes)}\"}}";
            ContentPluginMetadata metadata = ContentPluginMetadata.Of(TestPluginMetadata.Plugin, new string('a', 64), 1, TestInstants.Now, PluginDocument.Parse("{}"));
            ChangeLogEntry entry = new ChangeLogEntry
            {
                Sequence = ++_sequence,
                EntityType = ChangeEntityType.ContentPluginMetadata,
                EntityId = metadata.Id,
                Action = ChangeAction.Added,
                Payload = ChangePayload.Serialize(metadata).Replace("\"document\":{}", $"\"document\":{sent}", StringComparison.Ordinal),
                CreatedAt = TestInstants.Now,
            };

            await Assert.ThrowsAsync<PrimaryFailedException>(() => ApplyAsync(entry));

            Assert.Null(await Services.GetContentDocumentAsync(metadata.Sha256));
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddReplicationServices(new ReplicationOptions { PublicUrl = AppBaseUrl });
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

        private static ShelfKeyGrant KeyGrant(Guid keyId, Guid shelfId, ShelfKeyPermission permission)
        {
            return new ShelfKeyGrant
            {
                Id = Guid.NewGuid(),
                KeyId = keyId,
                ShelfId = shelfId,
                Permission = permission,
                CreatedAt = TestInstants.Now,
                UpdatedAt = TestInstants.Now,
            };
        }

        private ChangeLogEntry Removal(string entityType, Guid id)
        {
            return new ChangeLogEntry { Sequence = ++_sequence, EntityType = entityType, EntityId = id, Action = ChangeAction.Purged, CreatedAt = TestInstants.Now };
        }

        private static Passkey Passkey(User user, string credentialId, string name, Guid? id = null)
        {
            return new Passkey
            {
                Id = id ?? Guid.NewGuid(),
                UserId = user.Id,
                CredentialId = credentialId,
                PublicKey = "public-key",
                Transports = new[] { "internal" },
                Name = name,
                CreatedAt = TestInstants.Now,
            };
        }

        private async Task ApplyAsync(params ChangeLogEntry[] entries)
        {
            await using IDbTransactionSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            ReplicaApplier applier = Services.GetRequiredService<ReplicaApplier>();
            AppliedChanges changes = new AppliedChanges();
            await applier.ApplyAsync(entries, session, changes);
            await applier.SettleAsync(session, changes);
            await session.CommitAsync();
        }
    }
}
