using EasyReasy.EnvironmentVariables;

namespace ByteShelf.Server.Email
{
    /// <summary>
    /// How the server sends mail: exactly one of <see cref="PostmarkEmail"/> (sent through Postmark),
    /// <see cref="DevMailEmail"/> (written to remote-claude's dev mail folder) or <see cref="LogOnlyEmail"/>
    /// (only logged). Read from the environment once by <see cref="FromEnvironment"/>, which holds every
    /// rule about which settings go together, so each sender takes only its own, complete settings. A
    /// test passes <see cref="None"/>, so an ambient token on the machine running it is never used.
    /// </summary>
    public abstract record EmailOptions
    {
        /// <summary>The sender's name when <c>EMAIL_FROM_NAME</c> is unset.</summary>
        public const string DefaultFromName = "ByteShelf";

        /// <summary>
        /// The sender a dev mail file names when <c>EMAIL_FROM_ADDRESS</c> is unset. Nothing is ever sent
        /// from it: it only fills the file's <c>fromEmail</c>, which the contract requires.
        /// </summary>
        public const string DevMailFromAddress = "byteshelf@localhost";

        /// <summary>Mail only to the log.</summary>
        public static EmailOptions None { get; } = new LogOnlyEmail();

        /// <summary>The settings the environment names; see <see cref="FromValues"/>.</summary>
        /// <exception cref="InvalidOperationException"><c>POSTMARK_API_KEY</c> is set without <c>EMAIL_FROM_ADDRESS</c>.</exception>
        public static EmailOptions FromEnvironment()
        {
            return FromValues(
                EnvironmentVariables.PostmarkApiKey.GetValueOrDefault(),
                EnvironmentVariables.EmailFromAddress.GetValueOrDefault(),
                EnvironmentVariables.EmailFromName.GetValueOrDefault(),
                EnvironmentVariables.DevMailDirectory.GetValueOrDefault());
        }

        /// <summary>
        /// Postmark when a token is given, which then needs a sender address; else the dev mail folder
        /// when one is given; else the log. Values are trimmed, and blank ones count as unset, so an
        /// empty line in a launch profile switches nothing on.
        /// </summary>
        /// <exception cref="InvalidOperationException">A token is given without a sender address.</exception>
        internal static EmailOptions FromValues(string? postmarkApiKey, string? fromAddress, string? fromName, string? devMailDirectory)
        {
            string? apiKey = ValueOf(postmarkApiKey);
            string? address = ValueOf(fromAddress);
            string name = ValueOf(fromName) ?? DefaultFromName;
            string? directory = ValueOf(devMailDirectory);

            if (apiKey != null)
            {
                return new PostmarkEmail(
                    apiKey,
                    new MailSender(address ?? throw new InvalidOperationException("EMAIL_FROM_ADDRESS must be set when POSTMARK_API_KEY is: Postmark sends only from a confirmed sender."), name));
            }

            // In any environment, unlike LoggingEmailSender's links in the log: only remote-claude sets the
            // variable, for a server it runs whatever its environment, the files are the owner's only, and
            // the startup warning names the folder.
            return directory != null ? new DevMailEmail(directory, new MailSender(address ?? DevMailFromAddress, name)) : None;
        }

        private static string? ValueOf(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>The address and name mail is from.</summary>
    public sealed record MailSender(string Address, string Name);

    /// <summary>Mail sent through Postmark with a server token, from a sender confirmed there.</summary>
    public sealed record PostmarkEmail(string ApiKey, MailSender From) : EmailOptions;

    /// <summary>Mail written into the folder remote-claude reads a dev server's mail from, never sent.</summary>
    public sealed record DevMailEmail(string Directory, MailSender From) : EmailOptions;

    /// <summary>Mail only logged, never sent: what the log may hold is <c>LoggingEmailSender</c>'s rule.</summary>
    public sealed record LogOnlyEmail : EmailOptions;
}
