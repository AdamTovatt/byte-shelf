using System.Collections.Concurrent;
using System.Text.Json;

namespace ByteShelf.Services
{
    /// <summary>
    /// Implementation of IFileSystemStorageService that provides thread-safe
    /// user storage quota management and usage tracking in a filesystem-based architecture.
    /// </summary>
    public class FileSystemStorageService : IFileSystemStorageService, IDisposable
    {
        private readonly IFileSystemConfigurationService _configService;
        private readonly string _storagePath;
        private readonly ILogger<FileSystemStorageService> _logger;
        private readonly SemaphoreSlim _usageLock = new(1, 1);
        private readonly ConcurrentDictionary<string, long> _usageCache = new(); // "tenantId:userId" -> usage
        private readonly string _usageFilePath;
        private int _operationCount = 0;
        private const int PersistInterval = 10; // Persist every 10 operations
        private bool _disposed = false;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public FileSystemStorageService(
            IFileSystemConfigurationService configService,
            ILogger<FileSystemStorageService> logger)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Get storage path from environment variable or use default
            string? envPath = Environment.GetEnvironmentVariable("BYTESHELF_STORAGE_PATH");
            _storagePath = string.IsNullOrWhiteSpace(envPath) ? "./storage" : envPath;
            
            _usageFilePath = Path.Combine(_storagePath, "usage.json");

            // Ensure storage directory exists
            Directory.CreateDirectory(_storagePath);

            _ = LoadUsageDataAsync();
        }

        public async Task<bool> CanStoreDataAsync(string tenantId, string userId, long sizeBytes)
        {
            var user = await _configService.GetUserAsync(tenantId, userId);
            var tenant = await _configService.GetTenantAsync(tenantId);

            if (user == null || tenant == null)
            {
                _logger.LogWarning("User {UserId} or tenant {TenantId} not found", userId, tenantId);
                return false;
            }

            await _usageLock.WaitAsync();
            try
            {
                long currentUserUsage = await GetUserUsageInternalAsync(tenantId, userId);
                long currentTenantUsage = await GetTenantUsageInternalAsync(tenantId);

                // Check user limit
                long userLimit = user.StorageLimitBytes;
                if (userLimit > 0) // 0 means unlimited for user
                {
                    if (currentUserUsage + sizeBytes > userLimit)
                    {
                        _logger.LogDebug("User {UserId} would exceed individual limit", userId);
                        return false;
                    }
                }

                // Check tenant limit
                long tenantLimit = tenant.StorageLimitBytes;
                if (tenantLimit > 0) // 0 means unlimited for tenant
                {
                    if (currentTenantUsage + sizeBytes > tenantLimit)
                    {
                        _logger.LogDebug("Tenant {TenantId} would exceed tenant limit", tenantId);
                        return false;
                    }
                }

                _logger.LogDebug("Storage check passed for user {UserId} in tenant {TenantId}", userId, tenantId);
                return true;
            }
            finally
            {
                _usageLock.Release();
            }
        }

        public async Task RecordStorageUsedAsync(string tenantId, string userId, long sizeBytes)
        {
            if (sizeBytes <= 0) return;

            await _usageLock.WaitAsync();
            try
            {
                string userKey = $"{tenantId}:{userId}";
                _usageCache.AddOrUpdate(userKey, sizeBytes, (key, existing) => existing + sizeBytes);

                _operationCount++;
                if (_operationCount >= PersistInterval)
                {
                    await PersistUsageDataInternalAsync();
                    _operationCount = 0;
                }

                _logger.LogDebug("Recorded {SizeBytes} bytes used for user {UserId} in tenant {TenantId}", 
                    sizeBytes, userId, tenantId);
            }
            finally
            {
                _usageLock.Release();
            }
        }

        public async Task RecordStorageFreedAsync(string tenantId, string userId, long sizeBytes)
        {
            if (sizeBytes <= 0) return;

            await _usageLock.WaitAsync();
            try
            {
                string userKey = $"{tenantId}:{userId}";
                _usageCache.AddOrUpdate(userKey, 0, (key, existing) => Math.Max(0, existing - sizeBytes));

                _operationCount++;
                if (_operationCount >= PersistInterval)
                {
                    await PersistUsageDataInternalAsync();
                    _operationCount = 0;
                }

                _logger.LogDebug("Recorded {SizeBytes} bytes freed for user {UserId} in tenant {TenantId}", 
                    sizeBytes, userId, tenantId);
            }
            finally
            {
                _usageLock.Release();
            }
        }

        public async Task<long> GetUserUsageAsync(string tenantId, string userId)
        {
            await _usageLock.WaitAsync();
            try
            {
                return await GetUserUsageInternalAsync(tenantId, userId);
            }
            finally
            {
                _usageLock.Release();
            }
        }

        public async Task<long> GetTenantUsageAsync(string tenantId)
        {
            await _usageLock.WaitAsync();
            try
            {
                return await GetTenantUsageInternalAsync(tenantId);
            }
            finally
            {
                _usageLock.Release();
            }
        }

