namespace ByteShelf.Server.Users.Models
{
    /// <summary>
    /// A user with its local-provider password hash, used only on the authentication path.
    /// </summary>
    public class UserCredentials
    {
        public required User User { get; init; }
        public required string PasswordHash { get; init; }
    }
}
