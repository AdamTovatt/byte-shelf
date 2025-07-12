using ByteShelf.Configuration;
using ByteShelfCommon;
using System.Text.Json;

namespace ByteShelf.Services
{
    /// <summary>
    /// Implementation of <see cref="IStorageService"/> that provides thread-safe
    /// tenant storage quota management and usage tracking.
    /// </summary>
    /// <remarks>
    /// This service maintains usage data in memory for performance and persists it
    /// to disk periodically. All operations are thread-safe using locks to prevent
    /// race conditions when multiple requests are updating usage simultaneously.
    /// </remarks>
    public class StorageService : IStorageService
    {
        private readonly ITenantConfigurationService _configService;
        private readonly string _storagePath;
        private readonly ILogger<StorageService> _logger;
        private readonly object _usageLock = new object();
        private readonly Dictionary<string, long> _usageCache = new Dictionary<string, long>();
        private readonly string _usageFilePath;
        private int _operationCount = 0;
        private const int PersistInterval = 10; // Persist every 10 operations

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageService"/> class.
        /// </summary>
        /// <param name="configService">The tenant configuration service for accessing tenant information.</param>
        /// <param name="logger">The logger for recording service operations.</param>
        /// <param name="configuration">The application configuration for accessing storage settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configService"/>, <paramref name="logger"/>, or <paramref name="configuration"/> is null.</exception>
        /// <remarks>
        /// This constructor initializes the service with the tenant configuration service,
        /// sets up the storage path from configuration, and loads existing usage data from disk.
        /// The service will automatically persist usage data periodically to maintain consistency.
        /// </remarks>
        public StorageService(
            ITenantConfigurationService configService,
            ILogger<StorageService> logger,
            IConfiguration configuration)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storagePath = (configuration ?? throw new ArgumentNullException(nameof(configuration)))["StoragePath"] ?? "byte-shelf-storage";
            _usageFilePath = Path.Combine(_storagePath, "usage.json");

            LoadUsageData();
        }

        /// <inheritdoc/>
        public bool CanStoreData(string tenantId, long sizeBytes)
        {
            TenantConfiguration config = _configService.GetConfiguration();
            if (!config.Tenants.ContainsKey(tenantId))
            {
                _logger.LogWarning("Tenant {TenantId} not found in configuration", tenantId);
                return false;
            }

            lock (_usageLock)
            {
                long currentUsage = GetCurrentUsageInternal(tenantId);
                long limit = config.Tenants[tenantId].StorageLimitBytes;

                // Admins with 0 storage limit have unlimited storage
                bool isUnlimited = limit == 0 && config.Tenants[tenantId].IsAdmin;
                bool canStore = isUnlimited || currentUsage + sizeBytes <= limit;

                _logger.LogDebug(
                    "Quota check for tenant {TenantId}: current={CurrentUsage}, limit={Limit}, unlimited={Unlimited}, requested={Requested}, canStore={CanStore}",
                    tenantId, currentUsage, limit, isUnlimited, sizeBytes, canStore);

                return canStore;
            }
        }

        /// <inheritdoc/>
        public void RecordStorageUsed(string tenantId, long sizeBytes)
        {
            TenantConfiguration config = _configService.GetConfiguration();
            if (!config.Tenants.ContainsKey(tenantId))
            {
                _logger.LogWarning("Attempted to record storage for unknown tenant {TenantId}", tenantId);
                return;
            }

            lock (_usageLock)
            {
                long currentUsage = GetCurrentUsageInternal(tenantId);
                long newUsage = currentUsage + sizeBytes;
                _usageCache[tenantId] = newUsage;

                _logger.LogInformation(
                    "Recorded storage used for tenant {TenantId}: {SizeBytes} bytes, new total: {NewUsage}",
                    tenantId, sizeBytes, newUsage);

                IncrementOperationCount();
            }
        }

        /// <inheritdoc/>
        public void RecordStorageFreed(string tenantId, long sizeBytes)
        {
            TenantConfiguration config = _configService.GetConfiguration();
            if (!config.Tenants.ContainsKey(tenantId))
            {
                _logger.LogWarning("Attempted to record storage freed for unknown tenant {TenantId}", tenantId);
                return;
            }

            lock (_usageLock)
            {
                long currentUsage = GetCurrentUsageInternal(tenantId);
                long newUsage = Math.Max(0, currentUsage - sizeBytes); // Don't go below 0
                _usageCache[tenantId] = newUsage;

                _logger.LogInformation(
                    "Recorded storage freed for tenant {TenantId}: {SizeBytes} bytes, new total: {NewUsage}",
                    tenantId, sizeBytes, newUsage);

                IncrementOperationCount();
            }
        }

