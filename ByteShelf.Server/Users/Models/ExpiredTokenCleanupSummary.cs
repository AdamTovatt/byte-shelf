namespace ByteShelf.Server.Users.Models
{
    /// <summary>How many expired tokens one cleanup pass deleted.</summary>
    public record ExpiredTokenCleanupSummary(int RefreshTokens, int PasswordResetTokens);
}
