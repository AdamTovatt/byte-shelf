using System.Text.Json;
using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Json;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>What a plugin's process does for derived files: has the plugin's maker make some kinds, and leaves them in its folder.</summary>
    internal sealed class MakeWork : ProcessWork
    {
        public override PluginWork For(PluginRegistrations registered)
        {
            Func<IDerivedFileMaker> maker = Ready("maker", registered.Maker?.Make);
            return (arguments, contents, folder) => RunAsync(maker(), registered.Maker!.Makes, arguments, contents, folder);
        }

        /// <exception cref="PluginUnavailableException">The maker makes other versions, or what it made could not be written.</exception>
        private static async Task<JsonElement> RunAsync(IDerivedFileMaker maker, IReadOnlyList<DeclaredKind> makes, JsonElement arguments, Stream contents, string folder)
        {
            MakeArguments asked = arguments.Deserialize<MakeArguments>(JsonDefaults.CamelCase)!;
            if (!asked.Kinds.All(requested => makes.Any(declared => declared.Kind == requested.Kind && declared.Version == requested.Version)))
            {
                throw PluginUnavailableException.OtherVersion();
            }

            IReadOnlyList<DerivedKind> kinds = asked.Kinds.Select(requested => requested.Kind).ToList();
            MadeFiles made = new MadeFiles(folder, kinds);
            try
            {
                await maker.MakeAsync(contents, kinds, made);
            }
            finally
            {
                made.ThrowIfNotWritten();
            }

            return JsonSerializer.SerializeToElement(made.Made, JsonDefaults.CamelCase);
        }
    }
}
