namespace ByteShelf.Server.Email.Models
{
    /// <summary>
    /// A transactional email to be sent through <see cref="Services.IEmailSender"/>.
    /// </summary>
    public class EmailMessage
    {
        public required string To { get; init; }
        public required string Subject { get; init; }
        public required string HtmlBody { get; init; }
        public required string TextBody { get; init; }

        /// <summary>
        /// Optional provider-side categorization tag (e.g. "password-reset") for delivery analytics.
        /// </summary>
        public string? Tag { get; init; }
    }
}
