using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>Loads a plugin in its process, with everything it may be asked to do there.</summary>
    internal static class PluginWorkLoader
    {
        /// <summary>
        /// The work of the plugin in a folder. It registers what it adds as it does in the server, among the server's own
        /// registrations, on no root; none of its services or the server's is made, only its reader and maker.
        /// </summary>
        /// <exception cref="PluginRefusedException">It cannot be loaded.</exception>
        public static PluginWork Load(string folder)
        {
            IServiceCollection services = Program.AddServerServices(Program.CreateBuilder([]).Services, ServerSettings.OnNoRoot()).Services;
            PluginBuilder registered = PluginLoader.RegisterForProcess(PluginManifestReader.Read(folder), services);
            return Of(registered.Reader, registered.Maker);
        }

        /// <summary>
        /// The work of a plugin that registered these. Both are made now, so that one that cannot be is not the
        /// contents' doing; it is not asked for the other's.
        /// </summary>
        public static PluginWork Of(PluginReaderRegistration? reader, PluginMakerRegistration? maker)
        {
            Func<IContentDocumentReader> readerMade = MakeNow("reader", reader?.Make);
            Func<IDerivedFileMaker> makerMade = MakeNow("maker", maker?.Make);
            return (work, arguments, contents, folder) => work switch
            {
                ReadWork.Name => ReadWork.RunAsync(readerMade(), reader!.Version, arguments, contents),
                MakeWork.Name => MakeWork.RunAsync(makerMade(), maker!.Makes, arguments, contents, folder),
                _ => throw new PluginUnavailableException($"it does not do '{work}'"),
            };
        }

        /// <summary>Makes it, and answers it whenever it is asked for, or why it cannot be.</summary>
        private static Func<T> MakeNow<T>(string what, Func<T>? make)
        {
            try
            {
                if (make == null)
                {
                    return () => throw new PluginUnavailableException($"it has no {what}");
                }

                T instance = make();
                return () => instance;
            }
            catch (Exception exception)
            {
                string reason = PluginFailure.CauseOf(exception).Message.TrimEnd('.');
                return () => throw new PluginUnavailableException($"its {what} could not be made: {reason}");
            }
        }
    }
}
