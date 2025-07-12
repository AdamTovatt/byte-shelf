using ByteShelf.Configuration;
using ByteShelfCommon;
using System.Text.Json;

namespace ByteShelf.Services
{
    /// <summary>
    /// Implementation of <see cref="ITenantConfigurationService"/> that manages tenant configuration
    /// from an external JSON file with hot-reload capabilities.
    /// </summary>
    /// <remarks>
    /// This service provides thread-safe access to tenant configuration loaded from an external JSON file.
    /// It automatically watches the file for changes and reloads the configuration when the file is modified.
    /// The configuration file is created with default settings if it doesn't exist.
    /// </remarks>
    public class TenantConfigurationService : ITenantConfigurationService, IDisposable
    {
        private readonly string _configFilePath;
        private readonly ILogger<TenantConfigurationService> _logger;
        private readonly object _configLock = new object();
        private readonly FileSystemWatcher? _fileWatcher;
        private readonly JsonSerializerOptions _jsonOptions;

        private TenantConfiguration _currentConfiguration;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantConfigurationService"/> class.
        /// </summary>
        /// <param name="logger">The logger for recording service operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
        /// <remarks>
        /// This constructor initializes the service with the specified logger and sets up
        /// configuration file monitoring for hot-reload functionality. The configuration file
        /// path is determined from the BYTESHELF_TENANT_CONFIG_PATH environment variable,
        /// or defaults to "./tenant-config.json" if not specified.
        /// </remarks>
        public TenantConfigurationService(ILogger<TenantConfigurationService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Determine configuration file path from environment variable or use default
            string? envPath = Environment.GetEnvironmentVariable("BYTESHELF_TENANT_CONFIG_PATH");
            _configFilePath = string.IsNullOrWhiteSpace(envPath) ? "./tenant-config.json" : envPath;

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
            };

            // Initialize with default configuration
            _currentConfiguration = CreateDefaultConfiguration();

            // Load or create configuration file
            LoadOrCreateConfigurationFile();

            // Set up file watcher for hot-reload
            try
            {
                string directory = Path.GetDirectoryName(_configFilePath) ?? ".";
                string filename = Path.GetFileName(_configFilePath);

                _fileWatcher = new FileSystemWatcher(directory, filename)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                _fileWatcher.Changed += OnConfigurationFileChanged;
                _fileWatcher.Created += OnConfigurationFileChanged;

                _logger.LogInformation("File watcher initialized for configuration file: {ConfigPath}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize file watcher for configuration file: {ConfigPath}", _configFilePath);
            }
        }

        /// <inheritdoc/>
        public TenantConfiguration GetConfiguration()
        {
            lock (_configLock)
            {
                return _currentConfiguration;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> AddTenantAsync(string tenantId, TenantInfo tenantInfo)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));

            if (tenantInfo == null)
                throw new ArgumentNullException(nameof(tenantInfo));

            lock (_configLock)
            {
                if (_currentConfiguration.Tenants.ContainsKey(tenantId))
                {
                    _logger.LogWarning("Attempted to add tenant with existing ID: {TenantId}", tenantId);
                    return false;
                }

                _currentConfiguration.Tenants[tenantId] = tenantInfo;
            }

            bool saved = await SaveConfigurationAsync();
            if (saved)
            {
                _logger.LogInformation("Added new tenant: {TenantId}", tenantId);
            }

