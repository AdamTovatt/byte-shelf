using ByteShelfCommon;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>The plugins there are, or that it is not known which: the server loads them, a command and a backup do not.</summary>
    public sealed class KnownPlugins
    {
        private readonly InstalledPlugins? _installed;

        private KnownPlugins(InstalledPlugins? installed)
        {
            _installed = installed;
        }

        /// <summary>Where nothing loads plugins, which is not that there are none.</summary>
        public static KnownPlugins NotKnown { get; } = new KnownPlugins(null);

        /// <exception cref="InvalidOperationException">They are not known here.</exception>
        public InstalledPlugins Installed => _installed ?? throw new InvalidOperationException("The plugins are not known here.");

        public static KnownPlugins Of(InstalledPlugins installed)
        {
            return new KnownPlugins(installed);
        }
    }
}
