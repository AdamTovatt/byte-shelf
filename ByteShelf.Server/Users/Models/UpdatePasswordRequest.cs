namespace ByteShelf.Server.Users.Models
{
    public class UpdatePasswordRequest
    {
        public required string CurrentPassword { get; init; }
        public required string NewPassword { get; init; }
    }
}
