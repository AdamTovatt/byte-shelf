using ByteShelf.Configuration;
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
        private readonly IStorageService _storageService;
        private readonly ITenantConfigurationService _tenantConfigurationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantController"/> class.
        /// </summary>
        /// <param name="storageService">The storage service for quota operations.</param>
        /// <param name="tenantConfigurationService">The tenant configuration service for tenant information.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public TenantController(
            IStorageService storageService,
            ITenantConfigurationService tenantConfigurationService)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
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

            // Get tenant configuration (could be root tenant or subtenant)
            TenantInfo? tenantInfo = _tenantConfigurationService.GetTenant(tenantId);
            if (tenantInfo == null)
            {
                return NotFound("Tenant not found");
            }

            // Get storage information
            long currentUsage = _storageService.GetCurrentUsage(tenantId);
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
            long currentUsage = _storageService.GetCurrentUsage(tenantId);
            long storageLimit = _storageService.GetStorageLimit(tenantId);
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
            bool canStore = _storageService.CanStoreData(tenantId, fileSizeBytes);
            long currentUsage = _storageService.GetCurrentUsage(tenantId);
            long storageLimit = _storageService.GetStorageLimit(tenantId);
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

        /// <summary>
        /// Gets all subtenants of the authenticated tenant.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>List of subtenants.</returns>
        /// <response code="200">Returns the list of subtenants.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint returns all subtenants that belong to the authenticated tenant.
        /// </remarks>
        [HttpGet("subtenants")]
        [ProducesResponseType(typeof(Dictionary<string, TenantInfoResponse>), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetSubTenants(CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            string tenantId = HttpContext.GetTenantId();
            Dictionary<string, TenantInfo> subTenants = _tenantConfigurationService.GetSubTenants(tenantId);

            // Convert to TenantInfoResponse to avoid exposing API keys
            Dictionary<string, TenantInfoResponse> response = subTenants.ToTenantInfoResponses(_storageService);

            return Ok(response);
        }

        /// <summary>
        /// Gets information about a specific subtenant.
        /// </summary>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Subtenant information.</returns>
        /// <response code="200">Returns the subtenant information.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="404">If the subtenant is not found.</response>
        /// <remarks>
        /// This endpoint returns information about a specific subtenant that belongs to the authenticated tenant.
        /// </remarks>
        [HttpGet("subtenants/{subTenantId}")]
        [ProducesResponseType(typeof(TenantInfoResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetSubTenant(string subTenantId, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            string tenantId = HttpContext.GetTenantId();
            TenantInfo? subTenant = _tenantConfigurationService.GetSubTenant(tenantId, subTenantId);

            if (subTenant == null)
            {
                return NotFound("Subtenant not found");
            }

            // Convert to TenantInfoResponse to avoid exposing API keys
            TenantInfoResponse response = subTenant.ToTenantInfoResponse(subTenantId, _storageService);

            return Ok(response);
        }

        /// <summary>
        /// Gets all subtenants under a specific subtenant.
        /// </summary>
        /// <param name="parentSubtenantId">The parent subtenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>List of subtenants under the specified parent subtenant.</returns>
        /// <response code="200">Returns the list of subtenants.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="404">If the parent subtenant is not found.</response>
        /// <remarks>
        /// This endpoint returns all subtenants that belong to a specific subtenant.
        /// The authenticated tenant must have access to the parent subtenant.
        /// </remarks>
        [HttpGet("subtenants/{parentSubtenantId}/subtenants")]
        [ProducesResponseType(typeof(Dictionary<string, TenantInfoResponse>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetSubTenantsUnderSubTenant(string parentSubtenantId, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            string tenantId = HttpContext.GetTenantId();
            
            // Verify the parent subtenant exists and the authenticated tenant has access to it
            TenantInfo? parentSubtenant = _tenantConfigurationService.GetSubTenant(tenantId, parentSubtenantId);
            if (parentSubtenant == null)
            {
                return NotFound("Parent subtenant not found");
            }

            // Get all subtenants under the parent subtenant
            Dictionary<string, TenantInfo> subTenants = _tenantConfigurationService.GetSubTenants(parentSubtenantId);

            // Convert to TenantInfoResponse to avoid exposing API keys
            Dictionary<string, TenantInfoResponse> response = subTenants.ToTenantInfoResponses(_storageService);

            return Ok(response);
        }

        /// <summary>
        /// Creates a new subtenant under the authenticated tenant.
        /// </summary>
        /// <param name="request">The subtenant creation request.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The created subtenant information.</returns>
        /// <response code="201">Returns the created subtenant information.</response>
        /// <response code="400">If the request is invalid.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="409">If maximum depth is reached.</response>
        /// <remarks>
        /// This endpoint creates a new subtenant under the authenticated tenant.
        /// The subtenant will have a unique ID and API key generated automatically.
        /// </remarks>
        [HttpPost("subtenants")]
        [ProducesResponseType(typeof(CreateSubTenantResponse), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> CreateSubTenant([FromBody] CreateSubTenantRequest? request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest("Request cannot be null");
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return BadRequest("Display name is required");
            }

            try
            {
                string tenantId = HttpContext.GetTenantId();
                string subTenantId = await _tenantConfigurationService.CreateSubTenantAsync(tenantId, request.DisplayName);
                
                CreateSubTenantResponse response = new CreateSubTenantResponse(subTenantId, request.DisplayName, "Subtenant created successfully");
                return CreatedAtAction(nameof(GetSubTenant), new { subTenantId = subTenantId }, response);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Creates a new subtenant under a specific subtenant.
        /// </summary>
        /// <param name="parentSubtenantId">The parent subtenant ID.</param>
        /// <param name="request">The subtenant creation request.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The created subtenant information.</returns>
        /// <response code="201">Returns the created subtenant information.</response>
        /// <response code="400">If the request is invalid.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="404">If the parent subtenant is not found.</response>
        /// <response code="409">If maximum depth is reached.</response>
        /// <remarks>
        /// This endpoint creates a new subtenant under a specific subtenant, enabling hierarchical folder creation.
        /// The subtenant will have a unique ID and API key generated automatically.
        /// The authenticated tenant must have access to the parent subtenant.
        /// </remarks>
        [HttpPost("subtenants/{parentSubtenantId}/subtenants")]
        [ProducesResponseType(typeof(CreateSubTenantResponse), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> CreateSubTenantUnderSubTenant(string parentSubtenantId, [FromBody] CreateSubTenantRequest? request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest("Request cannot be null");
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return BadRequest("Display name is required");
            }

            if (string.IsNullOrWhiteSpace(parentSubtenantId))
            {
                return BadRequest("Parent subtenant ID is required");
            }

            try
            {
                string tenantId = HttpContext.GetTenantId();
                
                // Verify the parent subtenant exists and the authenticated tenant has access to it
                TenantInfo? parentSubtenant = _tenantConfigurationService.GetSubTenant(tenantId, parentSubtenantId);
                if (parentSubtenant == null)
                {
                    return NotFound("Parent subtenant not found");
                }

                // Create the subtenant under the parent subtenant
                string subTenantId = await _tenantConfigurationService.CreateSubTenantAsync(parentSubtenantId, request.DisplayName);
                
                CreateSubTenantResponse response = new CreateSubTenantResponse(subTenantId, request.DisplayName, "Subtenant created successfully");
                return CreatedAtAction(nameof(GetSubTenant), new { subTenantId = subTenantId }, response);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Updates the storage limit of a subtenant.
        /// </summary>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="request">The storage limit update request.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Success status.</returns>
        /// <response code="200">Returns success status.</response>
        /// <response code="400">If the request is invalid.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="404">If the subtenant is not found.</response>
        /// <remarks>
        /// This endpoint updates the storage limit of a subtenant.
        /// The new limit cannot exceed the parent tenant's limit.
        /// </remarks>
        [HttpPut("subtenants/{subTenantId}/storage-limit")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> UpdateSubTenantStorageLimit(string subTenantId, [FromBody] UpdateStorageLimitRequest? request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest("Request cannot be null");
            }

            if (request.StorageLimitBytes < 0)
            {
                return BadRequest("Storage limit must be non-negative");
            }

            string tenantId = HttpContext.GetTenantId();
            bool success = await _tenantConfigurationService.UpdateSubTenantStorageLimitAsync(tenantId, subTenantId, request.StorageLimitBytes);

            if (!success)
            {
                return NotFound("Subtenant not found");
            }

            return Ok();
        }

        /// <summary>
        /// Deletes a subtenant.
        /// </summary>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Success status.</returns>
        /// <response code="204">Returns success status.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="404">If the subtenant is not found.</response>
        /// <remarks>
        /// This endpoint deletes a subtenant and all its data.
        /// This operation cannot be undone.
        /// </remarks>
        [HttpDelete("subtenants/{subTenantId}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> DeleteSubTenant(string subTenantId, CancellationToken cancellationToken)
        {
            string tenantId = HttpContext.GetTenantId();
            bool success = await _tenantConfigurationService.DeleteSubTenantAsync(tenantId, subTenantId);

            if (!success)
            {
                return NotFound("Subtenant not found");
            }

            return Ok();
        }
    }
}