using EasyReasy.EnvironmentVariables;

namespace ByteShelf.Server
{
    [EnvironmentVariableNameContainer]
    public static class EnvironmentVariables
    {
        // The folder that holds the whole instance (database, objects, staging, keys); see RootFolder.
        [EnvironmentVariableName(minLength: 1)]
        public static readonly VariableName ByteShelfRoot = new VariableName("BYTESHELF_ROOT");

        // First-run admin bootstrap: read by InitialAccountSeeder only while there are no users, so
        // both can be removed once the admin exists. The display name falls back to the email's
        // local part.
        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName InitialAdminEmail = new VariableName("INITIAL_ADMIN_EMAIL");

        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName InitialAdminPassword = new VariableName("INITIAL_ADMIN_PASSWORD");

        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName InitialAdminDisplayName = new VariableName("INITIAL_ADMIN_DISPLAY_NAME");

        // Public base URL of the deployment (e.g. https://shelf.example.com), which password-reset
        // links point at. Required outside Development, where it falls back to the local dev URL.
        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName AppBaseUrl = new VariableName("APP_BASE_URL");

        // How long a deleted file or directory can be restored before it is purged. Whole days, at
        // most StorageOptions.MaxRestoreWindowDays; defaults to StorageOptions.DefaultRestoreWindowDays.
        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName RestoreWindowDays = new VariableName("BYTESHELF_RESTORE_WINDOW_DAYS");

        // How many minutes replication may go wrong (a replica unseen or behind, or a replica unable to
        // follow its primary) before the admins are emailed. Whole minutes, at most
        // ReplicationOptions.MaxAlertAfterMinutes; defaults to ReplicationOptions.DefaultAlertAfter.
        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName ReplicationAlertMinutes = new VariableName("BYTESHELF_REPLICATION_ALERT_MINUTES");

        // Comma-separated IP addresses or CIDR ranges of the reverse proxies in front of the server.
        // Their X-Forwarded-For and X-Forwarded-Proto are trusted, so rate limits and the sign-in
        // delay count the real client. Unset: only loopback proxies are trusted.
        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName TrustedProxies = new VariableName("BYTESHELF_TRUSTED_PROXIES");

        // Email (see Email/Email.md). With a Postmark server token, mail is sent through Postmark from
        // EMAIL_FROM_ADDRESS, which is then required, under EMAIL_FROM_NAME (default "ByteShelf").
        // Without one, a folder in REMOTE_CLAUDE_DEV_MAIL_DIR (set only by remote-claude on a dev
        // server it starts) gets each message as a JSON file; with neither, mail is only logged.
        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName PostmarkApiKey = new VariableName("POSTMARK_API_KEY");

        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName EmailFromAddress = new VariableName("EMAIL_FROM_ADDRESS");

        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName EmailFromName = new VariableName("EMAIL_FROM_NAME");

        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName DevMailDirectory = new VariableName("REMOTE_CLAUDE_DEV_MAIL_DIR");
    }
}
