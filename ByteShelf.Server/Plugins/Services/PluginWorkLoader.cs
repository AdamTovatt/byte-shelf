using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>Loads a plugin in its process, with everything it may be asked to do there.</summary>
    internal static class PluginWorkLoader
    {
        /// <summary>
        /// The works of the plugin in a folder: every <see cref="ProcessWork"/> the server's modules register. It registers
        /// what it adds as it does in the server, among the server's own registrations, on no root; of those, only the
        /// works are made, and of the plugin's, only its reader and maker.
        /// </summary>
        /// <exception cref="PluginRefusedException">It cannot be loaded.</exception>
        public static PluginWorks Load(string folder)
        {
            IServiceCollection services = Program.AddServerServices(Program.CreateBuilder([]).Services, ServerSettings.OnNoRoot()).Services;
            PluginBuilder registered = PluginLoader.RegisterForProcess(PluginManifestReader.Read(folder), services);
            using ServiceProvider provider = services.BuildServiceProvider();
            return PluginWorks.Of(provider.GetServices<ProcessWork>(), new PluginRegistrations(registered.Reader, registered.Maker));
        }
    }
}
