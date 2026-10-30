namespace ByteShelf.Server.Users.Models
{
    public class CompletePasswordResetRequest
    {
        public required string Token { get; init; }
        public required string NewPassword { get; init; }
    }
}
