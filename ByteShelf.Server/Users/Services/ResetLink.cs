namespace ByteShelf.Server.Users.Services
{
    /// <summary>A reset token to send to the address it was issued for.</summary>
    public record ResetLink(string Email, string Token);
}
