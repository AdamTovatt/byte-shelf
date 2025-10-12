using ByteShelfCommon;
using System.Text.Json.Serialization;

namespace ByteShelfClient
{
    /// <summary>
    /// JSON serializer context for ByteShelfClient types to enable AOT compilation.
    /// </summary>
    [JsonSerializable(typeof(ChunkConfiguration))]
    [JsonSerializable(typeof(ShelfFileMetadata))]
    [JsonSerializable(typeof(List<ShelfFileMetadata>))]
    [JsonSerializable(typeof(TenantStorageInfo))]
    [JsonSerializable(typeof(QuotaCheckResult))]
    [JsonSerializable(typeof(TenantInfoResponse))]
    [JsonSerializable(typeof(Dictionary<string, TenantInfoResponse>))]
    [JsonSerializable(typeof(CreateSubTenantRequest))]
    [JsonSerializable(typeof(CreateSubTenantResponse))]
    [JsonSerializable(typeof(UpdateStorageLimitRequest))]
    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    internal partial class ByteShelfClientJsonSerializerContext : JsonSerializerContext
    {
    }
}

