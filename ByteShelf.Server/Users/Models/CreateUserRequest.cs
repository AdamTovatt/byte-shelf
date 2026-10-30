namespace ByteShelf.Server.Users.Models
{
    public class CreateUserRequest
    {
        public required string Email { get; init; }
        public required string DisplayName { get; init; }
        public required string Password { get; init; }
        public IEnumerable<string>? Roles { get; init; }
    }
}
