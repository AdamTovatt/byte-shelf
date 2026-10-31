using System.Text.Json.Serialization;

namespace ByteShelfCommon.Serialization
{
    /// <summary>
    /// Source-generated JSON for every contract type, camelCase like the server, so clients serialize
    /// without reflection and stay AOT-compatible.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    [JsonSerializable(typeof(ErrorResponse))]
    [JsonSerializable(typeof(ShelfFile))]
    [JsonSerializable(typeof(ShelfDirectory))]
    [JsonSerializable(typeof(DirectoryListing))]
    [JsonSerializable(typeof(ShelfInfo))]
    [JsonSerializable(typeof(IReadOnlyList<ShelfInfo>))]
    [JsonSerializable(typeof(CreateShelfRequest))]
    [JsonSerializable(typeof(UpdateShelfRequest))]
    [JsonSerializable(typeof(CreateDirectoryRequest))]
    [JsonSerializable(typeof(MoveRequest))]
    [JsonSerializable(typeof(StartUploadRequest))]
    [JsonSerializable(typeof(UploadSession))]
    [JsonSerializable(typeof(TrashEntry))]
    [JsonSerializable(typeof(IReadOnlyList<TrashEntry>))]
    public sealed partial class ByteShelfJsonContext : JsonSerializerContext
    {
    }
}
