# ByteShelfCommon

ByteShelfCommon is a shared library that contains the core data structures, interfaces, and models used across the ByteShelf ecosystem. It ensures type safety and consistency between the client and server components, providing a common contract for file storage operations.

## 🎯 Purpose

This library serves as the foundation for the ByteShelf system by defining:
- **Data Structures**: File metadata, tenant information, and storage details
- **Interfaces**: Contracts for file operations and content providers
- **Models**: Request/response models for API communication
- **Types**: Common types and enums used throughout the system

## 📦 Project Structure

```
ByteShelfCommon/
├── ShelfFileMetadata.cs           # File metadata structure
├── ShelfFile.cs                   # File representation with content
├── IContentProvider.cs            # Content provider interface
├── IShelfFileProvider.cs          # File provider interface
├── TenantInfo.cs                  # Tenant information model
├── TenantStorageInfo.cs           # Tenant storage usage information
├── QuotaCheckResult.cs            # Storage quota check results
├── CreateTenantRequest.cs         # Tenant creation request model
├── UpdateStorageLimitRequest.cs   # Storage limit update request
└── ByteShelfCommon.csproj         # Project file
```

## 🔌 Core Interfaces

### IShelfFileProvider

The main interface for file storage operations, implemented by both client and server components.

```csharp
public interface IShelfFileProvider
{
    // File operations
    Task<Guid> WriteFileAsync(string filename, string contentType, Stream content);
    Task<ShelfFile> ReadFileAsync(Guid fileId);
    Task DeleteFileAsync(Guid fileId);
    Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync();
}
```

### IContentProvider

Interface for providing file content streams, used for efficient content delivery.

```csharp
public interface IContentProvider
{
    Stream GetContentStream();
}
```

**Note**: The `HttpShelfFileProvider` implementation provides additional methods beyond the core `IShelfFileProvider` interface, including tenant-specific operations like `GetTenantInfoAsync()`, `GetStorageInfoAsync()`, and `CanStoreFileAsync()`. These are specific to the HTTP API implementation and not part of the core interface.

## 📊 Data Models

### ShelfFileMetadata

Represents file metadata without the actual content.

```csharp
public class ShelfFileMetadata
{
    public Guid FileId { get; set; }
    public string OriginalFilename { get; set; }
    public string ContentType { get; set; }
    public long FileSize { get; set; }
    public List<Guid> ChunkIds { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**Properties:**
- `FileId`: Unique identifier for the file
- `OriginalFilename`: Original name of the uploaded file
- `ContentType`: MIME type of the file
- `FileSize`: Total size of the file in bytes
- `ChunkIds`: List of chunk IDs that make up the file
- `CreatedAt`: Timestamp when the file was created

### ShelfFile

Represents a complete file with both metadata and content.

```csharp
public class ShelfFile
{
    public ShelfFileMetadata Metadata { get; }
    public IContentProvider ContentProvider { get; }
    
    public Stream GetContentStream() => ContentProvider.GetContentStream();
}
```

**Usage:**
```csharp
ShelfFile file = await provider.ReadFileAsync(fileId);

// Access metadata
Console.WriteLine($"File: {file.Metadata.OriginalFilename}");
Console.WriteLine($"Size: {file.Metadata.FileSize} bytes");

