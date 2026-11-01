using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Exceptions;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using EasyReasy.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Users
{
    public class UserServiceTests : ServiceTestBase
    {
        private IUserService UserService => Services.GetRequiredService<IUserService>();

        [Fact]
        public async Task CreateUserAsync_ThenValidateCredentials_SignsInWithThatPassword()
        {
            User created = await UserService.CreateUserAsync("user@example.com", "User", "correct-password");

            UserCredentials? signedIn = await UserService.ValidateCredentialsAsync("USER@example.com", "correct-password");

            Assert.Equal(created.Id, signedIn?.User.Id);
        }

        [Fact]
        public async Task CreateUserAsync_StoresAHashNotThePassword()
        {
            await UserService.CreateUserAsync("user@example.com", "User", "correct-password");

            UserCredentials? credentials = await Services.GetRequiredService<IUserLoginRepository>().GetCredentialsByEmailAsync("user@example.com");

            Assert.NotNull(credentials);
            Assert.DoesNotContain("correct-password", credentials.PasswordHash, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ValidateCredentialsAsync_WrongPassword_ReturnsNull()
        {
            await UserService.CreateUserAsync("user@example.com", "User", "correct-password");

            Assert.Null(await UserService.ValidateCredentialsAsync("user@example.com", "wrong-password"));
        }

        [Fact]
        public async Task ValidateCredentialsAsync_UnknownEmail_ReturnsNull()
        {
            Assert.Null(await UserService.ValidateCredentialsAsync("nobody@example.com", "whatever-password"));
        }

        [Fact]
        public async Task CreateUserAsync_NoRolesGiven_CreatesAUserWithoutRoles()
        {
            User created = await UserService.CreateUserAsync("new@example.com", "New", "password123");

            Assert.Empty((await UserService.GetUserByIdAsync(created.Id))!.Roles);
        }

        [Fact]
        public async Task CreateUserAsync_EmailTakenInAnotherCase_ThrowsDuplicateEmail()
        {
            await UserService.CreateUserAsync("taken@example.com", "First", "password123");

            await Assert.ThrowsAsync<DuplicateEmailException>(() => UserService.CreateUserAsync("Taken@Example.com", "Second", "password123"));
        }

        [Theory]
        [InlineData("not-an-email", "Name", "password123")]
        [InlineData("new@example.com", "", "password123")]
        [InlineData("new@example.com", "  ", "password123")]
        [InlineData("new@example.com", "Na\tme", "password123")]
        [InlineData("new@example.com", "Name", "short")]
        public async Task CreateUserAsync_InvalidInput_ThrowsWithoutCreating(string email, string displayName, string password)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => UserService.CreateUserAsync(email, displayName, password));

            Assert.Equal(0, await UserService.GetUserCountAsync());
        }

        [Fact]
        public async Task CreateUserAsync_DisplayNameWithSurroundingSpace_StoresItTrimmed()
        {
            User user = await UserService.CreateUserAsync("new@example.com", "  Name  ", "password123");

            Assert.Equal("Name", (await UserService.GetUserByIdAsync(user.Id))!.DisplayName);
        }

        [Fact]
        public async Task CreateUserAsync_OverlongInput_Throws()
        {
            string longEmail = new string('a', UserFields.MaxEmailLength) + "@example.com";
            string longPassword = new string('p', PasswordPolicy.MaximumLength + 1);

            await Assert.ThrowsAsync<ArgumentException>(() => UserService.CreateUserAsync(longEmail, "Name", "password123"));
            await Assert.ThrowsAsync<ArgumentException>(() => UserService.CreateUserAsync("a@example.com", "Name", longPassword));
        }

        [Fact]
        public async Task CreateUserAsync_UnknownRole_ThrowsWithoutCreating()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => UserService.CreateUserAsync("new@example.com", "New", "password123", new[] { "superuser" }));

            Assert.Equal(0, await UserService.GetUserCountAsync());
        }

        [Fact]
        public async Task UpdatePasswordAsync_CorrectCurrentPassword_SignsInWithTheNewOneOnly()
        {
            User user = await UserService.CreateUserAsync("user@example.com", "User", "old-password");

            bool updated = await UserService.UpdatePasswordAsync(user.Id, "old-password", "new-password");

            Assert.True(updated);
            Assert.NotNull(await UserService.ValidateCredentialsAsync("user@example.com", "new-password"));
            Assert.Null(await UserService.ValidateCredentialsAsync("user@example.com", "old-password"));
        }

        [Fact]
        public async Task UpdatePasswordAsync_WrongCurrentPassword_KeepsTheOldPassword()
        {
            User user = await UserService.CreateUserAsync("user@example.com", "User", "old-password");

            bool updated = await UserService.UpdatePasswordAsync(user.Id, "wrong-password", "new-password");

            Assert.False(updated);
            Assert.NotNull(await UserService.ValidateCredentialsAsync("user@example.com", "old-password"));
        }

        [Fact]
        public async Task UpdateRolesAsync_SecondAdminDemoted_RemovesTheRole()
        {
            await UserService.CreateUserAsync("first@example.com", "First", "password123", new[] { UserRoles.Admin });
            User second = await UserService.CreateUserAsync("second@example.com", "Second", "password123", new[] { UserRoles.Admin });

            await UserService.UpdateRolesAsync(second.Id, Array.Empty<string>());

            Assert.Empty((await UserService.GetUserByIdAsync(second.Id))!.Roles);
        }

        [Fact]
        public async Task UpdateRolesAsync_LastAdminDemoted_ThrowsConflictAndKeepsTheRole()
        {
            User admin = await UserService.CreateUserAsync("admin@example.com", "Admin", "password123", new[] { UserRoles.Admin });

            await Assert.ThrowsAsync<ConflictException>(() => UserService.UpdateRolesAsync(admin.Id, Array.Empty<string>()));

            Assert.Equal(new[] { UserRoles.Admin }, (await UserService.GetUserByIdAsync(admin.Id))!.Roles);
        }

        [Fact]
        public async Task DeleteUserAsync_Member_RemovesThemAndTheirAccess()
        {
            Shelf shelf = await CreateShelfAsync();
            User member = await UserService.CreateUserAsync("member@example.com", "Member", "correct-password");
            await GrantService.SetAsync(shelf, member.Id, ShelfAccessPermission.Read);

            await UserService.DeleteUserAsync(member.Id);

            Assert.Null(await UserService.GetUserByIdAsync(member.Id));
            Assert.Null(await UserService.ValidateCredentialsAsync("member@example.com", "correct-password"));
            Assert.Empty(await GrantService.GetForShelfAsync(shelf));
        }

        [Fact]
        public async Task DeleteUserAsync_Member_EndsEverySession()
        {
            User member = await UserService.CreateUserAsync("member@example.com", "Member", "correct-password");
            RefreshTokenCreationResult session = await Services.GetRequiredService<IRefreshTokenService>().CreateRefreshTokenWithFamilyAsync(
                member.Id.ToString("D"), AuthRequestValidationService.AuthType, null, RefreshTokenClaims.SerializeRoles(Array.Empty<string>()));

            await UserService.DeleteUserAsync(member.Id);

            StoredRefreshToken? stored = await Services.GetRequiredService<IRefreshTokenStore>().GetByTokenHashAsync(SecretHash.Sha256Hex(session.RawToken));
            Assert.True(stored?.IsInvalidated);
        }

        [Fact]
        public async Task DeleteUserAsync_LastAdmin_ThrowsConflictAndKeepsThem()
        {
            User admin = await UserService.CreateUserAsync("admin@example.com", "Admin", "correct-password", new[] { UserRoles.Admin });

            await Assert.ThrowsAsync<ConflictException>(() => UserService.DeleteUserAsync(admin.Id));

            Assert.NotNull(await UserService.GetUserByIdAsync(admin.Id));
        }

        [Fact]
        public async Task DeleteUserAsync_OneOfTwoAdmins_RemovesThem()
        {
            User first = await UserService.CreateUserAsync("first@example.com", "First", "correct-password", new[] { UserRoles.Admin });
            await UserService.CreateUserAsync("second@example.com", "Second", "correct-password", new[] { UserRoles.Admin });

            await UserService.DeleteUserAsync(first.Id);

            Assert.Null(await UserService.GetUserByIdAsync(first.Id));
        }

        [Fact]
        public async Task DeleteUserAsync_UnknownUser_ThrowsNotFound()
        {
            await Assert.ThrowsAsync<NotFoundException>(() => UserService.DeleteUserAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task UpdateRolesAsync_UnknownUser_ThrowsNotFound()
        {
            await Assert.ThrowsAsync<NotFoundException>(() => UserService.UpdateRolesAsync(Guid.NewGuid(), Array.Empty<string>()));
        }

        [Fact]
        public async Task UpdateRolesAsync_UnknownRole_Throws()
        {
            User user = await UserService.CreateUserAsync("user@example.com", "User", "password123");

            await Assert.ThrowsAsync<ArgumentException>(() => UserService.UpdateRolesAsync(user.Id, new[] { "superuser" }));
        }
    }
}
