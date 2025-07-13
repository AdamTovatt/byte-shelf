using ByteShelf.Services;
using ByteShelfCommon;

namespace ByteShelf.Extensions
{
    /// <summary>
    /// Extension methods for tenant-related operations.
    /// </summary>
    public static class TenantExtensions
    {
        /// <summary>
        /// Converts a TenantInfo to TenantInfoResponse, including storage usage information.
        /// </summary>
        /// <param name="tenantInfo">The tenant information to convert.</param>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="storageService">The storage service to get usage information.</param>
        /// <returns>A TenantInfoResponse without API keys.</returns>
        public static TenantInfoResponse ToTenantInfoResponse(
            this TenantInfo tenantInfo,
            string tenantId,
            IStorageService storageService)
        {
            long currentUsage = storageService.GetTotalUsageIncludingSubTenants(tenantId);
            long storageLimit = tenantInfo.StorageLimitBytes;
            long availableSpace = Math.Max(0, storageLimit - currentUsage);

            return new TenantInfoResponse(
                tenantId,
                tenantInfo.DisplayName,
                tenantInfo.IsAdmin,
                storageLimit,
                currentUsage,
                availableSpace,
                storageLimit > 0 ? (double)currentUsage / storageLimit * 100 : 0);
        }

        /// <summary>
        /// Converts a dictionary of TenantInfo objects to TenantInfoResponse objects.
        /// </summary>
        /// <param name="subTenants">The dictionary of subtenants to convert.</param>
        /// <param name="storageService">The storage service to get usage information.</param>
        /// <returns>A dictionary of TenantInfoResponse objects without API keys.</returns>
        public static Dictionary<string, TenantInfoResponse> ToTenantInfoResponses(
            this Dictionary<string, TenantInfo> subTenants,
            IStorageService storageService)
        {
            Dictionary<string, TenantInfoResponse> response = new Dictionary<string, TenantInfoResponse>();
            
            foreach (KeyValuePair<string, TenantInfo> subTenant in subTenants)
            {
                response[subTenant.Key] = subTenant.Value.ToTenantInfoResponse(subTenant.Key, storageService);
            }

            return response;
        }
    }
} 