            return saved;
        }

        /// <inheritdoc/>
        public async Task<bool> UpdateTenantAsync(string tenantId, TenantInfo tenantInfo)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));

            if (tenantInfo == null)
                throw new ArgumentNullException(nameof(tenantInfo));

            lock (_configLock)
            {
                if (!_currentConfiguration.Tenants.ContainsKey(tenantId))
                {
                    _logger.LogWarning("Attempted to update non-existent tenant: {TenantId}", tenantId);
                    return false;
                }

                _currentConfiguration.Tenants[tenantId] = tenantInfo;
            }

            bool saved = await SaveConfigurationAsync();
            if (saved)
            {
                _logger.LogInformation("Updated tenant: {TenantId}", tenantId);
            }

            return saved;
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveTenantAsync(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));

            lock (_configLock)
            {
                if (!_currentConfiguration.Tenants.ContainsKey(tenantId))
                {
                    _logger.LogWarning("Attempted to remove non-existent tenant: {TenantId}", tenantId);
                    return false;
                }

                _currentConfiguration.Tenants.Remove(tenantId);
            }

            bool saved = await SaveConfigurationAsync();
            if (saved)
            {
                _logger.LogInformation("Removed tenant: {TenantId}", tenantId);
            }

            return saved;
        }

        /// <inheritdoc/>
        public string GetConfigurationFilePath()
        {
            return _configFilePath;
        }

        /// <inheritdoc/>
        public async Task<bool> ReloadConfigurationAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    _logger.LogWarning("Configuration file does not exist: {ConfigPath}", _configFilePath);
                    return false;
                }

                string jsonContent = await File.ReadAllTextAsync(_configFilePath);
                TenantConfiguration? newConfig = JsonSerializer.Deserialize<TenantConfiguration>(jsonContent, _jsonOptions);

                if (newConfig == null)
                {
                    _logger.LogError("Failed to deserialize configuration from file: {ConfigPath}", _configFilePath);
                    return false;
                }

                // Rebuild parent relationships after deserialization
                RebuildParentRelationships(newConfig);

                lock (_configLock)
                {
                    _currentConfiguration = newConfig;
                }

                _logger.LogInformation("Configuration reloaded from file: {ConfigPath}", _configFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload configuration from file: {ConfigPath}", _configFilePath);
                return false;
            }
        }

        /// <summary>
        /// Creates a new subtenant under the specified parent tenant.
        /// </summary>
        /// <param name="parentTenantId">The parent tenant ID.</param>
        /// <param name="displayName">The display name for the subtenant.</param>
        /// <returns>The ID of the created subtenant.</returns>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when maximum depth is reached or parent not found.</exception>
        public async Task<string> CreateSubTenantAsync(string parentTenantId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(parentTenantId))
                throw new ArgumentException("Parent tenant ID cannot be null or empty", nameof(parentTenantId));

            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Display name cannot be null or empty", nameof(displayName));

            // Check depth limit
            if (!await CanCreateSubTenantAsync(parentTenantId))
            {
                throw new InvalidOperationException($"Cannot create subtenant: maximum depth of {MaxSubTenantDepth} levels reached");
            }

            // Generate unique tenant ID (GUID)
            string tenantId = Guid.NewGuid().ToString();

            // Generate unique API key (GUID + random suffix)
            string apiKey = GenerateUniqueApiKey();

            // Create subtenant with parent's storage limit by default
            TenantInfo subTenant = new TenantInfo
            {
                ApiKey = apiKey,
                DisplayName = displayName,
                StorageLimitBytes = 0, // Will be set from parent
                IsAdmin = false,
                SubTenants = new Dictionary<string, TenantInfo>()
            };

            // Add to parent's subtenants under lock
            lock (_configLock)
            {
                TenantInfo? parentTenant = GetTenant(parentTenantId);
                if (parentTenant == null)
                {
                    throw new InvalidOperationException($"Parent tenant '{parentTenantId}' not found");
                }

                // Set storage limit from parent
                subTenant.StorageLimitBytes = parentTenant.StorageLimitBytes;

                // Set the parent of the subtenant to the parent tenant
                subTenant.Parent = parentTenant;

                // Add to parent's subtenants
                parentTenant.SubTenants[tenantId] = subTenant;
            }

            // Save configuration
            bool saved = await SaveConfigurationAsync();
            if (!saved)
            {
                throw new InvalidOperationException("Failed to save subtenant configuration");
            }

            _logger.LogInformation("Created subtenant '{TenantId}' under parent '{ParentTenantId}'", tenantId, parentTenantId);
            return tenantId;
        }

        /// <summary>
        /// Gets all subtenants of the specified tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>A dictionary of subtenants keyed by tenant ID.</returns>
        public Dictionary<string, TenantInfo> GetSubTenants(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                return new Dictionary<string, TenantInfo>();

            TenantInfo? tenant = GetTenant(tenantId);
            return tenant?.SubTenants ?? new Dictionary<string, TenantInfo>();
        }

        /// <summary>
        /// Gets a specific subtenant.
        /// </summary>
        /// <param name="parentTenantId">The parent tenant ID.</param>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <returns>The subtenant information, or null if not found.</returns>
        public TenantInfo? GetSubTenant(string parentTenantId, string subTenantId)
        {
            if (string.IsNullOrWhiteSpace(parentTenantId) || string.IsNullOrWhiteSpace(subTenantId))
                return null;

            TenantInfo? parentTenant = GetTenant(parentTenantId);
            if (parentTenant?.SubTenants.TryGetValue(subTenantId, out TenantInfo? subTenant) == true)
            {
                return subTenant;
            }

            return null;
        }

        /// <summary>
        /// Updates a subtenant's storage limit.
        /// </summary>
        /// <param name="parentTenantId">The parent tenant ID.</param>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="newStorageLimit">The new storage limit in bytes.</param>
        /// <returns>True if the update was successful.</returns>
        public async Task<bool> UpdateSubTenantStorageLimitAsync(string parentTenantId, string subTenantId, long newStorageLimit)
        {
            if (string.IsNullOrWhiteSpace(parentTenantId))
                throw new ArgumentException("Parent tenant ID cannot be null or empty", nameof(parentTenantId));

            if (string.IsNullOrWhiteSpace(subTenantId))
                throw new ArgumentException("Subtenant ID cannot be null or empty", nameof(subTenantId));

            if (newStorageLimit < 0)
                throw new ArgumentException("Storage limit cannot be negative", nameof(newStorageLimit));

            // Get parent tenant to validate storage limit
            TenantInfo? parentTenant = GetTenant(parentTenantId);
            if (parentTenant == null)
            {
                _logger.LogWarning("Parent tenant not found: {ParentTenantId}", parentTenantId);
                return false;
            }

            // Check if subtenant exists
            if (!parentTenant.SubTenants.TryGetValue(subTenantId, out TenantInfo? subTenant))
            {
                _logger.LogWarning("Subtenant not found: {SubTenantId} under parent {ParentTenantId}", subTenantId, parentTenantId);
                return false;
            }

            // Validate that new limit doesn't exceed parent's limit
            if (parentTenant.StorageLimitBytes > 0 && newStorageLimit > parentTenant.StorageLimitBytes)
            {
                _logger.LogWarning("Subtenant storage limit {NewLimit} exceeds parent limit {ParentLimit}", newStorageLimit, parentTenant.StorageLimitBytes);
                return false;
            }

            // Update the storage limit
            subTenant.StorageLimitBytes = newStorageLimit;

            bool saved = await SaveConfigurationAsync();
            if (saved)
            {
                _logger.LogInformation("Updated subtenant {SubTenantId} storage limit to {NewLimit} bytes", subTenantId, newStorageLimit);
            }

            return saved;
        }

        /// <summary>
        /// Deletes a subtenant.
        /// </summary>
        /// <param name="parentTenantId">The parent tenant ID.</param>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <returns>True if the deletion was successful.</returns>
        public async Task<bool> DeleteSubTenantAsync(string parentTenantId, string subTenantId)
        {
            if (string.IsNullOrWhiteSpace(parentTenantId))
                throw new ArgumentException("Parent tenant ID cannot be null or empty", nameof(parentTenantId));

            if (string.IsNullOrWhiteSpace(subTenantId))
                throw new ArgumentException("Subtenant ID cannot be null or empty", nameof(subTenantId));

            TenantInfo? parentTenant = GetTenant(parentTenantId);
            if (parentTenant == null)
            {
                _logger.LogWarning("Parent tenant not found: {ParentTenantId}", parentTenantId);
                return false;
            }

            if (!parentTenant.SubTenants.Remove(subTenantId))
            {
                _logger.LogWarning("Subtenant not found: {SubTenantId} under parent {ParentTenantId}", subTenantId, parentTenantId);
                return false;
            }

            bool saved = await SaveConfigurationAsync();
            if (saved)
            {
                _logger.LogInformation("Deleted subtenant {SubTenantId} under parent {ParentTenantId}", subTenantId, parentTenantId);
            }

            return saved;
        }

        /// <summary>
        /// Checks if a tenant can create subtenants (depth limit not reached).
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>True if the tenant can create subtenants.</returns>
        public async Task<bool> CanCreateSubTenantAsync(string tenantId)
        {
            await Task.CompletedTask; // So that we can keep the interface async in case we want to create an actually async version

            if (string.IsNullOrWhiteSpace(tenantId))
                throw new ArgumentException("Tenant ID cannot be null or empty.", nameof(tenantId));

            TenantInfo? tenant = GetTenant(tenantId);
            if (tenant == null)
                return false;

            int currentDepth = FindTenantDepth(tenantId, 0);
            return currentDepth < MaxSubTenantDepth;
        }

        /// <summary>
        /// Gets the depth of a tenant in the hierarchy.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The depth (0 for root tenants).</returns>
        public int GetTenantDepth(string tenantId)
        {
            TenantInfo? tenant = GetTenant(tenantId);
            if (tenant == null)
                return 0;

            // Find the depth by searching from root tenants
            return FindTenantDepth(tenantId, 0);
        }

        /// <summary>
        /// Gets a tenant by ID, searching through all levels.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The tenant information, or null if not found.</returns>
        public TenantInfo? GetTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                return null;

            TenantConfiguration config = GetConfiguration();

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
        /// Validates if a tenant has access to a specific subtenant.
        /// </summary>
        /// <param name="accessingTenantId">The ID of the tenant requesting access.</param>
        /// <param name="targetTenantId">The ID of the tenant being accessed.</param>
        /// <returns>True if the accessing tenant has access to the target tenant.</returns>
        public bool HasAccessToTenant(string accessingTenantId, string targetTenantId)
        {
            if (string.IsNullOrWhiteSpace(accessingTenantId) || string.IsNullOrWhiteSpace(targetTenantId))
                return false;

            // If the tenant is accessing itself, allow it
            if (accessingTenantId == targetTenantId)
                return true;

            TenantConfiguration config = GetConfiguration();

            // Get the accessing tenant
            TenantInfo? accessingTenant = GetTenant(accessingTenantId);
            if (accessingTenant == null)
                return false;

            // Get the target tenant
            TenantInfo? targetTenant = GetTenant(targetTenantId);
            if (targetTenant == null)
                return false;

            // Check if the target tenant is a descendant of the accessing tenant
            return IsDescendantOf(targetTenant, accessingTenant);
        }

        /// <summary>
        /// Checks if a tenant is a descendant of another tenant.
        /// </summary>
        /// <param name="potentialDescendant">The tenant to check if it's a descendant.</param>
        /// <param name="potentialAncestor">The tenant to check if it's an ancestor.</param>
        /// <returns>True if potentialDescendant is a descendant of potentialAncestor.</returns>
        private bool IsDescendantOf(TenantInfo potentialDescendant, TenantInfo potentialAncestor)
        {
            // Check if the potential descendant has the potential ancestor as a parent
            TenantInfo? current = potentialDescendant.Parent;
            while (current != null)
            {
                if (ReferenceEquals(current, potentialAncestor))
                    return true;
                current = current.Parent;
            }

            return false;
        }

        private const int MaxSubTenantDepth = 10;

        /// <summary>
        /// Recursively calculates the depth of a tenant in the hierarchy.
        /// </summary>
        /// <param name="tenantId">The tenant to check.</param>
        /// <param name="currentDepth">The current depth level.</param>
        /// <returns>The maximum depth of this tenant and its subtenants.</returns>
        private int FindTenantDepth(string tenantId, int currentDepth)
        {
            TenantConfiguration config = GetConfiguration();

            // Check root tenants first
            if (config.Tenants.TryGetValue(tenantId, out _))
            {
                return currentDepth; // Root tenant
            }

            // Search in subtenants recursively
            foreach (TenantInfo rootTenant in config.Tenants.Values)
            {
                int depth = FindTenantDepthInSubTenants(tenantId, rootTenant, currentDepth + 1);
                if (depth >= 0)
                {
                    return depth;
                }
            }

            return -1; // Not found
        }

        private int FindTenantDepthInSubTenants(string tenantId, TenantInfo tenant, int currentDepth)
        {
            if (tenant.SubTenants.TryGetValue(tenantId, out _))
            {
                return currentDepth; // Found the tenant
            }

            // Search deeper in subtenants
            foreach (TenantInfo subTenant in tenant.SubTenants.Values)
            {
                int depth = FindTenantDepthInSubTenants(tenantId, subTenant, currentDepth + 1);
                if (depth >= 0)
                {
                    return depth;
                }
            }

            return -1; // Not found in this branch
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
        /// Generates a unique API key.
        /// </summary>
        /// <returns>A unique API key.</returns>
        private string GenerateUniqueApiKey()
        {
            TenantConfiguration config = GetConfiguration();
            string apiKey;

            do
            {
                // Generate base GUID
                string baseGuid = Guid.NewGuid().ToString("N"); // No hyphens

                // Add random suffix for extra uniqueness
                string suffix = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                    .Replace("/", "").Replace("+", "").Replace("=", "")
                    .Substring(0, 8);

                apiKey = $"{baseGuid}{suffix}";

            } while (ApiKeyExists(apiKey, config));

            return apiKey;
        }

        /// <summary>
        /// Checks if an API key already exists in the configuration.
        /// </summary>
        /// <param name="apiKey">The API key to check.</param>
        /// <param name="config">The configuration to search in.</param>
        /// <returns>True if the API key exists.</returns>
        private bool ApiKeyExists(string apiKey, TenantConfiguration config)
        {
            // Check root tenants
            if (config.Tenants.Values.Any(t => t.ApiKey == apiKey))
                return true;

            // Check all subtenants recursively
            foreach (TenantInfo tenant in config.Tenants.Values)
            {
                if (ApiKeyExistsInSubTenants(apiKey, tenant))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Recursively checks if an API key exists in subtenants.
        /// </summary>
        /// <param name="apiKey">The API key to check.</param>
        /// <param name="tenant">The tenant to search in.</param>
        /// <returns>True if the API key exists.</returns>
        private bool ApiKeyExistsInSubTenants(string apiKey, TenantInfo tenant)
        {
            if (tenant.SubTenants.Values.Any(t => t.ApiKey == apiKey))
                return true;

            foreach (TenantInfo subTenant in tenant.SubTenants.Values)
            {
                if (ApiKeyExistsInSubTenants(apiKey, subTenant))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Loads the configuration from file or creates a default configuration file if it doesn't exist.
        /// </summary>
        private void LoadOrCreateConfigurationFile()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string jsonContent = File.ReadAllText(_configFilePath);
                    TenantConfiguration? loadedConfig = JsonSerializer.Deserialize<TenantConfiguration>(jsonContent, _jsonOptions);

                    if (loadedConfig != null)
                    {
                        // Rebuild parent relationships after deserialization
                        RebuildParentRelationships(loadedConfig);
                        _currentConfiguration = loadedConfig;
                        _logger.LogInformation("Configuration loaded from file: {ConfigPath}", _configFilePath);
                        return;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to deserialize configuration from file, using default: {ConfigPath}", _configFilePath);
                    }
                }

                // Create default configuration file
                CreateDefaultConfigurationFile();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from file, using default: {ConfigPath}", _configFilePath);
                CreateDefaultConfigurationFile();
            }
        }

        /// <summary>
        /// Creates a default configuration file with sample tenants.
        /// </summary>
        private void CreateDefaultConfigurationFile()
        {
            try
            {
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string jsonContent = JsonSerializer.Serialize(_currentConfiguration, _jsonOptions);
                File.WriteAllText(_configFilePath, jsonContent);

                _logger.LogInformation("Created default configuration file: {ConfigPath}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create default configuration file: {ConfigPath}", _configFilePath);
            }
        }

        /// <summary>
        /// Creates a default tenant configuration with sample tenants.
        /// </summary>
        /// <returns>A default tenant configuration.</returns>
        private static TenantConfiguration CreateDefaultConfiguration()
        {
            return new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["admin"] = new TenantInfo
                    {
                        ApiKey = "admin-secure-api-key-here",
                        StorageLimitBytes = 0, // Unlimited for admin
                        DisplayName = "System Administrator",
                        IsAdmin = true
                    },
                    ["tenant1"] = new TenantInfo
                    {
                        ApiKey = "tenant1-secure-api-key-here",
                        StorageLimitBytes = 1024 * 1024 * 1024, // 1GB
                        DisplayName = "Tenant 1",
                        IsAdmin = false
                    }
                }
            };
        }

        /// <summary>
        /// Saves the current configuration to the file.
        /// </summary>
        /// <returns><c>true</c> if the configuration was saved successfully; otherwise, <c>false</c>.</returns>
        private async Task<bool> SaveConfigurationAsync()
        {
            try
            {
                // Temporarily disable file watcher to avoid reloading during save
                if (_fileWatcher != null)
                {
                    _fileWatcher.EnableRaisingEvents = false;
                }

                TenantConfiguration configToSave;
                lock (_configLock)
                {
                    configToSave = _currentConfiguration;
                }

                string jsonContent = JsonSerializer.Serialize(configToSave, _jsonOptions);
                await File.WriteAllTextAsync(_configFilePath, jsonContent);

                // Re-enable file watcher
                if (_fileWatcher != null)
                {
                    _fileWatcher.EnableRaisingEvents = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to file: {ConfigPath}", _configFilePath);

                // Re-enable file watcher even if save failed
                if (_fileWatcher != null)
                {
                    _fileWatcher.EnableRaisingEvents = true;
                }

                return false;
            }
        }

        /// <summary>
        /// Handles configuration file change events.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The file system event arguments.</param>
        private async void OnConfigurationFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Add a small delay to ensure the file is fully written
                await Task.Delay(100);
                await ReloadConfigurationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle configuration file change: {ConfigPath}", _configFilePath);
            }
        }

        /// <summary>
        /// Disposes the service and cleans up resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _fileWatcher?.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// Rebuilds parent relationships in the tenant hierarchy after deserialization.
        /// </summary>
        /// <param name="config">The tenant configuration to rebuild parent relationships for.</param>
        /// <remarks>
        /// This method traverses the tenant hierarchy and sets the Parent property on each subtenant
        /// to reference its parent tenant. This is necessary because the Parent property is marked
        /// with [JsonIgnore] to avoid circular references during serialization.
        /// </remarks>
        private void RebuildParentRelationships(TenantConfiguration config)
        {
            foreach (TenantInfo rootTenant in config.Tenants.Values)
            {
                RebuildParentRelationshipsForTenant(rootTenant, null);
            }
        }

        /// <summary>
        /// Recursively rebuilds parent relationships for a tenant and all its subtenants.
        /// </summary>
        /// <param name="tenant">The tenant to rebuild parent relationships for.</param>
        /// <param name="parent">The parent tenant, or null if this is a root tenant.</param>
        private void RebuildParentRelationshipsForTenant(TenantInfo tenant, TenantInfo? parent)
        {
            // Set the parent for this tenant
            tenant.Parent = parent;

            // Rebuild parent relationships for all subtenants
            foreach (TenantInfo subTenant in tenant.SubTenants.Values)
            {
                RebuildParentRelationshipsForTenant(subTenant, tenant);
            }
        }
    }
}