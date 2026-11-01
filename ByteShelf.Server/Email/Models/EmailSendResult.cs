namespace ByteShelf.Server.Email.Models
{
    /// <summary>
    /// Represents the result of attempting to send an email.
    /// </summary>
    public class EmailSendResult
    {
        /// <summary>
        /// Gets a value indicating whether the email was sent successfully.
        /// </summary>
        public required bool IsSuccess { get; init; }

        /// <summary>
        /// Gets the provider message id if the email was sent successfully. Null for the logging
        /// dev sender or failures.
        /// </summary>
        public string? MessageId { get; init; }

        /// <summary>
        /// Gets the error message if the send failed. Null on success.
        /// </summary>
        public string? ErrorMessage { get; init; }
    }
}
