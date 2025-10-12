using System.Text.Json.Serialization;

namespace ByteShelfCommon
{
    /// <summary>
    /// JSON serializer context for ByteShelfCommon types to enable AOT compilation.
    /// </summary>
    [JsonSerializable(typeof(ShelfFileMetadata))]
    [JsonSerializable(typeof(List<ShelfFileMetadata>))]
    [JsonSerializable(typeof(TenantStorageInfo))]
    [JsonSerializable(typeof(QuotaCheckResult))]
    [JsonSerializable(typeof(TenantInfo))]
    [JsonSerializable(typeof(TenantInfoResponse))]
    [JsonSerializable(typeof(Dictionary<string, TenantInfoResponse>))]
    [JsonSerializable(typeof(CreateSubTenantRequest))]
    [JsonSerializable(typeof(CreateSubTenantResponse))]
    [JsonSerializable(typeof(UpdateStorageLimitRequest))]
    [JsonSerializable(typeof(CreateTenantRequest))]
    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    public partial class ByteShelfCommonJsonSerializerContext : JsonSerializerContext
    {
    }
}