        /// <inheritdoc/>
        public long GetCurrentUsage(string tenantId)
        {
            lock (_usageLock)
            {
                return GetCurrentUsageInternal(tenantId);
            }
        }

        /// <inheritdoc/>
        public long GetStorageLimit(string tenantId)
        {
            TenantConfiguration config = _configService.GetConfiguration();
            if (config.Tenants.TryGetValue(tenantId, out TenantInfo? tenantInfo))
            {
                return tenantInfo.StorageLimitBytes;
            }

            return 0;
        }

        /// <summary>
        /// Rebuilds the usage cache from metadata files and persists the results.
        /// </summary>
        /// <remarks>
        /// This method scans all tenant metadata directories to recalculate usage
        /// from actual stored files. This is useful for correcting inconsistencies
        /// or after manual file operations outside the normal API.
        /// </remarks>
        public void RebuildUsageCache()
        {
            try
            {
                Dictionary<string, long> actualUsage = RebuildUsageFromMetadata();

                lock (_usageLock)
                {
                    _usageCache.Clear();
                    foreach (KeyValuePair<string, long> kvp in actualUsage)
                    {
                        _usageCache[kvp.Key] = kvp.Value;
                    }
                }

                PersistUsageData();
                _logger.LogInformation("Manually rebuilt usage cache for {Count} tenants", _usageCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild usage cache");
            }
        }

        /// <summary>
        /// Gets the current usage for a tenant without acquiring a lock.
        /// This method should only be called from within a lock.
        /// </summary>
        private long GetCurrentUsageInternal(string tenantId)
        {
            return _usageCache.TryGetValue(tenantId, out long usage) ? usage : 0;
        }

        /// <summary>
        /// Gets the total usage for a tenant including all subtenants recursively.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The total usage including subtenants.</returns>
        public long GetTotalUsageIncludingSubTenants(string tenantId)
        {
            TenantConfiguration config = _configService.GetConfiguration();
            TenantInfo? tenant = GetTenantRecursive(config, tenantId);
            
            if (tenant == null)
                return 0;
            
            return CalculateTotalUsageRecursive(tenantId, tenant);
        }

        /// <summary>
        /// Recursively calculates the total usage for a tenant and all its subtenants.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="tenant">The tenant information.</param>
        /// <returns>The total usage including all subtenants.</returns>
        private long CalculateTotalUsageRecursive(string tenantId, TenantInfo tenant)
        {
            // Get own usage
            long ownUsage = GetCurrentUsage(tenantId);
            
            // Add usage from all subtenants
            long subTenantUsage = 0;
            foreach (KeyValuePair<string, TenantInfo> subTenant in tenant.SubTenants)
            {
                subTenantUsage += CalculateTotalUsageRecursive(subTenant.Key, subTenant.Value);
            }
            
            return ownUsage + subTenantUsage;
        }

        /// <summary>
        /// Recursively finds a tenant in the configuration.
        /// </summary>
        /// <param name="config">The tenant configuration.</param>
        /// <param name="tenantId">The tenant ID to find.</param>
        /// <returns>The tenant information, or null if not found.</returns>
        private TenantInfo? GetTenantRecursive(TenantConfiguration config, string tenantId)
        {
            // Check root tenants first
            if (config.Tenants.TryGetValue(tenantId, out TenantInfo? tenant))
            {
                return tenant;
            }

            // Search in subtenants recursively
            foreach (TenantInfo rootTenant in config.Tenants.Values)
            {
                TenantInfo? found = FindTenantInSubTenants(tenantId, rootTenant);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively searches for a tenant in subtenants.
        /// </summary>
        /// <param name="tenantId">The tenant ID to find.</param>
        /// <param name="tenant">The tenant to search in.</param>
        /// <returns>The found tenant, or null if not found.</returns>
        private TenantInfo? FindTenantInSubTenants(string tenantId, TenantInfo tenant)
        {
            if (tenant.SubTenants.TryGetValue(tenantId, out TenantInfo? found))
            {
                return found;
            }

            foreach (TenantInfo subTenant in tenant.SubTenants.Values)
            {
                TenantInfo? result = FindTenantInSubTenants(tenantId, subTenant);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// Increments the operation count and persists data if needed.
        /// This method should only be called from within a lock.
        /// </summary>
        private void IncrementOperationCount()
        {
            _operationCount++;
            if (_operationCount >= PersistInterval)
            {
                _operationCount = 0;
                PersistUsageData();
            }
        }

        /// <summary>
        /// Loads and rebuilds usage data from disk and metadata files.
        /// </summary>
        /// <remarks>
        /// This method first attempts to load cached usage data from the usage.json file.
        /// Then it scans all tenant metadata directories to rebuild the usage cache from
        /// actual stored files, ensuring consistency between the cache and reality.
        /// Finally, it persists the rebuilt cache to disk.
        /// </remarks>
        private void LoadUsageData()
        {
            try
            {
                // First, try to load cached usage data
                Dictionary<string, long> cachedUsage = LoadCachedUsageData();

                // Then rebuild usage from actual metadata files
                Dictionary<string, long> actualUsage = RebuildUsageFromMetadata();

                // Merge the data, preferring actual usage but keeping cached data for tenants with no files
                lock (_usageLock)
                {
                    _usageCache.Clear();

                    // Add all tenants from actual usage
                    foreach (KeyValuePair<string, long> kvp in actualUsage)
                    {
                        _usageCache[kvp.Key] = kvp.Value;
                    }

                    // Add tenants from cache that don't have files (preserve zero usage)
                    foreach (KeyValuePair<string, long> kvp in cachedUsage)
                    {
                        if (!_usageCache.ContainsKey(kvp.Key))
                        {
                            _usageCache[kvp.Key] = kvp.Value;
                        }
                    }
                }

                _logger.LogInformation("Rebuilt usage data for {Count} tenants from metadata files", _usageCache.Count);

                // Persist the rebuilt cache immediately
                PersistUsageData();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load and rebuild usage data");
            }
        }

        /// <summary>
        /// Loads cached usage data from the usage.json file.
        /// </summary>
        /// <returns>A dictionary of tenant usage data, or empty if the file doesn't exist or is invalid.</returns>
        private Dictionary<string, long> LoadCachedUsageData()
        {
            try
            {
                if (File.Exists(_usageFilePath))
                {
                    string json = File.ReadAllText(_usageFilePath);
                    Dictionary<string, long>? usageData = JsonSerializer.Deserialize<Dictionary<string, long>>(json);

                    if (usageData != null)
                    {
                        _logger.LogDebug("Loaded cached usage data for {Count} tenants", usageData.Count);
                        return usageData;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cached usage data from {FilePath}, will rebuild from metadata", _usageFilePath);
            }

            return new Dictionary<string, long>();
        }

        /// <summary>
        /// Rebuilds usage data by scanning all tenant metadata directories.
        /// </summary>
        /// <returns>A dictionary of tenant usage data calculated from actual stored files.</returns>
        private Dictionary<string, long> RebuildUsageFromMetadata()
        {
            Dictionary<string, long> usage = new Dictionary<string, long>();

            try
            {
                if (!Directory.Exists(_storagePath))
                {
                    _logger.LogDebug("Storage path does not exist: {StoragePath}", _storagePath);
                    return usage;
                }

                // Get all tenant directories
                string[] tenantDirectories = Directory.GetDirectories(_storagePath);

                foreach (string tenantDir in tenantDirectories)
                {
                    string tenantId = Path.GetFileName(tenantDir);
                    string metadataPath = Path.Combine(tenantDir, "metadata");

                    if (!Directory.Exists(metadataPath))
                    {
                        continue;
                    }

                    long tenantUsage = 0;
                    string[] metadataFiles = Directory.GetFiles(metadataPath, "*.json");

                    foreach (string metadataFile in metadataFiles)
                    {
                        try
                        {
                            string json = File.ReadAllText(metadataFile);
                            ShelfFileMetadata? metadata = JsonSerializer.Deserialize<ShelfFileMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (metadata != null)
                            {
                                tenantUsage += metadata.FileSize;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to read metadata file: {MetadataFile}", metadataFile);
                        }
                    }

                    if (tenantUsage > 0 || metadataFiles.Length > 0)
                    {
                        usage[tenantId] = tenantUsage;
                        _logger.LogDebug("Calculated usage for tenant {TenantId}: {Usage} bytes from {FileCount} files",
                            tenantId, tenantUsage, metadataFiles.Length);
                    }
                }

                _logger.LogInformation("Rebuilt usage data from {TenantCount} tenant directories", usage.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild usage data from metadata files");
            }

            return usage;
        }

        /// <summary>
        /// Persists usage data to disk.
        /// </summary>
        private void PersistUsageData()
        {
            try
            {
                // Ensure the storage directory exists
                Directory.CreateDirectory(_storagePath);

                Dictionary<string, long> usageData;
                lock (_usageLock)
                {
                    usageData = new Dictionary<string, long>(_usageCache);
                }

                string json = JsonSerializer.Serialize(usageData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_usageFilePath, json);

                _logger.LogDebug("Persisted usage data for {Count} tenants", usageData.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist usage data to {FilePath}", _usageFilePath);
            }
        }
    }
}