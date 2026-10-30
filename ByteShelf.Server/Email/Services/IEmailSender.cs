using ByteShelf.Server.Email.Models;

namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// General outbound-email seam. Password reset is its first consumer; invitations and
    /// notifications reuse it. The base registration is <see cref="LoggingEmailSender"/> (dev
    /// only); production apps swap in a concrete provider (SMTP, Postmark, …) via DI — no code
    /// or schema change.
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>
        /// Sends a transactional email message.
        /// </summary>
        /// <param name="message">The email message to send.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The result of the send operation.</returns>
        Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }
}
