using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>
    /// <c>ByteShelf.Server --check-plugin &lt;folder&gt;</c>: whether this server would load the plugin in a
    /// folder, for updaemon to ask before it switches versions. It loads the plugin as the server does,
    /// among the server's own registrations, which are thrown away, so it starts nothing, needs no root and
    /// goes by none of the server's settings (<see cref="EnvironmentVariables"/>). Where it can still answer
    /// differently is in Plugins.md. Its arguments, exit codes and what it may need are a contract with updaemon
    /// (<c>check</c> in updaemon.json).
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
                IMvcBuilder mvc = Program.AddServerServices(Program.CreateBuilder([]).Services, ServerSettings.OnNoRoot());
                LoadedPlugin plugin = PluginLoader.Load(PluginManifestReader.Read(folder), mvc.Services, mvc.PartManager);
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
