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

        // How long a deleted file or directory can be restored before it is purged. Whole days;
        // defaults to StorageOptions.DefaultRestoreWindowDays.
        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName RestoreWindowDays = new VariableName("BYTESHELF_RESTORE_WINDOW_DAYS");

        // Comma-separated IP addresses or CIDR ranges of the reverse proxies in front of the server.
        // Their X-Forwarded-For and X-Forwarded-Proto are trusted, so rate limits and the sign-in
        // delay count the real client. Unset: only loopback proxies are trusted.
        [EnvironmentVariableName(optional: true)]
        public static readonly VariableName TrustedProxies = new VariableName("BYTESHELF_TRUSTED_PROXIES");
    }
}
