namespace ByteShelf.Server.Email.Services.PostmarkModels
{
    /// <summary>Response body from Postmark's <c>POST /email</c>.</summary>
    internal sealed class PostmarkSendResponse
    {
        /// <summary>The id Postmark gave the message.</summary>
        public string? MessageID { get; init; }

        /// <summary>Postmark's error code; zero is success.</summary>
        public int ErrorCode { get; init; }

        /// <summary>Postmark's status or error message.</summary>
        public string? Message { get; init; }
    }
}
