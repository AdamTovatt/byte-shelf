using System.Text.Json;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Json;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>Has a plugin's reader read one contents.</summary>
    internal sealed class ReadRun : IPluginRun<PluginReading>
    {
        private readonly int _version;

        public ReadRun(PluginTarget plugin, string sha256, int version)
        {
            Plugin = plugin;
            Sha256 = sha256;
            _version = version;
        }

        public PluginTarget Plugin { get; }

        public string Sha256 { get; }

        public string Work => ReadWork.Name;

        public PluginReading Nothing => new PluginReading.NotRead();

        public JsonElement? Prepare(IReadOnlyList<string> mediaTypes)
        {
            return JsonSerializer.SerializeToElement(new ReadArguments(_version), JsonDefaults.CamelCase);
        }

        public Task<PluginReading> TakeAsync(PluginRunResult result, WorkFolder folder, CancellationToken cancellationToken)
        {
            return Task.FromResult<PluginReading>(result switch
            {
                PluginRunResult.Answered { Answer.ValueKind: JsonValueKind.Null } => new PluginReading.Found(null),
                PluginRunResult.Answered answered => new PluginReading.Found(answered.Answer.GetRawText()),
                PluginRunResult.ContentsNotRead => new PluginReading.NotRead(),
                _ => new PluginReading.Failed(),
            });
        }
    }
}
