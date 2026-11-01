using System.Text.Json.Serialization;

namespace ByteShelf.Cli.Config
{
    /// <summary>
    /// The CLI's own JSON: its config file. A person may edit the file, so <c>kind</c> is found
    /// wherever it stands in the login, and a null where the config needs a value is refused as damage
    /// rather than read into a config that fails later.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true)]
    [JsonSerializable(typeof(CliConfig))]
    internal sealed partial class CliJsonContext : JsonSerializerContext
    {
    }
}
