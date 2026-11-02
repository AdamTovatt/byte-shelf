using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Shared;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Sends the password-reset link through <see cref="IEmailSender"/>. The link points at the
    /// frontend's <c>/reset-password</c> route on the server's <see cref="PublicAddress"/>.
    /// </summary>
    public class EmailPasswordResetNotifier : IPasswordResetNotifier
    {
        private readonly IEmailSender _emailSender;
        private readonly PublicAddress _publicAddress;

        public EmailPasswordResetNotifier(IEmailSender emailSender, PublicAddress publicAddress)
        {
            _emailSender = emailSender;
            _publicAddress = publicAddress;
        }

        /// <inheritdoc />
        public async Task SendResetLinkAsync(string email, string resetToken)
        {
            string resetUrl = _publicAddress.AbsoluteLinkTo($"/reset-password?token={Uri.EscapeDataString(resetToken)}");
            string lifetimeText = FormatLifetime(PasswordResetService.TokenLifetime);

            string textBody = $"You requested a password reset. Visit this link to set a new password: {resetUrl}";
            string htmlBody = $"""
                <p>We received a request to reset your password.</p>
                <p><a href="{resetUrl}">Reset your password</a></p>
                <p>If you didn't request this, you can safely ignore this email. The link expires in {lifetimeText}.</p>
                """;

            EmailMessage message = new EmailMessage
            {
                To = email,
                Subject = "Reset your password",
                HtmlBody = htmlBody,
                TextBody = textBody,
                Tag = "password-reset",
            };

            EmailSendResult result = await _emailSender.SendAsync(message);

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to send password reset email to {email}: {result.ErrorMessage}");
            }
        }

        private static string FormatLifetime(TimeSpan lifetime)
        {
            if (lifetime.TotalHours >= 1 && lifetime.TotalHours == Math.Floor(lifetime.TotalHours))
            {
                int hours = (int)lifetime.TotalHours;
                return hours == 1 ? "1 hour" : $"{hours} hours";
            }

            int minutes = (int)lifetime.TotalMinutes;
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }
    }
}

