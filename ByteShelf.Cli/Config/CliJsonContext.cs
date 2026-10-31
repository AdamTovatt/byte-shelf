using System.Text.Json.Serialization;

namespace ByteShelf.Cli.Config
{
    /// <summary>The CLI's own JSON: its config file.</summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(CliConfig))]
    internal sealed partial class CliJsonContext : JsonSerializerContext
    {
    }
}
