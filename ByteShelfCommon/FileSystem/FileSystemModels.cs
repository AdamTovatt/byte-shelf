namespace ByteShelfCommon.FileSystem
{
    /// <summary>
    /// Response model for tenant information.
    /// </summary>
    public class TenantInfoResponse
    {
        public TenantInfoResponse(string tenantId, string displayName, long storageLimitBytes, 
            long currentUsageBytes, long availableSpaceBytes, double usagePercentage, bool isActive)
        {
            TenantId = tenantId;
            DisplayName = displayName;
            StorageLimitBytes = storageLimitBytes;
            CurrentUsageBytes = currentUsageBytes;
            AvailableSpaceBytes = availableSpaceBytes;
            UsagePercentage = usagePercentage;
            IsActive = isActive;
        }

        public string TenantId { get; }
        public string DisplayName { get; }
        public long StorageLimitBytes { get; }
        public long CurrentUsageBytes { get; }
        public long AvailableSpaceBytes { get; }
        public double UsagePercentage { get; }
        public bool IsActive { get; }
    }

    /// <summary>
    /// Response model for user information.
    /// </summary>
    public class UserInfoResponse
    {
        public UserInfoResponse(string userId, string tenantId, string displayName, string email,
            long storageLimitBytes, long currentUsageBytes, long availableSpaceBytes, 
            double usagePercentage, bool isActive, bool isAdmin, DateTime createdAt, DateTime? lastAccessAt)
        {
            UserId = userId;
            TenantId = tenantId;
            DisplayName = displayName;
            Email = email;
            StorageLimitBytes = storageLimitBytes;
            CurrentUsageBytes = currentUsageBytes;
            AvailableSpaceBytes = availableSpaceBytes;
            UsagePercentage = usagePercentage;
            IsActive = isActive;
            IsAdmin = isAdmin;
            CreatedAt = createdAt;
            LastAccessAt = lastAccessAt;
        }

        public string UserId { get; }
        public string TenantId { get; }
        public string DisplayName { get; }
        public string Email { get; }
        public long StorageLimitBytes { get; }
        public long CurrentUsageBytes { get; }
        public long AvailableSpaceBytes { get; }
        public double UsagePercentage { get; }
        public bool IsActive { get; }
        public bool IsAdmin { get; }
        public DateTime CreatedAt { get; }
        public DateTime? LastAccessAt { get; }
    }

    /// <summary>
    /// Request model for creating a new user.
    /// </summary>
    public class CreateUserRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public long StorageLimitBytes { get; set; }
        public bool IsAdmin { get; set; } = false;
    }

    /// <summary>
    /// Response model for user creation.
    /// </summary>
    public class CreateUserResponse
    {
        public CreateUserResponse(string userId, string apiKey)
        {
            UserId = userId;
            ApiKey = apiKey;
        }

        public string UserId { get; }
        public string ApiKey { get; }
    }

    /// <summary>
    /// Request model for updating user information.
    /// </summary>
    public class UpdateUserRequest
    {
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public long? StorageLimitBytes { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsAdmin { get; set; }
    }

    /// <summary>
    /// Request model for creating a new tenant.
    /// </summary>
    public class CreateTenantRequest
    {
        public string TenantId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public long StorageLimitBytes { get; set; }
    }

    /// <summary>
    /// Request model for updating tenant information.
    /// </summary>
    public class UpdateTenantRequest
    {
        public string? DisplayName { get; set; }
        public long? StorageLimitBytes { get; set; }
        public bool? IsActive { get; set; }
    }
}