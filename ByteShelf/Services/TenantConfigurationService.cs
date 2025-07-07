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
    }
}