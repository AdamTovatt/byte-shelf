using ByteShelf.Server.Email.Models;

namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// The one seam all of the server's outbound email goes through. The base registration is
    /// <see cref="LoggingEmailSender"/> (dev only); production apps swap in a concrete provider (SMTP,
    /// Postmark, …) via DI — no code or schema change.
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>
        /// Sends a transactional email message. A send that fails is answered as a failed result, never
        /// thrown, so a caller sending to several people goes on to the rest; only cancellation throws.
        /// </summary>
        /// <param name="message">The email message to send.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The result of the send operation.</returns>
        Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }
}
