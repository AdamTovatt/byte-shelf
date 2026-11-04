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
        [InlineData(ShelfKeyPermission.Read, new[] { ShelfOperation.Read })]
        [InlineData(ShelfKeyPermission.AddOnly, new[] { ShelfOperation.Read, ShelfOperation.Add })]
        [InlineData(ShelfKeyPermission.ReadWrite, new[] { ShelfOperation.Read, ShelfOperation.Add, ShelfOperation.Write })]
        public async Task ResolveAsync_Key_AllowsWhatTheKeySaysAndNeverAdministers(ShelfKeyPermission permission, ShelfOperation[] expected)
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
            CreatedShelfKey created = await KeyService.CreateAsync(other, "app", ShelfKeyPermission.ReadWrite);

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
        public async Task GetVisibleShelvesAsync_UserWithOneGrant_ListsOnlyThatShelf()
        {
            Shelf granted = await CreateShelfAsync("granted");
            await CreateShelfAsync("hidden");
            User user = await CreateUserAsync("user@example.com");
            await GrantService.SetAsync(granted, user.Id, ShelfAccessPermission.Read);

            IReadOnlyList<Shelf> visible = await AccessResolver.GetVisibleShelvesAsync(ShelfCaller.ForUser(user.Id, isAdmin: false));

            Assert.Equal("granted", Assert.Single(visible).Name);
        }

        [Fact]
        public async Task GetVisibleShelvesAsync_Admin_ListsEveryShelfByName()
        {
            await CreateShelfAsync("b");
            await CreateShelfAsync("a");
            User admin = await CreateUserAsync("admin@example.com", UserRoles.Admin);

            IReadOnlyList<Shelf> visible = await AccessResolver.GetVisibleShelvesAsync(ShelfCaller.ForUser(admin.Id, isAdmin: true));

            Assert.Equal(new[] { "a", "b" }, visible.Select(shelf => shelf.Name));
        }

        [Fact]
        public async Task GetVisibleShelvesAsync_Key_ListsOnlyItsShelf()
        {
            await CreateShelfAsync("a");
            Shelf own = await CreateShelfAsync("b");
            CreatedShelfKey created = await KeyService.CreateAsync(own, "app", ShelfKeyPermission.Read);

            IReadOnlyList<Shelf> visible = await AccessResolver.GetVisibleShelvesAsync(ShelfCaller.ForKey((await KeyService.FindBySecretAsync(created.Secret))!));

            Assert.Equal("b", Assert.Single(visible).Name);
        }
    }
}
