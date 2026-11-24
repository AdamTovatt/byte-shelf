using ByteShelf.Server.Email;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Users.Models;
using EasyReasy.EnvironmentVariables;

namespace ByteShelf.Server
{
    /// <summary>
    /// What the server's services are registered for: a root, and everything an instance is set up with.
    /// Registering them reads nothing else, so only <see cref="FromEnvironment"/> is the machine's doing,
    /// and whatever else registers them (the root commands, <c>--check-plugin</c>, the tests) is the same
    /// on every machine.
    /// </summary>
    internal sealed record ServerSettings
    {
        private ServerSettings(RootFolder root, string jwtSecret)
        {
            Root = root;
            JwtSecret = jwtSecret;
        }

        public RootFolder Root { get; }

        /// <summary>What sessions are signed with.</summary>
        public string JwtSecret { get; }

        public PublicAddress PublicAddress { get; init; } = new PublicAddress(null);

        public EmailOptions Email { get; init; } = EmailOptions.None;

        public StorageOptions Storage { get; init; } = new StorageOptions();

        /// <summary><see cref="ReplicationOptions.AlertAfter"/>.</summary>
        public TimeSpan ReplicationAlertAfter { get; init; } = ReplicationOptions.DefaultAlertAfter;

        public InitialAdminSettings InitialAdmin { get; init; } = new InitialAdminSettings(null, null, null);

        /// <summary><c>BYTESHELF_TRUSTED_PROXIES</c>, null for loopback alone.</summary>
        public string? TrustedProxies { get; init; }

        /// <summary>The folder of plugins, null for none.</summary>
        public string? PluginsPath { get; init; }

        public ReplicationOptions Replication => new ReplicationOptions { PublicUrl = PublicAddress.Absolute.AbsoluteUri, AlertAfter = ReplicationAlertAfter };

        /// <summary>The instance at <paramref name="root"/>, with the secret it has or is now given, and nothing set.</summary>
        public static ServerSettings On(RootFolder root)
        {
            return new ServerSettings(root, RootKeyMaterial.GetOrCreateJwtSecret(root));
        }

        /// <summary>No instance: its services can be registered, never run, and nothing is made or opened.</summary>
        public static ServerSettings OnNoRoot()
        {
            return new ServerSettings(RootFolder.At("no-root"), RandomSecret.Generate());
        }

        /// <summary>The instance at <paramref name="root"/> as the environment sets it up.</summary>
        /// <exception cref="InvalidOperationException">A setting is missing or holds what it may not.</exception>
        public static ServerSettings FromEnvironment(RootFolder root, IHostEnvironment environment)
        {
            return On(root) with
            {
                PublicAddress = PublicAddress.Resolve(EnvironmentVariables.AppBaseUrl.GetValueOrDefault(), environment),
                Email = EmailOptions.FromEnvironment(),
                Storage = StorageOptions.FromEnvironment(),
                ReplicationAlertAfter = ReplicationOptions.AlertAfterFrom(EnvironmentVariables.ReplicationAlertMinutes.GetValueOrDefault()),
                InitialAdmin = InitialAdminSettings.FromEnvironment(),
                TrustedProxies = EnvironmentVariables.TrustedProxies.GetValueOrDefault(),
                PluginsPath = EnvironmentVariables.Plugins.GetValueOrDefault(),
            };
        }
    }
}
