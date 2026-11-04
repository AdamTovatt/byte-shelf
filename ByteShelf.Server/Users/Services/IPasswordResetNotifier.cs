namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Delivers the password-reset link to the user. The base implementation sends email through
    /// <see cref="Email.Services.IEmailSender"/>.
    /// </summary>
    public interface IPasswordResetNotifier
    {
        Task SendResetLinkAsync(string email, string resetToken);
    }
}
