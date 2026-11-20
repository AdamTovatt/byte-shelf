using System.Text.Json.Serialization;

namespace ByteShelf.Cli.Commands
{
    /// <summary>
    /// The JSON of the CLI's own results, the ones ByteShelfCommon has no type for, camelCase like the
    /// server's. Nulls are written, so a script sees every property.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(VersionReport))]
    internal sealed partial class CliOutputJsonContext : JsonSerializerContext
    {
    }
}
