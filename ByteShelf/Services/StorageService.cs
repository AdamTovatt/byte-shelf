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
        /// <param name="storagePath">The storage path for tenant data.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configService"/>, <paramref name="logger"/>, or <paramref name="storagePath"/> is null.</exception>
        /// <remarks>
        /// This constructor initializes the service with the tenant configuration service,
        /// sets up the storage path, and loads existing usage data from disk.
        /// The service will automatically persist usage data periodically to maintain consistency.
        /// </remarks>
        public StorageService(
            ITenantConfigurationService configService,
            ILogger<StorageService> logger,
            string storagePath)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
            _usageFilePath = Path.Combine(_storagePath, "usage.json");

            LoadUsageData();
        }

        /// <inheritdoc/>
        public bool CanStoreData(string tenantId, long sizeBytes)
        {
            TenantConfiguration config = _configService.GetConfiguration();
            TenantInfo? tenant = GetTenantRecursive(config, tenantId);

            if (tenant == null)
            {
                _logger.LogWarning("Tenant {TenantId} not found in configuration", tenantId);
                return false;
            }

            lock (_usageLock)
            {
                long currentUsage = GetTotalUsageIncludingSubTenants(tenantId);
                long individualLimit = tenant.StorageLimitBytes;

                if (individualLimit == 0) // A value of 0 means unlimited
                {
                    _logger.LogDebug(
                        "Quota check for tenant {TenantId}: unlimited storage, canStore=true",
                        tenantId);
                    return true;
                }

                // Check individual limit first
                bool canStoreIndividual = currentUsage + sizeBytes <= individualLimit;
                if (!canStoreIndividual)
                {
                    _logger.LogDebug(
                        "Quota check for tenant {TenantId}: exceeds individual limit, canStore=false",
                        tenantId);
                    return false;
                }

                // If this tenant has a parent, check shared storage limit
                if (tenant.Parent != null)
                {
                    long parentLimit = tenant.Parent.StorageLimitBytes;

                    // If parent has unlimited storage, subtenant can use unlimited (subject to own limit)
                    if (parentLimit == 0 && tenant.Parent.IsAdmin)
                    {
                        _logger.LogDebug(
                            "Quota check for tenant {TenantId}: parent has unlimited storage, canStore=true",
                            tenantId);
                        return true;
                    }

                    // Calculate total usage of parent and all subtenants
                    long totalParentUsage = CalculateTotalUsageRecursive(tenant.Parent);

                    // The subtenant is limited by both its own limit and the parent's remaining quota
                    bool canStoreShared = totalParentUsage + sizeBytes <= parentLimit;
                    bool canStore = canStoreIndividual && canStoreShared;

                    _logger.LogDebug(
                        "Quota check for tenant {TenantId}: individual={Individual}, shared={Shared}, parentUsage={ParentUsage}, parentLimit={ParentLimit}, canStore={CanStore}",
                        tenantId, canStoreIndividual, canStoreShared, totalParentUsage, parentLimit, canStore);

                    return canStore;
                }
                else
                {
                    // Root tenant - only check individual limit
                    _logger.LogDebug(
                        "Quota check for tenant {TenantId}: root tenant, current={CurrentUsage}, limit={Limit}, requested={Requested}, canStore={CanStore}",
                        tenantId, currentUsage, individualLimit, sizeBytes, canStoreIndividual);

                    return canStoreIndividual;
                }
            }
        }

        /// <inheritdoc/>
        public void RecordStorageUsed(string tenantId, long sizeBytes)
        {
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
        /// The method validates against the tenant configuration to ensure only
        /// valid tenants are included in the cache.
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

            return CalculateTotalUsageRecursive(tenant);
        }

        /// <summary>
        /// Recursively calculates the total usage for a tenant and all its subtenants.
        /// </summary>
        /// <param name="tenant">The tenant information.</param>
        /// <returns>The total usage including all subtenants.</returns>
        private long CalculateTotalUsageRecursive(TenantInfo tenant)
        {
            // Find the tenant ID by searching for this tenant in the configuration
            TenantConfiguration config = _configService.GetConfiguration();
            string? tenantId = FindTenantId(tenant, config);

            if (tenantId == null)
                return 0;

            // Get own usage
            long ownUsage = GetCurrentUsageInternal(tenantId);

            // Add usage from all subtenants
            long subTenantUsage = 0;
            foreach (KeyValuePair<string, TenantInfo> subTenant in tenant.SubTenants)
            {
                subTenantUsage += CalculateTotalUsageRecursive(subTenant.Value);
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
        /// Finds the tenant ID for a given tenant by searching through the configuration.
        /// </summary>
        /// <param name="tenant">The tenant to find the ID for.</param>
        /// <param name="config">The tenant configuration.</param>
        /// <returns>The tenant ID, or null if not found.</returns>
        private string? FindTenantId(TenantInfo tenant, TenantConfiguration config)
        {
            // Search in root tenants
            foreach (KeyValuePair<string, TenantInfo> kvp in config.Tenants)
            {
                if (ReferenceEquals(kvp.Value, tenant))
                    return kvp.Key;
            }

            // Search in subtenants recursively
            foreach (TenantInfo rootTenant in config.Tenants.Values)
            {
                string? found = FindTenantIdInSubTenants(tenant, rootTenant);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Recursively searches for a tenant ID in subtenants.
        /// </summary>
        /// <param name="tenant">The tenant to find.</param>
        /// <param name="parentTenant">The parent tenant to search in.</param>
        /// <returns>The tenant ID, or null if not found.</returns>
        private string? FindTenantIdInSubTenants(TenantInfo tenant, TenantInfo parentTenant)
        {
            foreach (KeyValuePair<string, TenantInfo> kvp in parentTenant.SubTenants)
            {
                if (ReferenceEquals(kvp.Value, tenant))
                    return kvp.Key;

                string? found = FindTenantIdInSubTenants(tenant, kvp.Value);
                if (found != null)
                    return found;
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

                // Get all valid tenant IDs from configuration
                HashSet<string> validTenantIds = GetAllValidTenantIds();

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
                    // but only if they still exist in the configuration
                    foreach (KeyValuePair<string, long> kvp in cachedUsage)
                    {
                        if (!_usageCache.ContainsKey(kvp.Key) && validTenantIds.Contains(kvp.Key))
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

                // Get all valid tenant IDs from configuration
                HashSet<string> validTenantIds = GetAllValidTenantIds();

                // Get all tenant directories
                string[] tenantDirectories = Directory.GetDirectories(_storagePath);

                foreach (string tenantDir in tenantDirectories)
                {
                    string tenantId = Path.GetFileName(tenantDir);
                    
                    // Skip tenants that are no longer in the configuration
                    if (!validTenantIds.Contains(tenantId))
                    {
                        _logger.LogDebug("Skipping tenant {TenantId} as it no longer exists in configuration", tenantId);
                        continue;
                    }

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
        /// Gets all valid tenant IDs from the configuration, including subtenants.
        /// </summary>
        /// <returns>A set of all valid tenant IDs.</returns>
        private HashSet<string> GetAllValidTenantIds()
        {
            HashSet<string> validTenantIds = new HashSet<string>();
            TenantConfiguration config = _configService.GetConfiguration();

            // Add all root tenants
            foreach (string tenantId in config.Tenants.Keys)
            {
                validTenantIds.Add(tenantId);
                AddSubTenantIds(tenantId, config.Tenants[tenantId], validTenantIds);
            }

            return validTenantIds;
        }

        /// <summary>
        /// Recursively adds all subtenant IDs to the provided set.
        /// </summary>
        /// <param name="parentId">The parent tenant ID.</param>
        /// <param name="tenant">The tenant information.</param>
        /// <param name="validTenantIds">The set to add tenant IDs to.</param>
        private void AddSubTenantIds(string parentId, TenantInfo tenant, HashSet<string> validTenantIds)
        {
            foreach (KeyValuePair<string, TenantInfo> subTenant in tenant.SubTenants)
            {
                validTenantIds.Add(subTenant.Key);
                AddSubTenantIds(subTenant.Key, subTenant.Value, validTenantIds);
            }
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