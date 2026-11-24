using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Access
{
    public class ShelfAccessResolverTests : ServiceTestBase
    {
        [Fact]
        public async Task ResolveAsync_Admin_AllowsEverything()
        {
            Shelf shelf = await CreateShelfAsync();
            User admin = await CreateUserAsync("admin@example.com", UserRoles.Admin);

            ShelfAccess access = await AccessResolver.ResolveAsync(ShelfCaller.ForUser(admin.Id, isAdmin: true), shelf);

            Assert.Equal(Enum.GetValues<ShelfOperation>(), Enum.GetValues<ShelfOperation>().Where(access.Allows));
        }

        [Fact]
        public async Task ResolveAsync_UserWithoutGrant_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");

            await Assert.ThrowsAsync<NotFoundException>(() => AccessResolver.ResolveAsync(ShelfCaller.ForUser(user.Id, isAdmin: false), shelf));
        }

        [Theory]
        [InlineData(ShelfAccessPermission.Read, new[] { ShelfOperation.Read })]
        [InlineData(ShelfAccessPermission.ReadWrite, new[] { ShelfOperation.Read, ShelfOperation.Add, ShelfOperation.Write })]
        public async Task ResolveAsync_UserWithGrant_AllowsWhatTheGrantSays(ShelfAccessPermission permission, ShelfOperation[] expected)
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");
            await GrantService.SetAsync(shelf, user.Id, permission);

            ShelfAccess access = await AccessResolver.ResolveAsync(ShelfCaller.ForUser(user.Id, isAdmin: false), shelf);

            Assert.Equal(expected, Enum.GetValues<ShelfOperation>().Where(access.Allows));
        }

        [Theory]
        [InlineData(ShelfPermission.Read, new[] { ShelfOperation.Read })]
        [InlineData(ShelfPermission.AddOnly, new[] { ShelfOperation.Read, ShelfOperation.Add })]
        [InlineData(ShelfPermission.ReadWrite, new[] { ShelfOperation.Read, ShelfOperation.Add, ShelfOperation.Write })]
        public async Task ResolveAsync_Key_AllowsWhatTheKeySaysAndNeverAdministers(ShelfPermission permission, ShelfOperation[] expected)
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", permission);

            ShelfAccess access = await AccessResolver.ResolveAsync(ShelfCaller.ForKey((await KeyService.FindBySecretAsync(created.Secret))!), shelf);

            Assert.Equal(expected, Enum.GetValues<ShelfOperation>().Where(access.Allows));
        }

        [Fact]
        public async Task ResolveAsync_KeyOfAnotherShelf_ThrowsNotFound()
        {
            Shelf mine = await CreateShelfAsync("mine");
            Shelf other = await CreateShelfAsync("other");
            CreatedShelfKey created = await KeyService.CreateAsync(other, "app", ShelfPermission.ReadWrite);

            ShelfCaller caller = ShelfCaller.ForKey((await KeyService.FindBySecretAsync(created.Secret))!);

            await Assert.ThrowsAsync<NotFoundException>(() => AccessResolver.ResolveAsync(caller, mine));
        }

        [Fact]
        public async Task Require_OperationNotAllowed_ThrowsPermissionDenied()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");
            await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);
            ShelfAccess access = await AccessResolver.ResolveAsync(ShelfCaller.ForUser(user.Id, isAdmin: false), shelf);

            Assert.Throws<PermissionDeniedException>(() => access.Require(ShelfOperation.Add));
        }

        [Fact]
        public async Task GetAccessesAsync_UserWithOneGrant_ListsOnlyThatShelf()
        {
            Shelf granted = await CreateShelfAsync("granted");
            await CreateShelfAsync("hidden");
            User user = await CreateUserAsync("user@example.com");
            await GrantService.SetAsync(granted, user.Id, ShelfAccessPermission.Read);

            IReadOnlyList<ShelfAccess> visible = await AccessResolver.GetAccessesAsync(ShelfCaller.ForUser(user.Id, isAdmin: false));

            Assert.Equal("granted", Assert.Single(visible).Shelf.Name);
        }

        [Fact]
        public async Task GetAccessesAsync_Admin_ListsEveryShelfByName()
        {
            await CreateShelfAsync("b");
            await CreateShelfAsync("a");
            User admin = await CreateUserAsync("admin@example.com", UserRoles.Admin);

            IReadOnlyList<ShelfAccess> visible = await AccessResolver.GetAccessesAsync(ShelfCaller.ForUser(admin.Id, isAdmin: true));

            Assert.Equal(new[] { "a", "b" }, visible.Select(access => access.Shelf.Name));
        }

        [Fact]
        public async Task GetAccessesAsync_Key_ListsOnlyItsShelf()
        {
            await CreateShelfAsync("a");
            Shelf own = await CreateShelfAsync("b");
            CreatedShelfKey created = await KeyService.CreateAsync(own, "app", ShelfPermission.Read);

            IReadOnlyList<ShelfAccess> visible = await AccessResolver.GetAccessesAsync(ShelfCaller.ForKey((await KeyService.FindBySecretAsync(created.Secret))!));

            Assert.Equal("b", Assert.Single(visible).Shelf.Name);
        }

        [Fact]
        public async Task ResolveAsync_KeyOnTwoShelves_AllowsOnEachWhatItsGrantThereSays()
        {
            Shelf docs = await CreateShelfAsync("docs");
            Shelf backups = await CreateShelfAsync("backups");
            Shelf hidden = await CreateShelfAsync("hidden");
            CreatedShelfKey created = await KeyService.CreateAsync(docs, "app", ShelfPermission.Read);
            await KeyService.SetGrantAsync(backups, created.Key.Id, ShelfPermission.AddOnly);
            ShelfCaller caller = ShelfCaller.ForKey((await KeyService.FindBySecretAsync(created.Secret))!);

            ShelfAccess onDocs = await AccessResolver.ResolveAsync(caller, docs);
            ShelfAccess onBackups = await AccessResolver.ResolveAsync(caller, backups);

            Assert.Equal((true, false), (onDocs.Allows(ShelfOperation.Read), onDocs.Allows(ShelfOperation.Add)));
            Assert.Equal((true, false), (onBackups.Allows(ShelfOperation.Add), onBackups.Allows(ShelfOperation.Write)));
            await Assert.ThrowsAsync<NotFoundException>(() => AccessResolver.ResolveAsync(caller, hidden));
        }

        [Fact]
        public async Task GetAccessesAsync_KeyOnTwoShelves_AnswersBothByNameWithTheirOwnAccess()
        {
            Shelf docs = await CreateShelfAsync("docs");
            Shelf backups = await CreateShelfAsync("backups");
            await CreateShelfAsync("hidden");
            CreatedShelfKey created = await KeyService.CreateAsync(docs, "app", ShelfPermission.ReadWrite);
            await KeyService.SetGrantAsync(backups, created.Key.Id, ShelfPermission.Read);

            IReadOnlyList<ShelfAccess> accesses = await AccessResolver.GetAccessesAsync(ShelfCaller.ForKey((await KeyService.FindBySecretAsync(created.Secret))!));

            Assert.Equal(new[] { ("backups", false), ("docs", true) }, accesses.Select(access => (access.Shelf.Name, access.Allows(ShelfOperation.Write))));
        }

        [Fact]
        public async Task ResolveAsync_KeysGrantChanged_AllowsTheNewPermissionAtOnce()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfPermission.ReadWrite);
            ShelfCaller caller = ShelfCaller.ForKey((await KeyService.FindBySecretAsync(created.Secret))!);

            await KeyService.SetGrantAsync(shelf, created.Key.Id, ShelfPermission.Read);

            Assert.False((await AccessResolver.ResolveAsync(caller, shelf)).Allows(ShelfOperation.Add));
        }
    }
}
