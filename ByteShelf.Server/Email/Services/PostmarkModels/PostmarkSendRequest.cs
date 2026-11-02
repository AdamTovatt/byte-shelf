namespace ByteShelf.Server.Email.Services.PostmarkModels
{
    /// <summary>
    /// Request body for Postmark's <c>POST /email</c>.
    /// </summary>
    /// <remarks>
    /// An absent tracking field is not a neutral one: Postmark reads it as "use this Server's dashboard
    /// setting", which nothing here controls. So both tracking members are <c>required</c>, and both are
    /// off; <c>Email/Email.md</c> says why.
    /// </remarks>
    internal sealed class PostmarkSendRequest
    {
        /// <summary>The sender as <c>Name &lt;address&gt;</c>.</summary>
        public required string From { get; init; }

        public required string To { get; init; }

        public required string Subject { get; init; }

        public string? HtmlBody { get; init; }

        public string? TextBody { get; init; }

        /// <summary>Postmark's category for the message, such as <c>password-reset</c>.</summary>
        public string? Tag { get; init; }

        /// <summary>Whether Postmark embeds an open-tracking pixel. Always false.</summary>
        public required bool TrackOpens { get; init; }

        /// <summary>Which bodies Postmark rewrites links in. Always <see cref="NoLinkTracking"/>.</summary>
        public required string TrackLinks { get; init; }

        /// <summary>Postmark's spelling for "rewrite no links", a value in its API rather than an enum of ours.</summary>
        public const string NoLinkTracking = "None";
    }
}
