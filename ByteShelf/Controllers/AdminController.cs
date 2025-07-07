using ByteShelf.Configuration;
using ByteShelf.Extensions;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    /// <summary>
    /// Controller for administrative operations on tenants.
    /// </summary>
    /// <remarks>
    /// This controller provides REST API endpoints for tenant management operations including:
    /// - Listing all tenants and their information
    /// - Creating new tenants
    /// - Updating tenant configuration
    /// - Viewing tenant storage usage
    /// All endpoints require admin API key authentication.
    /// </remarks>
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly ITenantConfigurationService _configService;
        private readonly IStorageService _storageService;
        private readonly IFileStorageService _fileStorageService;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdminController"/> class.
        /// </summary>
        /// <param name="configService">The tenant configuration service.</param>
        /// <param name="storageService">The storage service for quota operations.</param>
        /// <param name="fileStorageService">The file storage service for file operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public AdminController(
            ITenantConfigurationService configService,
            IStorageService storageService,
            IFileStorageService fileStorageService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        }

        /// <summary>
        /// Gets information about all tenants.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Information about all tenants including their storage usage.</returns>
        /// <response code="200">Returns the list of tenant information.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the user is not an admin.</response>
        /// <remarks>
        /// This endpoint provides comprehensive information about all tenants including
        /// their current storage usage, limits, and configuration.
        /// </remarks>
        [HttpGet("tenants")]
        [ProducesResponseType(typeof(IEnumerable<object>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetTenants(CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            // Check if user is admin
            if (!HttpContext.IsAdmin())
            {
                return Forbid();
            }

            List<object> tenantInfo = new List<object>();
            TenantConfiguration config = _configService.GetConfiguration();

            foreach (KeyValuePair<string, TenantInfo> tenant in config.Tenants)
            {
                long currentUsage = _storageService.GetCurrentUsage(tenant.Key);
                long storageLimit = tenant.Value.StorageLimitBytes;
                long availableSpace = Math.Max(0, storageLimit - currentUsage);

                tenantInfo.Add(new
                {
                    TenantId = tenant.Key,
                    DisplayName = tenant.Value.DisplayName,
                    IsAdmin = tenant.Value.IsAdmin,
                    StorageLimitBytes = storageLimit,
                    CurrentUsageBytes = currentUsage,
                    AvailableSpaceBytes = availableSpace,
                    UsagePercentage = storageLimit > 0 ? (double)currentUsage / storageLimit * 100 : 0
                });
            }

            return Ok(tenantInfo);
        }

        /// <summary>
        /// Gets detailed information about a specific tenant.
        /// </summary>
        /// <param name="tenantId">The ID of the tenant to get information for.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Detailed information about the specified tenant.</returns>
        /// <response code="200">Returns the tenant information.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the user is not an admin.</response>
        /// <response code="404">If the tenant does not exist.</response>
        /// <remarks>
        /// This endpoint provides detailed information about a specific tenant including
        /// their configuration and current storage usage.
        /// </remarks>
        [HttpGet("tenants/{tenantId}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetTenant(string tenantId, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            // Check if user is admin
            if (!HttpContext.IsAdmin())
            {
                return Forbid();
            }

            // Check if tenant exists
            TenantConfiguration config = _configService.GetConfiguration();
            if (!config.Tenants.TryGetValue(tenantId, out TenantInfo? tenantInfo))
            {
                return NotFound("Tenant not found");
            }

            long currentUsage = _storageService.GetCurrentUsage(tenantId);
            long storageLimit = tenantInfo.StorageLimitBytes;
            long availableSpace = Math.Max(0, storageLimit - currentUsage);

            return Ok(new
            {
                TenantId = tenantId,
                DisplayName = tenantInfo.DisplayName,
                IsAdmin = tenantInfo.IsAdmin,
                StorageLimitBytes = storageLimit,
                CurrentUsageBytes = currentUsage,
                AvailableSpaceBytes = availableSpace,
                UsagePercentage = storageLimit > 0 ? (double)currentUsage / storageLimit * 100 : 0
            });
        }

        /// <summary>
        /// Creates a new tenant.
        /// </summary>
        /// <param name="request">The tenant creation request.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The created tenant information.</returns>
        /// <response code="201">Returns the created tenant information.</response>
        /// <response code="400">If the request is invalid.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the user is not an admin.</response>
        /// <response code="409">If a tenant with the same ID already exists.</response>
        /// <remarks>
        /// This endpoint creates a new tenant with the specified configuration.
        /// The tenant is immediately persisted to the external configuration file
        /// and becomes available for use without requiring a service restart.
        /// </remarks>
        [HttpPost("tenants")]
        [ProducesResponseType(typeof(object), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            // Check if user is admin
            if (!HttpContext.IsAdmin())
            {
                return Forbid();
            }

            // Validate request
            if (string.IsNullOrWhiteSpace(request.TenantId))
            {
                return BadRequest("Tenant ID is required");
            }

            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return BadRequest("API key is required");
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return BadRequest("Display name is required");
            }

            if (request.StorageLimitBytes <= 0)
            {
                return BadRequest("Storage limit must be greater than 0 for non-admin tenants");
            }

            // Check if tenant already exists
            TenantConfiguration config = _configService.GetConfiguration();
            if (config.Tenants.ContainsKey(request.TenantId))
            {
                return Conflict("A tenant with the specified ID already exists");
            }

            // Create the new tenant
            TenantInfo newTenant = new TenantInfo
            {
                ApiKey = request.ApiKey,
                DisplayName = request.DisplayName,
                StorageLimitBytes = request.StorageLimitBytes,
                IsAdmin = request.IsAdmin
            };

            bool success = await _configService.AddTenantAsync(request.TenantId, newTenant);
            if (!success)
            {
                return StatusCode(500, new { message = "Failed to create tenant" });
            }

            return CreatedAtAction(nameof(GetTenant), new { tenantId = request.TenantId }, new
            {
                TenantId = request.TenantId,
                DisplayName = request.DisplayName,
                StorageLimitBytes = request.StorageLimitBytes,
                IsAdmin = request.IsAdmin
            });
        }

        /// <summary>
        /// Updates a tenant's storage limit.
        /// </summary>
        /// <param name="tenantId">The ID of the tenant to update.</param>
        /// <param name="request">The update request containing the new storage limit.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The updated tenant information.</returns>
        /// <response code="200">Returns the updated tenant information.</response>
        /// <response code="400">If the request is invalid.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the user is not an admin.</response>
        /// <response code="404">If the tenant does not exist.</response>
        /// <remarks>
        /// This endpoint updates a tenant's storage limit. The new limit must be greater than
        /// or equal to the tenant's current usage. The change is immediately persisted to the
        /// external configuration file and takes effect without requiring a service restart.
        /// </remarks>
        [HttpPut("tenants/{tenantId}/storage-limit")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> UpdateTenantStorageLimit(string tenantId, [FromBody] UpdateStorageLimitRequest request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // No async operations needed

            // Check if user is admin
            if (!HttpContext.IsAdmin())
            {
                return Forbid();
            }

            // Check if tenant exists
            TenantConfiguration config = _configService.GetConfiguration();
            if (!config.Tenants.TryGetValue(tenantId, out TenantInfo? tenantInfo))
            {
                return NotFound("Tenant not found");
            }

            // Validate request
            if (request.StorageLimitBytes <= 0)
            {
                return BadRequest("Storage limit must be greater than 0");
            }

            // Check if new limit is sufficient for current usage
            long currentUsage = _storageService.GetCurrentUsage(tenantId);
            if (request.StorageLimitBytes < currentUsage)
            {
                return BadRequest($"New storage limit ({request.StorageLimitBytes} bytes) is less than current usage ({currentUsage} bytes)");
            }

            // Update the tenant's storage limit
            tenantInfo.StorageLimitBytes = request.StorageLimitBytes;
            bool success = await _configService.UpdateTenantAsync(tenantId, tenantInfo);
            if (!success)
            {
                return StatusCode(500, new { message = "Failed to update tenant storage limit" });
            }

            return Ok(new
            {
                TenantId = tenantId,
                DisplayName = tenantInfo.DisplayName,
                IsAdmin = tenantInfo.IsAdmin,
                StorageLimitBytes = request.StorageLimitBytes,
                CurrentUsageBytes = currentUsage,
                AvailableSpaceBytes = Math.Max(0, request.StorageLimitBytes - currentUsage),
                UsagePercentage = request.StorageLimitBytes > 0 ? (double)currentUsage / request.StorageLimitBytes * 100 : 0
            });
        }

        /// <summary>
        /// Deletes a tenant if it has no files.
        /// </summary>
        /// <param name="tenantId">The ID of the tenant to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>No content on successful deletion.</returns>
        /// <response code="204">If the tenant was successfully deleted.</response>
        /// <response code="400">If the tenant has files and cannot be deleted.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the user is not an admin.</response>
        /// <response code="404">If the tenant does not exist.</response>
        /// <remarks>
        /// This endpoint deletes a tenant only if it has no files. This prevents accidental
        /// deletion of tenants with important data. The tenant's storage usage is also cleared.
        /// </remarks>
        [HttpDelete("tenants/{tenantId}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> DeleteTenant(string tenantId, CancellationToken cancellationToken)
        {
            // Check if user is admin
            if (!HttpContext.IsAdmin())
            {
                return Forbid();
            }

            // Check if tenant exists
            TenantConfiguration config = _configService.GetConfiguration();
            if (!config.Tenants.TryGetValue(tenantId, out TenantInfo? tenantInfo))
            {
                return NotFound("Tenant not found");
            }

            // Check if tenant has any files
            IEnumerable<ShelfFileMetadata> files = await _fileStorageService.GetFilesAsync(tenantId, cancellationToken);
            if (files.Any())
            {
                return BadRequest(new
                {
                    message = $"Cannot delete tenant '{tenantId}' because it has {files.Count()} file(s). Please delete all files first."
                });
            }

            // Delete the tenant
            bool success = await _configService.RemoveTenantAsync(tenantId);
            if (!success)
            {
                return StatusCode(500, new { message = "Failed to delete tenant" });
            }

            return NoContent();
        }
    }


}