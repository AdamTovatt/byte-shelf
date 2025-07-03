using ByteShelf.Extensions;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    /// <summary>
    /// Controller for tenant information and quota management.
    /// </summary>
    /// <remarks>
    /// This controller provides REST API endpoints for tenant operations including:
    /// - Retrieving tenant information and admin status
    /// - Retrieving tenant storage usage and limits
    /// - Checking if a file can be stored within quota limits
    /// All endpoints require API key authentication and are scoped to the authenticated tenant.
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    public class TenantController : ControllerBase
    {
        private readonly ITenantStorageService _tenantStorageService;
        private readonly ITenantConfigurationService _tenantConfigurationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantController"/> class.
        /// </summary>
        /// <param name="tenantStorageService">The tenant storage service for quota operations.</param>
        /// <param name="tenantConfigurationService">The tenant configuration service for tenant information.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public TenantController(
            ITenantStorageService tenantStorageService,
            ITenantConfigurationService tenantConfigurationService)
        {
            _tenantStorageService = tenantStorageService ?? throw new ArgumentNullException(nameof(tenantStorageService));
            _tenantConfigurationService = tenantConfigurationService ?? throw new ArgumentNullException(nameof(tenantConfigurationService));
        }

        /// <summary>
        /// Gets information about the authenticated tenant.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Tenant information including admin status and display name.</returns>
        /// <response code="200">Returns the tenant information.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="404">If the tenant configuration is not found.</response>
        /// <remarks>
        /// This endpoint provides information about the tenant including their admin status,
        /// display name, and other configuration details. This is useful for frontend applications
        /// to determine what UI controls to show based on the tenant's permissions.
        /// </remarks>
        [HttpGet("info")]
        [ProducesResponseType(typeof(TenantInfoResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetTenantInfo(CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            string tenantId = HttpContext.GetTenantId();
            
            // Get tenant configuration
            var config = _tenantConfigurationService.GetConfiguration();
            if (!config.Tenants.TryGetValue(tenantId, out TenantInfo? tenantInfo))
            {
                return NotFound("Tenant not found");
            }

            // Get storage information
            long currentUsage = _tenantStorageService.GetCurrentUsage(tenantId);
            long storageLimit = tenantInfo.StorageLimitBytes;
            long availableSpace = Math.Max(0, storageLimit - currentUsage);

            TenantInfoResponse response = new TenantInfoResponse(
                tenantId,
                tenantInfo.DisplayName,
                tenantInfo.IsAdmin,
                storageLimit,
                currentUsage,
                availableSpace,
                storageLimit > 0 ? (double)currentUsage / storageLimit * 100 : 0);

            return Ok(response);
        }

        /// <summary>
        /// Gets storage usage information for the authenticated tenant.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Storage usage information including current usage and limits.</returns>
        /// <response code="200">Returns the storage usage information.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint provides information about the tenant's current storage usage
        /// and their configured storage limits.
        /// </remarks>
        [HttpGet("storage")]
        [ProducesResponseType(typeof(TenantStorageInfo), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetStorageInfo(CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            string tenantId = HttpContext.GetTenantId();
            long currentUsage = _tenantStorageService.GetCurrentUsage(tenantId);
            long storageLimit = _tenantStorageService.GetStorageLimit(tenantId);
            long availableSpace = Math.Max(0, storageLimit - currentUsage);

            TenantStorageInfo response = new TenantStorageInfo(
                tenantId,
                currentUsage,
                storageLimit,
                availableSpace,
                storageLimit > 0 ? (double)currentUsage / storageLimit * 100 : 0);

            return Ok(response);
        }

        /// <summary>
        /// Checks if the authenticated tenant can store a file of the specified size.
        /// </summary>
        /// <param name="fileSizeBytes">The size of the file to check in bytes.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Information about whether the file can be stored.</returns>
        /// <response code="200">Returns the quota check result.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint allows clients to check if they can store a file of a specific size
        /// before attempting to upload it, helping to avoid failed uploads due to quota limits.
        /// </remarks>
        [HttpGet("storage/can-store")]
        [ProducesResponseType(typeof(QuotaCheckResult), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> CanStoreFile([FromQuery] long fileSizeBytes, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            string tenantId = HttpContext.GetTenantId();
            bool canStore = _tenantStorageService.CanStoreData(tenantId, fileSizeBytes);
            long currentUsage = _tenantStorageService.GetCurrentUsage(tenantId);
            long storageLimit = _tenantStorageService.GetStorageLimit(tenantId);
            long availableSpace = Math.Max(0, storageLimit - currentUsage);

            QuotaCheckResult response = new QuotaCheckResult(
                tenantId,
                fileSizeBytes,
                canStore,
                currentUsage,
                storageLimit,
                availableSpace,
                !canStore);

            return Ok(response);
        }
    }
}