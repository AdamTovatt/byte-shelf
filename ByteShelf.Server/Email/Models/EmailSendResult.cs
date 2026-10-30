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

        /// <summary>
        /// Gets a value indicating whether a failed send is worth retrying.
        /// <c>true</c> for transient failures (network blip, provider 5xx,
        /// maintenance, throttling). <c>false</c> for permanent failures
        /// (inactive recipient, malformed request, bad sender — anything a
        /// retry will not fix). Always <c>true</c> on success — callers
        /// should branch on <see cref="IsSuccess"/> first.
        /// </summary>
        public bool IsTransient { get; init; } = true;
    }
}
