using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Exceptions;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelf.Server.Users.Services;
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

            User? signedIn = await UserService.ValidateCredentialsAsync("USER@example.com", "correct-password");

            Assert.Equal(created.Id, signedIn?.Id);
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
        [InlineData("new@example.com", "Name", "short")]
        public async Task CreateUserAsync_InvalidInput_ThrowsWithoutCreating(string email, string displayName, string password)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => UserService.CreateUserAsync(email, displayName, password));

            Assert.Equal(0, await UserService.GetUserCountAsync());
        }

        [Fact]
        public async Task CreateUserAsync_OverlongInput_Throws()
        {
            string longEmail = new string('a', ByteShelf.Server.Users.Services.UserService.MaxEmailLength) + "@example.com";
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
            await UserService.CreateUserAsync("first@example.com", "First", "password123", new[] { UserRole.Admin });
            User second = await UserService.CreateUserAsync("second@example.com", "Second", "password123", new[] { UserRole.Admin });

            await UserService.UpdateRolesAsync(second.Id, Array.Empty<string>());

            Assert.Empty((await UserService.GetUserByIdAsync(second.Id))!.Roles);
        }

        [Fact]
        public async Task UpdateRolesAsync_LastAdminDemoted_ThrowsConflictAndKeepsTheRole()
        {
            User admin = await UserService.CreateUserAsync("admin@example.com", "Admin", "password123", new[] { UserRole.Admin });

            await Assert.ThrowsAsync<ConflictException>(() => UserService.UpdateRolesAsync(admin.Id, Array.Empty<string>()));

            Assert.Equal(new[] { UserRole.Admin }, (await UserService.GetUserByIdAsync(admin.Id))!.Roles);
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
