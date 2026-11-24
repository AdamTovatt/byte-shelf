using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>
    /// <c>ByteShelf.Server --check-plugin &lt;folder&gt;</c>: whether this server would load the plugin in a
    /// folder, for updaemon to ask before it switches versions. It loads the plugin as the server does,
    /// into services that are thrown away, so it starts nothing and needs no root and no settings. Its
    /// arguments, exit codes and what it may need are a contract with updaemon (<c>check</c> in
    /// updaemon.json).
    /// </summary>
    public static class PluginCheckCommand
    {
        public const string Flag = "--check-plugin";

        /// <summary>This server would load the plugin.</summary>
        public const int Loads = 0;

        /// <summary>It would not; why is written.</summary>
        public const int Refused = 1;

        /// <summary>Nothing was tried: the arguments are wrong.</summary>
        public const int Usage = 2;

        /// <summary>Runs the check <paramref name="args"/> ask for, writing one line about it: to <paramref name="errors"/> when it does not load.</summary>
        public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter errors)
        {
            CommandArguments arguments = new CommandArguments(args);
            string? folder = arguments.Value(Flag);
            if (string.IsNullOrWhiteSpace(folder) || !arguments.AllTaken)
            {
                errors.WriteLine($"Usage: ByteShelf.Server {Flag} <the plugin's folder>");
                return Usage;
            }

            try
            {
                // The content root is the server's own folder: a builder watches its content root, which
                // must not be wherever the check was started from.
                WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
                IMvcBuilder mvc = builder.Services.AddControllers();

                LoadedPlugin plugin = PluginLoader.Load(PluginManifestReader.Read(folder), builder.Services, mvc.PartManager);
                output.WriteLine($"This server loads the plugin {plugin.Manifest.Id} {plugin.Manifest.Version}.");
                return Loads;
            }
            catch (PluginRefusedException refusal)
            {
                errors.WriteLine(refusal.Message);
                return Refused;
            }
        }
    }
}
