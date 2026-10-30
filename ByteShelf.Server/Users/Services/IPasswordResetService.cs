namespace ByteShelf.Server.Users.Services
{
    public interface IPasswordResetService
    {
        /// <summary>
        /// Requests a password reset for the given email. Enumeration-safe: completes normally
        /// whether or not the email exists; a reset link is only actually sent for known emails.
        /// </summary>
        Task RequestResetAsync(string email);

        /// <summary>
        /// Completes a password reset: consumes the single-use token and sets the new password.
        /// Returns false when the token is unknown, already used, or expired.
        /// </summary>
        Task<bool> CompleteResetAsync(string token, string newPassword);
    }
}
