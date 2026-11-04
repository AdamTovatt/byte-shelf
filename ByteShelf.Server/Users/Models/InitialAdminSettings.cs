using EasyReasy.EnvironmentVariables;

namespace ByteShelf.Server.Users.Models
{
    /// <summary>The first admin's account, created by the seeder while there are no users.</summary>
    public record InitialAdminSettings(string? Email, string? Password, string? DisplayName)
    {
        /// <summary>From <c>INITIAL_ADMIN_EMAIL</c>, <c>INITIAL_ADMIN_PASSWORD</c> and <c>INITIAL_ADMIN_DISPLAY_NAME</c>.</summary>
        public static InitialAdminSettings FromEnvironment()
        {
            return new InitialAdminSettings(
                EnvironmentVariables.InitialAdminEmail.GetValueOrDefault(),
                EnvironmentVariables.InitialAdminPassword.GetValueOrDefault(),
                EnvironmentVariables.InitialAdminDisplayName.GetValueOrDefault());
        }
    }
}