        public async Task<long> GetUserStorageLimitAsync(string tenantId, string userId)
        {
            var user = await _configService.GetUserAsync(tenantId, userId);
            return user?.StorageLimitBytes ?? 0;
        }

        public async Task<long> GetTenantStorageLimitAsync(string tenantId)
        {
            var tenant = await _configService.GetTenantAsync(tenantId);
            return tenant?.StorageLimitBytes ?? 0;
        }

        public async Task<Dictionary<string, long>> GetTenantUserUsagesAsync(string tenantId)
        {
            await _usageLock.WaitAsync();
            try
            {
                var result = new Dictionary<string, long>();
                var users = await _configService.GetUsersForTenantAsync(tenantId);

                foreach (var user in users)
                {
                    long usage = await GetUserUsageInternalAsync(tenantId, user.UserId);
                    result[user.UserId] = usage;
                }

                return result;
            }
            finally
            {
                _usageLock.Release();
            }
        }

        public async Task RecalculateUsageAsync(string? tenantId = null, string? userId = null)
        {
            await _usageLock.WaitAsync();
            try
            {
                if (tenantId != null && userId != null)
                {
                    // Recalculate for specific user
                    await RecalculateUserUsageAsync(tenantId, userId);
                }
                else if (tenantId != null)
                {
                    // Recalculate for all users in tenant
                    var users = await _configService.GetUsersForTenantAsync(tenantId);
                    foreach (var user in users)
                    {
                        await RecalculateUserUsageAsync(tenantId, user.UserId);
                    }
                }
                else
                {
                    // Recalculate for all tenants and users
                    var tenants = await _configService.GetAllTenantsAsync();
                    foreach (var tenant in tenants)
                    {
                        var users = await _configService.GetUsersForTenantAsync(tenant.TenantId);
                        foreach (var user in users)
                        {
                            await RecalculateUserUsageAsync(tenant.TenantId, user.UserId);
                        }
                    }
                }

                await PersistUsageDataInternalAsync();
            }
            finally
            {
                _usageLock.Release();
            }
        }

        public async Task PersistUsageDataAsync()
        {
            await _usageLock.WaitAsync();
            try
            {
                await PersistUsageDataInternalAsync();
            }
            finally
            {
                _usageLock.Release();
            }
        }

        private async Task<long> GetUserUsageInternalAsync(string tenantId, string userId)
        {
            string userKey = $"{tenantId}:{userId}";
            return _usageCache.GetValueOrDefault(userKey, 0);
        }

        private async Task<long> GetTenantUsageInternalAsync(string tenantId)
        {
            long totalUsage = 0;
            var users = await _configService.GetUsersForTenantAsync(tenantId);

            foreach (var user in users)
            {
                totalUsage += await GetUserUsageInternalAsync(tenantId, user.UserId);
            }

            return totalUsage;
        }

        private async Task RecalculateUserUsageAsync(string tenantId, string userId)
        {
            try
            {
                string userFilesPath = Path.Combine(_storagePath, tenantId, "users", userId, "files");
                
                if (!Directory.Exists(userFilesPath))
                {
                    string userKey = $"{tenantId}:{userId}";
                    _usageCache.TryRemove(userKey, out _);
                    return;
                }

                long totalSize = 0;
                var files = Directory.GetFiles(userFilesPath, "*", SearchOption.AllDirectories);
                
                foreach (string file in files)
                {
                    var fileInfo = new FileInfo(file);
                    totalSize += fileInfo.Length;
                }

                string userKey = $"{tenantId}:{userId}";
                _usageCache.AddOrUpdate(userKey, totalSize, (key, existing) => totalSize);

                _logger.LogDebug("Recalculated usage for user {UserId} in tenant {TenantId}: {TotalSize} bytes", 
                    userId, tenantId, totalSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recalculate usage for user {UserId} in tenant {TenantId}", 
                    userId, tenantId);
            }
        }

        private async Task LoadUsageDataAsync()
        {
            try
            {
                if (!File.Exists(_usageFilePath))
                {
                    _logger.LogInformation("Usage file not found, starting with empty usage data");
                    return;
                }

                string json = await File.ReadAllTextAsync(_usageFilePath);
                var usageData = JsonSerializer.Deserialize<Dictionary<string, long>>(json, JsonOptions);

                if (usageData != null)
                {
                    foreach (var kvp in usageData)
                    {
                        _usageCache.TryAdd(kvp.Key, kvp.Value);
                    }
                }

                _logger.LogInformation("Loaded usage data for {Count} users", _usageCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load usage data from {UsageFilePath}", _usageFilePath);
            }
        }

        private async Task PersistUsageDataInternalAsync()
        {
            try
            {
                var usageData = _usageCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                string json = JsonSerializer.Serialize(usageData, JsonOptions);
                await File.WriteAllTextAsync(_usageFilePath, json);

                _logger.LogDebug("Persisted usage data for {Count} users", usageData.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist usage data to {UsageFilePath}", _usageFilePath);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _usageLock?.Dispose();
                _disposed = true;
            }
        }
    }
}