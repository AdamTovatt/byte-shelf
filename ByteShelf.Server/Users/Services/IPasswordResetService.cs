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

        /// <summary>
        /// Sets a user's password without the current one or a reset link, as the server's
        /// <c>--reset-password</c> does for whoever runs it on the server's machine. Like a reset, it
        /// ends every session and every reset link of theirs. Returns false when there is no such user.
        /// </summary>
        /// <exception cref="ArgumentException">The password breaks <see cref="ByteShelfCommon.PasswordPolicy"/>.</exception>
        Task<bool> SetPasswordAsync(Guid userId, string newPassword);
    }
}
