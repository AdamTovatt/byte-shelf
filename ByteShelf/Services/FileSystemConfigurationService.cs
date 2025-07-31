using ByteShelfCommon.FileSystem;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ByteShelf.Services
{
    /// <summary>
    /// Filesystem-based configuration service that manages tenants and users
    /// through the filesystem structure instead of external configuration files.
    /// 
    /// Directory structure:
    /// /storage/
    /// ├── tenant1/
    /// │   ├── tenant-config.json
    /// │   └── users/
    /// │       ├── user1/
    /// │       │   ├── user-config.json
    /// │       │   └── files/
    /// │       └── user2/
    /// │           ├── user-config.json
    /// │           └── files/
    /// └── tenant2/
    ///     ├── tenant-config.json
    ///     └── users/
    /// </summary>
    public class FileSystemConfigurationService : IFileSystemConfigurationService, IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        private readonly string _storagePath;
        private readonly ILogger<FileSystemConfigurationService> _logger;
        private readonly object _cacheLock = new();
        private readonly SemaphoreSlim _fileLock = new(1, 1);

        // Caches for performance
        private readonly ConcurrentDictionary<string, TenantConfig> _tenantCache = new();
        private readonly ConcurrentDictionary<string, UserConfig> _userCache = new();
        private readonly ConcurrentDictionary<string, string> _apiKeyToUserCache = new(); // apiKey -> "tenantId:userId"

        // File watchers for hot-reload
        private readonly Dictionary<string, FileSystemWatcher> _tenantWatchers = new();
        private readonly Dictionary<string, FileSystemWatcher> _userWatchers = new();
        private bool _disposed = false;

        public FileSystemConfigurationService(ILogger<FileSystemConfigurationService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Get storage path from environment variable or use default
            string? envPath = Environment.GetEnvironmentVariable("BYTESHELF_STORAGE_PATH");
            _storagePath = string.IsNullOrWhiteSpace(envPath) ? "./storage" : envPath;

            // Ensure storage directory exists
            Directory.CreateDirectory(_storagePath);

            _logger.LogInformation("FileSystemConfigurationService initialized with storage path: {StoragePath}", _storagePath);
        }

        #region Tenant Operations

        public async Task<TenantConfig?> GetTenantAsync(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                return null;

            // Check cache first
            if (_tenantCache.TryGetValue(tenantId, out var cachedTenant))
                return cachedTenant;

            // Load from filesystem
            string tenantPath = Path.Combine(_storagePath, tenantId);
            string configPath = Path.Combine(tenantPath, "tenant-config.json");

            if (!File.Exists(configPath))
                return null;

            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    string json = await File.ReadAllTextAsync(configPath);
                    var tenant = JsonSerializer.Deserialize<TenantConfig>(json, JsonOptions);
                    
                    if (tenant != null)
                    {
                        _tenantCache.TryAdd(tenantId, tenant);
                    }
                    
                    return tenant;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load tenant config for {TenantId}", tenantId);
                return null;
            }
        }

        public async Task<IEnumerable<TenantConfig>> GetAllTenantsAsync()
        {
            var tenants = new List<TenantConfig>();

            if (!Directory.Exists(_storagePath))
                return tenants;

            var tenantDirectories = Directory.GetDirectories(_storagePath);

            foreach (var tenantDir in tenantDirectories)
            {
                string tenantId = Path.GetFileName(tenantDir);
                var tenant = await GetTenantAsync(tenantId);
                if (tenant != null)
                {
                    tenants.Add(tenant);
                }
            }

            return tenants;
        }

        public async Task<bool> CreateTenantAsync(string tenantId, string displayName, long storageLimitBytes)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(displayName))
                return false;

            string tenantPath = Path.Combine(_storagePath, tenantId);
            string usersPath = Path.Combine(tenantPath, "users");
            string configPath = Path.Combine(tenantPath, "tenant-config.json");

            if (Directory.Exists(tenantPath))
                return false; // Tenant already exists

            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    // Create directory structure
                    Directory.CreateDirectory(tenantPath);
                    Directory.CreateDirectory(usersPath);

                    // Create tenant config
                    var tenantConfig = new TenantConfig
                    {
                        TenantId = tenantId,
                        DisplayName = displayName,
                        StorageLimitBytes = storageLimitBytes,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        ModifiedAt = DateTime.UtcNow
                    };

                    string json = JsonSerializer.Serialize(tenantConfig, JsonOptions);
                    await File.WriteAllTextAsync(configPath, json);

                    // Cache the tenant
                    _tenantCache.TryAdd(tenantId, tenantConfig);

                    _logger.LogInformation("Created tenant {TenantId} with display name '{DisplayName}'", tenantId, displayName);
                    return true;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create tenant {TenantId}", tenantId);
                return false;
            }
        }

        public async Task<bool> UpdateTenantAsync(string tenantId, UpdateTenantRequest request)
        {
            var tenant = await GetTenantAsync(tenantId);
            if (tenant == null)
                return false;

            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    // Update properties
                    if (!string.IsNullOrWhiteSpace(request.DisplayName))
                        tenant.DisplayName = request.DisplayName;
                    
                    if (request.StorageLimitBytes.HasValue)
                        tenant.StorageLimitBytes = request.StorageLimitBytes.Value;
                    
                    if (request.IsActive.HasValue)
                        tenant.IsActive = request.IsActive.Value;

                    tenant.ModifiedAt = DateTime.UtcNow;

                    // Save to file
                    string configPath = Path.Combine(_storagePath, tenantId, "tenant-config.json");
                    string json = JsonSerializer.Serialize(tenant, JsonOptions);
                    await File.WriteAllTextAsync(configPath, json);

                    // Update cache
                    _tenantCache.TryRemove(tenantId, out _);
                    _tenantCache.TryAdd(tenantId, tenant);

                    return true;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update tenant {TenantId}", tenantId);
                return false;
            }
        }

        public async Task<bool> DeleteTenantAsync(string tenantId)
        {
            string tenantPath = Path.Combine(_storagePath, tenantId);
            
            if (!Directory.Exists(tenantPath))
                return false;

            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    // Remove from caches first
                    _tenantCache.TryRemove(tenantId, out _);
                    
                    // Remove all users from cache
                    var usersToRemove = _userCache.Where(kvp => kvp.Value.TenantId == tenantId).ToList();
                    foreach (var userKvp in usersToRemove)
                    {
                        string userKey = $"{tenantId}:{userKvp.Value.UserId}";
                        _userCache.TryRemove(userKey, out _);
                        
                        // Remove from API key cache
                        var apiKeyToRemove = _apiKeyToUserCache.Where(kvp => kvp.Value == userKey).FirstOrDefault();
                        if (!apiKeyToRemove.Equals(default(KeyValuePair<string, string>)))
                        {
                            _apiKeyToUserCache.TryRemove(apiKeyToRemove.Key, out _);
                        }
                    }

                    // Delete directory
                    Directory.Delete(tenantPath, true);

                    _logger.LogInformation("Deleted tenant {TenantId}", tenantId);
                    return true;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete tenant {TenantId}", tenantId);
                return false;
            }
        }

        public async Task<bool> TenantExistsAsync(string tenantId)
        {
            var tenant = await GetTenantAsync(tenantId);
            return tenant != null;
        }

        #endregion

        #region User Operations

        public async Task<UserConfig?> GetUserAsync(string tenantId, string userId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
                return null;

            string userKey = $"{tenantId}:{userId}";

            // Check cache first
            if (_userCache.TryGetValue(userKey, out var cachedUser))
                return cachedUser;

            // Load from filesystem
            string userPath = Path.Combine(_storagePath, tenantId, "users", userId);
            string configPath = Path.Combine(userPath, "user-config.json");

            if (!File.Exists(configPath))
                return null;

            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    string json = await File.ReadAllTextAsync(configPath);
                    var user = JsonSerializer.Deserialize<UserConfig>(json, JsonOptions);
                    
                    if (user != null)
                    {
                        _userCache.TryAdd(userKey, user);
                        _apiKeyToUserCache.TryAdd(user.ApiKey, userKey);
                    }
                    
                    return user;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load user config for {TenantId}:{UserId}", tenantId, userId);
                return null;
            }
        }

        public async Task<UserConfig?> GetUserByApiKeyAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return null;

            // Check cache first
            if (_apiKeyToUserCache.TryGetValue(apiKey, out var userKey))
            {
                var parts = userKey.Split(':');
                if (parts.Length == 2)
                {
                    return await GetUserAsync(parts[0], parts[1]);
                }
            }

            // If not in cache, we need to scan all users (expensive operation)
            var tenants = await GetAllTenantsAsync();
            foreach (var tenant in tenants)
            {
                var users = await GetUsersForTenantAsync(tenant.TenantId);
                var user = users.FirstOrDefault(u => u.ApiKey == apiKey);
                if (user != null)
                {
                    return user;
                }
            }

            return null;
        }

        public async Task<IEnumerable<UserConfig>> GetUsersForTenantAsync(string tenantId)
        {
            var users = new List<UserConfig>();

            string usersPath = Path.Combine(_storagePath, tenantId, "users");
            if (!Directory.Exists(usersPath))
                return users;

            var userDirectories = Directory.GetDirectories(usersPath);

            foreach (var userDir in userDirectories)
            {
                string userId = Path.GetFileName(userDir);
                var user = await GetUserAsync(tenantId, userId);
                if (user != null)
                {
                    users.Add(user);
                }
            }

            return users;
        }

        public async Task<bool> CreateUserAsync(string tenantId, CreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(request.UserId))
                return false;

            // Check if tenant exists
            var tenant = await GetTenantAsync(tenantId);
            if (tenant == null)
                return false;

            string userPath = Path.Combine(_storagePath, tenantId, "users", request.UserId);
            string filesPath = Path.Combine(userPath, "files");
            string configPath = Path.Combine(userPath, "user-config.json");

            if (Directory.Exists(userPath))
                return false; // User already exists

            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    // Create directory structure
                    Directory.CreateDirectory(userPath);
                    Directory.CreateDirectory(filesPath);

                    // Generate API key
                    string apiKey = await GenerateApiKeyAsync();

                    // Create user config
                    var userConfig = new UserConfig
                    {
                        UserId = request.UserId,
                        TenantId = tenantId,
                        ApiKey = apiKey,
                        DisplayName = request.DisplayName,
                        Email = request.Email,
                        StorageLimitBytes = request.StorageLimitBytes,
                        IsActive = true,
                        IsAdmin = request.IsAdmin,
                        CreatedAt = DateTime.UtcNow,
                        ModifiedAt = DateTime.UtcNow
                    };

                    string json = JsonSerializer.Serialize(userConfig, JsonOptions);
                    await File.WriteAllTextAsync(configPath, json);

                    // Cache the user
                    string userKey = $"{tenantId}:{request.UserId}";
                    _userCache.TryAdd(userKey, userConfig);
                    _apiKeyToUserCache.TryAdd(apiKey, userKey);

                    _logger.LogInformation("Created user {UserId} for tenant {TenantId}", request.UserId, tenantId);
                    return true;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user {UserId} for tenant {TenantId}", request.UserId, tenantId);
                return false;
            }
        }

        public async Task<bool> UpdateUserAsync(string tenantId, string userId, UpdateUserRequest request)
        {
            var user = await GetUserAsync(tenantId, userId);
            if (user == null)
                return false;

            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    // Update properties
                    if (!string.IsNullOrWhiteSpace(request.DisplayName))
                        user.DisplayName = request.DisplayName;
                    
                    if (!string.IsNullOrWhiteSpace(request.Email))
                        user.Email = request.Email;
                    
                    if (request.StorageLimitBytes.HasValue)
                        user.StorageLimitBytes = request.StorageLimitBytes.Value;
                    
                    if (request.IsActive.HasValue)
                        user.IsActive = request.IsActive.Value;
                    
                    if (request.IsAdmin.HasValue)
                        user.IsAdmin = request.IsAdmin.Value;

                    user.ModifiedAt = DateTime.UtcNow;

                    // Save to file
                    string configPath = Path.Combine(_storagePath, tenantId, "users", userId, "user-config.json");
                    string json = JsonSerializer.Serialize(user, JsonOptions);
                    await File.WriteAllTextAsync(configPath, json);

                    // Update cache
                    string userKey = $"{tenantId}:{userId}";
                    _userCache.TryRemove(userKey, out _);
                    _userCache.TryAdd(userKey, user);

                    return true;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update user {UserId} for tenant {TenantId}", userId, tenantId);
                return false;
            }
        }

        public async Task<bool> DeleteUserAsync(string tenantId, string userId)
        {
            string userPath = Path.Combine(_storagePath, tenantId, "users", userId);
            
            if (!Directory.Exists(userPath))
                return false;

            // Get user config to remove from API key cache
            var user = await GetUserAsync(tenantId, userId);

            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    // Remove from caches
                    string userKey = $"{tenantId}:{userId}";
                    _userCache.TryRemove(userKey, out _);
                    
                    if (user != null)
                    {
                        _apiKeyToUserCache.TryRemove(user.ApiKey, out _);
                    }

                    // Delete directory
                    Directory.Delete(userPath, true);

                    _logger.LogInformation("Deleted user {UserId} for tenant {TenantId}", userId, tenantId);
                    return true;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete user {UserId} for tenant {TenantId}", userId, tenantId);
                return false;
            }
        }

        public async Task<bool> UserExistsAsync(string tenantId, string userId)
        {
            var user = await GetUserAsync(tenantId, userId);
            return user != null;
        }

        #endregion

        #region Authentication

        public async Task<(UserConfig? user, TenantConfig? tenant)> AuthenticateAsync(string apiKey)
        {
            var user = await GetUserByApiKeyAsync(apiKey);
            if (user == null || !user.IsActive)
                return (null, null);

            var tenant = await GetTenantAsync(user.TenantId);
            if (tenant == null || !tenant.IsActive)
                return (null, null);

            // Update last access time
            user.LastAccessAt = DateTime.UtcNow;
            await UpdateUserLastAccessAsync(user);

            return (user, tenant);
        }

        private async Task UpdateUserLastAccessAsync(UserConfig user)
        {
            try
            {
                string configPath = Path.Combine(_storagePath, user.TenantId, "users", user.UserId, "user-config.json");
                string json = JsonSerializer.Serialize(user, JsonOptions);
                await File.WriteAllTextAsync(configPath, json);

                // Update cache
                string userKey = $"{user.TenantId}:{user.UserId}";
                _userCache.TryRemove(userKey, out _);
                _userCache.TryAdd(userKey, user);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update last access time for user {UserId}", user.UserId);
            }
        }

        #endregion

        #region Utility Methods

        public async Task<string> GenerateApiKeyAsync()
        {
            await Task.CompletedTask;
            
            using var rng = RandomNumberGenerator.Create();
            byte[] bytes = new byte[32];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        public async Task RefreshCacheAsync()
        {
            _tenantCache.Clear();
            _userCache.Clear();
            _apiKeyToUserCache.Clear();

            // Preload all tenants and users
            await GetAllTenantsAsync();
            
            _logger.LogInformation("Configuration cache refreshed");
        }

        public void StartFileWatching()
        {
            // Implementation for file watching would go here
            // For now, we'll skip this to keep the implementation simpler
            _logger.LogInformation("File watching started (not implemented yet)");
        }

        public void StopFileWatching()
        {
            foreach (var watcher in _tenantWatchers.Values)
            {
                watcher?.Dispose();
            }
            _tenantWatchers.Clear();

            foreach (var watcher in _userWatchers.Values)
            {
                watcher?.Dispose();
            }
            _userWatchers.Clear();

            _logger.LogInformation("File watching stopped");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                StopFileWatching();
                _fileLock?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }
}