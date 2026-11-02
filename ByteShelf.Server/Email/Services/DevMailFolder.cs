using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Shared.IO;

namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// Writes a message the server did not send into the folder remote-claude hands a dev server
    /// (<c>REMOTE_CLAUDE_DEV_MAIL_DIR</c>), as one JSON file remote-claude renders. The contract is
    /// remote-claude's, documented in its <c>DevServer.md</c>, so a change here is a change to that contract.
    /// </summary>
    /// <remarks>
    /// The file is written to a <c>.tmp</c> sibling and renamed into place in one step, so a reader
    /// listing <c>*.json</c> never sees a half-written file, and the temporary file never outlives the
    /// call. It is owner-only from creation, which suits a file carrying a password-reset link, and
    /// remote-claude reads it as the same user. Order and time come from <c>sentAt</c>; the file name
    /// only has to be unique.
    /// </remarks>
    public static class DevMailFolder
    {
        private const int FileVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,

            // The file is read by remote-claude and by agents looking for a link in it, never embedded in a
            // page, so HTML-sensitive characters are written as themselves rather than as \u escapes.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Writes <paramref name="message"/>, from <paramref name="from"/>, into <paramref name="directory"/>,
        /// creating it owner-only if needed (a folder that exists keeps its own permissions), and returns
        /// the file's path.
        /// </summary>
        /// <param name="sentAt">The instant stamped as <c>sentAt</c>, which also orders the file name.</param>
        public static async Task<string> WriteAsync(
            string directory,
            EmailMessage message,
            MailSender from,
            DateTimeOffset sentAt,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(from);

            string fileName = $"{sentAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.json";
            string path = Path.Combine(directory, fileName);

            DevMailFile file = new DevMailFile
            {
                Version = FileVersion,
                SentAt = sentAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                To = message.To,
                FromEmail = from.Address,
                FromName = from.Name,
                Subject = message.Subject,
                Html = message.HtmlBody,
                Text = message.TextBody,
                Tag = message.Tag,
            };

            OwnerOnly.CreateDirectory(directory);
            OwnerOnly.WriteThenRename(path, JsonSerializer.Serialize(file, JsonOptions));
            return path;
        }

        /// <summary>
        /// One written message, shaped by remote-claude's dev-mail file contract. Property names are pinned
        /// rather than left to a naming policy, because the contract is the other side's.
        /// </summary>
        private sealed class DevMailFile
        {
            [JsonPropertyName("version")]
            public required int Version { get; init; }

            [JsonPropertyName("sentAt")]
            public required string SentAt { get; init; }

            [JsonPropertyName("to")]
            public required string To { get; init; }

            [JsonPropertyName("fromEmail")]
            public required string FromEmail { get; init; }

            [JsonPropertyName("fromName")]
            public required string FromName { get; init; }

            [JsonPropertyName("subject")]
            public required string Subject { get; init; }

            [JsonPropertyName("html")]
            public required string Html { get; init; }

            [JsonPropertyName("text")]
            public required string? Text { get; init; }

            [JsonPropertyName("tag")]
            public required string? Tag { get; init; }
        }
    }
}