// Access content
using Stream content = file.GetContentStream();
// Process the content...
```

### TenantInfo

Represents tenant configuration and information.

```csharp
public class TenantInfo
{
    public string TenantId { get; set; }
    public string DisplayName { get; set; }
    public string ApiKey { get; set; }
    public long StorageLimitBytes { get; set; }
    public bool IsAdmin { get; set; }
}
```

**Properties:**
- `TenantId`: Unique identifier for the tenant
- `DisplayName`: Human-readable name for the tenant
- `ApiKey`: API key for authentication
- `StorageLimitBytes`: Maximum storage allowed (0 = unlimited for admins)
- `IsAdmin`: Whether the tenant has administrative privileges

### TenantStorageInfo

Represents current storage usage for a tenant.

```csharp
public class TenantStorageInfo
{
    public long UsedBytes { get; set; }
    public long LimitBytes { get; set; }
    public long AvailableBytes { get; set; }
    public int FileCount { get; set; }
}
```

**Properties:**
- `UsedBytes`: Current storage usage in bytes
- `LimitBytes`: Storage limit in bytes (0 = unlimited)
- `AvailableBytes`: Available storage space
- `FileCount`: Number of files stored

### TenantInfoResponse

Represents tenant information returned by the API, including admin status and current usage.

```csharp
public class TenantInfoResponse
{
    public string TenantId { get; }
    public string DisplayName { get; }
    public bool IsAdmin { get; }
    public long StorageLimitBytes { get; }
    public long CurrentUsageBytes { get; }
    public long AvailableSpaceBytes { get; }
    public double UsagePercentage { get; }
}
```

**Properties:**
- `TenantId`: Unique identifier for the tenant
- `DisplayName`: Human-readable name for the tenant
- `IsAdmin`: Whether the tenant has administrative privileges
- `StorageLimitBytes`: Maximum storage allowed (0 = unlimited for admins)
- `CurrentUsageBytes`: Current storage usage in bytes
- `AvailableSpaceBytes`: Available storage space in bytes
- `UsagePercentage`: Percentage of storage used (0-100)

### QuotaCheckResult

Result of a storage quota check operation.

```csharp
public class QuotaCheckResult
{
    public bool CanStore { get; set; }
    public long RequiredBytes { get; set; }
    public long AvailableBytes { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**Properties:**
- `CanStore`: Whether the file can be stored
- `RequiredBytes`: Bytes required for the file
- `AvailableBytes`: Available storage space
- `ErrorMessage`: Error message if storage is not possible

## 📝 Request/Response Models

### CreateTenantRequest

Request model for creating a new tenant.

```csharp
public class CreateTenantRequest
{
    public string TenantId { get; set; }
    public string DisplayName { get; set; }
    public long StorageLimitBytes { get; set; }
    public bool IsAdmin { get; set; }
}
```

### UpdateStorageLimitRequest

Request model for updating tenant storage limits.

```csharp
public class UpdateStorageLimitRequest
{
    public long StorageLimitBytes { get; set; }
}
```

## 🔧 Usage Examples

### Working with File Metadata

```csharp
// Create metadata
ShelfFileMetadata metadata = new ShelfFileMetadata
{
    FileId = Guid.NewGuid(),
    OriginalFilename = "example.txt",
    ContentType = "text/plain",
    FileSize = 1024,
    ChunkIds = new List<Guid> { Guid.NewGuid() },
    CreatedAt = DateTime.UtcNow
};

// Access metadata properties
Console.WriteLine($"File ID: {metadata.FileId}");
Console.WriteLine($"Filename: {metadata.OriginalFilename}");
Console.WriteLine($"Size: {metadata.FileSize} bytes");
Console.WriteLine($"Chunks: {metadata.ChunkIds.Count}");
```

### Working with Tenant Information

```csharp
// Create tenant info
TenantInfo tenant = new TenantInfo
{
    TenantId = "tenant1",
    DisplayName = "Tenant 1",
    ApiKey = "secure-api-key",
    StorageLimitBytes = 1073741824, // 1GB
    IsAdmin = false
};

// Check if tenant is admin
if (tenant.IsAdmin)
{
    Console.WriteLine($"{tenant.DisplayName} has admin privileges");
}

// Check storage limit
if (tenant.StorageLimitBytes == 0)
{
    Console.WriteLine($"{tenant.DisplayName} has unlimited storage");
}
else
{
    Console.WriteLine($"{tenant.DisplayName} has {tenant.StorageLimitBytes} bytes limit");
}
```

### Working with Storage Information

```csharp
// Get storage info
TenantStorageInfo storageInfo = await provider.GetStorageInfoAsync();

// Display usage information
Console.WriteLine($"Used: {storageInfo.UsedBytes} bytes");
Console.WriteLine($"Limit: {storageInfo.LimitBytes} bytes");
Console.WriteLine($"Available: {storageInfo.AvailableBytes} bytes");
Console.WriteLine($"Files: {storageInfo.FileCount}");

// Check if storage is unlimited
bool isUnlimited = storageInfo.LimitBytes == 0;
if (isUnlimited)
{
    Console.WriteLine("Storage is unlimited");
}
else
{
    double usagePercent = (double)storageInfo.UsedBytes / storageInfo.LimitBytes * 100;
    Console.WriteLine($"Usage: {usagePercent:F1}%");
}
```

### Working with Tenant Information and Admin Status

```csharp
// Get tenant information including admin status
TenantInfoResponse tenantInfo = await provider.GetTenantInfoAsync();

// Display tenant information
Console.WriteLine($"Tenant: {tenantInfo.DisplayName}");
Console.WriteLine($"Admin: {tenantInfo.IsAdmin}");
Console.WriteLine($"Storage Limit: {tenantInfo.StorageLimitBytes} bytes");
Console.WriteLine($"Current Usage: {tenantInfo.CurrentUsageBytes} bytes");
Console.WriteLine($"Usage Percentage: {tenantInfo.UsagePercentage:F1}%");

// Check admin privileges
if (tenantInfo.IsAdmin)
{
    Console.WriteLine("This tenant has administrative privileges");
    // Enable admin-specific features
    // Show admin UI controls
    // Allow access to admin endpoints
}
else
{
    Console.WriteLine("This is a regular tenant");
    // Show regular user interface
    // Hide admin-specific features
}
```

### Working with Quota Checks

```csharp
// Check if a file can be stored
long fileSize = 1024 * 1024; // 1MB
QuotaCheckResult result = await provider.CheckQuotaAsync(fileSize);

if (result.CanStore)
{
    Console.WriteLine($"Can store {fileSize} bytes");
    Console.WriteLine($"Available: {result.AvailableBytes} bytes");
}
else
{
    Console.WriteLine($"Cannot store file: {result.ErrorMessage}");
    Console.WriteLine($"Required: {result.RequiredBytes} bytes");
    Console.WriteLine($"Available: {result.AvailableBytes} bytes");
}
```

## 🧪 Testing

### Unit Tests
```bash
dotnet test ByteShelfCommon.Tests
```

### Test Coverage
The ByteShelfCommon library includes comprehensive unit tests for:
- Data model validation
- Interface contract verification
- Serialization/deserialization
- Edge cases and error conditions

## 🔒 Type Safety

### Nullable Reference Types
The library uses nullable reference types to ensure type safety:
- Required properties are non-nullable
- Optional properties are nullable
- Proper null checking is enforced at compile time

### Validation
Data models include validation attributes where appropriate:
- Required fields are marked as required
- String lengths are validated
- Numeric ranges are enforced

## 📚 Integration

### Client Integration
The ByteShelfClient library implements `IShelfFileProvider` and uses these data models for all operations.

### Server Integration
The ByteShelf API server uses these models for:
- API request/response serialization
- Internal data representation
- Database storage (where applicable)

### Cross-Platform Compatibility
The library is designed to work across different .NET platforms:
- .NET 8.0+
- ASP.NET Core
- Console applications
- Desktop applications

## 🔧 Development

### Adding New Models
1. Create the new model class
2. Add appropriate properties with proper types
3. Include XML documentation
4. Add unit tests
5. Update this README if needed

### Adding New Interfaces
1. Define the interface contract
2. Document all methods and properties
3. Add unit tests for interface compliance
4. Update implementations in client and server

### Versioning
- Follow semantic versioning
- Maintain backward compatibility when possible
- Document breaking changes clearly

## 📚 Related Documentation

- [ByteShelf API Server](../ByteShelf/README.md) - Server implementation
- [ByteShelfClient](../ByteShelfClient/README.md) - Client implementation
- [Main README](../README.md) - Overview of the entire solution 