# ByteShelfClient

ByteShelfClient is a .NET client library that provides a simple and intuitive interface for interacting with the ByteShelf API server. It handles all the complexities of file chunking, HTTP communication, and authentication, making it easy to integrate file storage capabilities into your .NET applications.

## 🚀 Features

### Easy Integration
- **Simple API**: Clean, intuitive methods for file operations
- **Automatic Chunking**: Handles file splitting and reconstruction automatically
- **Streaming Support**: Efficient memory usage for large files
- **Error Handling**: Comprehensive exception handling with meaningful error messages

### Authentication & Security
- **API Key Authentication**: Automatic inclusion of API keys in requests
- **Tenant Support**: Full support for multi-tenant ByteShelf deployments
- **Subtenant Management**: Complete support for hierarchical tenant structures
- **Secure Communication**: Works with HTTPS endpoints

### Performance
- **Streaming Operations**: Files are streamed to avoid loading entire files into memory
- **Configurable Chunking**: Customizable chunk sizes for optimal performance
- **Efficient HTTP Usage**: Optimized HTTP requests with proper content handling

## 📦 Installation

### NuGet Package
```bash
dotnet add package ByteShelfClient
```

### Project Reference
```bash
dotnet add reference ../ByteShelfClient/ByteShelfClient.csproj
```

## 🔧 Basic Usage

### Setup
```csharp
using ByteShelfClient;
using ByteShelfCommon;

// Create HTTP client using the helper method
using HttpClient httpClient = HttpShelfFileProvider.CreateHttpClient("https://localhost:7001");

// Create client with tenant API key
IShelfFileProvider provider = new HttpShelfFileProvider(httpClient, "your-api-key");
```

### Upload a File
```csharp
// Upload a file from disk
using FileStream fileStream = File.OpenRead("example.txt");
Guid fileId = await provider.WriteFileAsync("example.txt", "text/plain", fileStream);
Console.WriteLine($"File uploaded with ID: {fileId}");

// Upload from memory stream
using MemoryStream memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));
Guid fileId2 = await provider.WriteFileAsync("hello.txt", "text/plain", memoryStream);

// Upload a file to a specific subtenant (parent access required)
using FileStream subtenantFileStream = File.OpenRead("subtenant-file.txt");
Guid subtenantFileId = await provider.WriteFileForTenantAsync("subtenant-id", "subtenant-file.txt", "text/plain", subtenantFileStream);
Console.WriteLine($"File uploaded to subtenant with ID: {subtenantFileId}");
```

### Download a File
```csharp
// Download and save to disk
ShelfFile file = await provider.ReadFileAsync(fileId);
using Stream content = file.GetContentStream();
using FileStream output = File.Create("downloaded.txt");
await content.CopyToAsync(output);

// Download and process in memory
ShelfFile file2 = await provider.ReadFileAsync(fileId2);
using Stream content2 = file2.GetContentStream();
using StreamReader reader = new StreamReader(content2);
string content = await reader.ReadToEndAsync();
Console.WriteLine($"File content: {content}");

// Download a file from a specific subtenant (parent access required)
ShelfFile subtenantFile = await provider.ReadFileForTenantAsync("subtenant-id", fileId);
using Stream subtenantContent = subtenantFile.GetContentStream();
// Process the file content...
```

### List Files
```csharp
// Get all files for the tenant
IEnumerable<ShelfFileMetadata> files = await provider.GetFilesAsync();
foreach (ShelfFileMetadata file in files)
{
    Console.WriteLine($"{file.OriginalFilename} ({file.FileSize} bytes)");
    Console.WriteLine($"  ID: {file.FileId}");
    Console.WriteLine($"  Content Type: {file.ContentType}");
    Console.WriteLine($"  Created: {file.CreatedAt}");
}

// Get files from a specific subtenant (parent access required)
IEnumerable<ShelfFileMetadata> subtenantFiles = await provider.GetFilesForTenantAsync("subtenant-id");
foreach (ShelfFileMetadata file in subtenantFiles)
{
    Console.WriteLine($"Subtenant file: {file.OriginalFilename}");
}
```

### Delete a File
```csharp
// Delete a file and all its chunks
await provider.DeleteFileAsync(fileId);

// Delete a file from a specific subtenant (parent access required)
await provider.DeleteFileForTenantAsync("subtenant-id", fileId);
```

### Get File Metadata
```csharp
// Get metadata without downloading the file
ShelfFileMetadata metadata = await provider.GetFileMetadataAsync(fileId);
Console.WriteLine($"File: {metadata.OriginalFilename}");
Console.WriteLine($"Size: {metadata.FileSize} bytes");
Console.WriteLine($"Chunks: {metadata.ChunkIds.Count}");
```

## 🔧 Advanced Usage

### Custom Chunk Configuration
```csharp
// Create client with custom chunk size
ChunkConfiguration config = new ChunkConfiguration
{
    ChunkSizeBytes = 2 * 1024 * 1024 // 2MB chunks
};

IShelfFileProvider provider = new HttpShelfFileProvider(httpClient, "your-api-key", config);
```

### Error Handling
```csharp
try
{
    Guid fileId = await provider.WriteFileAsync("large-file.zip", "application/zip", fileStream);
}
catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
{
    Console.WriteLine("Authentication failed. Check your API key.");
}
catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
{
    Console.WriteLine("Access denied. Check your permissions.");
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"File not found: {ex.Message}");
}
catch (QuotaExceededException ex)
{
    Console.WriteLine($"Storage quota exceeded: {ex.Message}");
}
```

### Dependency Injection
```csharp
// In Program.cs or Startup.cs
builder.Services.AddHttpClient<IShelfFileProvider, HttpShelfFileProvider>(client =>
{
    client.BaseAddress = new Uri("https://localhost:7001");
    client.DefaultRequestHeaders.Add("X-API-Key", "your-api-key");
});

// Or using the helper method for simpler setup
builder.Services.AddSingleton<IShelfFileProvider>(provider =>
{
    return new HttpShelfFileProvider(
        HttpShelfFileProvider.CreateHttpClient("https://localhost:7001"),
        "your-api-key");
});

// In your service or controller
public class FileService
{
    private readonly IShelfFileProvider _fileProvider;

    public FileService(IShelfFileProvider fileProvider)
    {
        _fileProvider = fileProvider;
    }

    public async Task<Guid> UploadFileAsync(string filename, string contentType, Stream content)
    {
        return await _fileProvider.WriteFileAsync(filename, contentType, content);
    }
}
```

### Tenant-Specific Operations
```csharp
// Get tenant information including admin status
TenantInfoResponse tenantInfo = await provider.GetTenantInfoAsync();
Console.WriteLine($"Tenant: {tenantInfo.DisplayName}");
Console.WriteLine($"Admin: {tenantInfo.IsAdmin}");
Console.WriteLine($"Storage Limit: {tenantInfo.StorageLimitBytes} bytes");
Console.WriteLine($"Current Usage: {tenantInfo.CurrentUsageBytes} bytes");

// Check if tenant is admin
if (tenantInfo.IsAdmin)
{
    Console.WriteLine("This tenant has administrative privileges");
    // Show admin-specific UI or enable admin features
}

// Check storage usage
TenantStorageInfo storageInfo = await provider.GetStorageInfoAsync();
Console.WriteLine($"Used: {storageInfo.UsedBytes} bytes");
Console.WriteLine($"Limit: {storageInfo.LimitBytes} bytes");
Console.WriteLine($"Available: {storageInfo.AvailableBytes} bytes");

// Check if you can store a file
bool canStore = await provider.CanStoreFileAsync(fileSize);
if (canStore)
{
    // Proceed with upload
    Guid fileId = await provider.WriteFileAsync(filename, contentType, content);
}
else
{
    Console.WriteLine("Not enough storage space available.");
}
```

### Subtenant Management
```csharp
// Create a new subtenant
string subTenantId = await provider.CreateSubTenantAsync("Department A");
Console.WriteLine($"Created subtenant with ID: {subTenantId}");

// Create a subtenant under another subtenant (hierarchical folder creation)
string parentSubTenantId = "parent-subtenant-id";
string nestedSubTenantId = await provider.CreateSubTenantUnderSubTenantAsync(parentSubTenantId, "Nested Department");
Console.WriteLine($"Created nested subtenant with ID: {nestedSubTenantId}");

// List all subtenants
Dictionary<string, TenantInfoResponse> subTenants = await provider.GetSubTenantsAsync();
foreach (KeyValuePair<string, TenantInfoResponse> kvp in subTenants)
{
    Console.WriteLine($"Subtenant: {kvp.Value.DisplayName}");
    Console.WriteLine($"  ID: {kvp.Key}");
    Console.WriteLine($"  Storage Limit: {kvp.Value.StorageLimitBytes} bytes");
    Console.WriteLine($"  Current Usage: {kvp.Value.CurrentUsageBytes} bytes");
}

// List subtenants under a specific subtenant (hierarchical folder browsing)
string parentSubTenantId = "parent-subtenant-id";
Dictionary<string, TenantInfoResponse> nestedSubTenants = await provider.GetSubTenantsUnderSubTenantAsync(parentSubTenantId);
foreach (KeyValuePair<string, TenantInfoResponse> kvp in nestedSubTenants)
{
    Console.WriteLine($"Nested Subtenant: {kvp.Value.DisplayName}");
    Console.WriteLine($"  ID: {kvp.Key}");
    Console.WriteLine($"  Storage Limit: {kvp.Value.StorageLimitBytes} bytes");
    Console.WriteLine($"  Current Usage: {kvp.Value.CurrentUsageBytes} bytes");
}

// Get specific subtenant information
TenantInfoResponse subTenant = await provider.GetSubTenantAsync(subTenantId);
Console.WriteLine($"Subtenant Details:");
Console.WriteLine($"  Display Name: {subTenant.DisplayName}");
Console.WriteLine($"  Storage Limit: {subTenant.StorageLimitBytes} bytes");
Console.WriteLine($"  Current Usage: {subTenant.CurrentUsageBytes} bytes");
Console.WriteLine($"  Is Admin: {subTenant.IsAdmin}");

// Update subtenant storage limit
long newLimit = 500 * 1024 * 1024; // 500MB
await provider.UpdateSubTenantStorageLimitAsync(subTenantId, newLimit);
Console.WriteLine($"Updated storage limit to {newLimit} bytes");

// Delete a subtenant
await provider.DeleteSubTenantAsync(subTenantId);
Console.WriteLine("Subtenant deleted successfully");
```

### Working with Shared Storage Quotas
```csharp
// Check storage availability considering shared quotas
TenantInfoResponse tenantInfo = await provider.GetTenantInfoAsync();

if (tenantInfo.StorageLimitBytes > 0)
{
    // Tenant has a specific storage limit
    double usagePercent = tenantInfo.UsagePercentage;
    long availableBytes = tenantInfo.AvailableSpaceBytes;
    
    Console.WriteLine($"Usage: {usagePercent:F1}%");
    Console.WriteLine($"Available: {availableBytes} bytes");
    
    if (usagePercent > 90)
    {
        Console.WriteLine("Warning: Storage usage is high!");
    }
}
else
{
    // Unlimited storage (admin tenant)
    Console.WriteLine("Unlimited storage available");
}

// Check if a large file can be stored (considers shared quotas)
long largeFileSize = 100 * 1024 * 1024; // 100MB
bool canStore = await provider.CanStoreFileAsync(largeFileSize);

if (canStore)
{
    Console.WriteLine("Large file can be stored");
}
else
{
    Console.WriteLine("Cannot store large file - quota exceeded");
}
```

## 📁 Project Structure

```
```

#### Additional Methods (Beyond IShelfFileProvider)

##### Storage Operations
- `GetStorageInfoAsync()` - Get tenant storage information
- `CanStoreFileAsync(long fileSize)` - Check if file can be stored
- `WriteFileWithQuotaCheckAsync(string filename, string contentType, Stream content, bool checkQuotaFirst = true)` - Upload with optional quota checking

##### Tenant Operations
- `GetTenantInfoAsync()` - Get tenant information including admin status

##### Subtenant Management
- `CreateSubTenantAsync(string displayName)` - Create a new subtenant
- `CreateSubTenantUnderSubTenantAsync(string parentSubtenantId, string displayName)` - Create a new subtenant under a specific subtenant (hierarchical folder creation)
- `GetSubTenantsAsync()` - List all subtenants
- `GetSubTenantAsync(string subTenantId)` - Get specific subtenant information
- `GetSubTenantsUnderSubTenantAsync(string parentSubtenantId)` - List all subtenants under a specific subtenant (hierarchical folder browsing)
- `UpdateSubTenantStorageLimitAsync(string subTenantId, long storageLimitBytes)` - Update subtenant storage limit
- `DeleteSubTenantAsync(string subTenantId)` - Delete a subtenant

#### Network Issues
```
System.Net.Http.HttpRequestException: Unable to connect to the remote server
```
**Solution**: Verify the server URL and network connectivity.

#### Subtenant Management Issues
```
System.IO.FileNotFoundException: Subtenant not found
```
**Solution**: Verify the subtenant ID exists and belongs to your tenant.

```
System.InvalidOperationException: Cannot create subtenant: maximum depth reached
```
**Solution**: The tenant hierarchy has reached the maximum depth of 10 levels. Create subtenants under a different parent.

```
System.InvalidOperationException: Cannot create subtenant: maximum of 50 subtenants per tenant reached
```
**Solution**: The tenant has reached the maximum of 50 subtenants. Create subtenants under a different parent tenant.

```
System.IO.FileNotFoundException: Parent subtenant with ID {parentSubtenantId} not found
```
**Solution**: The parent subtenant ID does not exist or you don't have access to it. Verify the parent subtenant ID and your permissions.

```
System.ArgumentException: Storage limit cannot be negative
```
**Solution**: Ensure the storage limit is a positive number or zero for unlimited storage.

```
System.ArgumentException: Subtenant storage limit exceeds parent limit
```
**Solution**: The subtenant's storage limit cannot exceed the parent's storage limit.

### Parent Access to Subtenant Files
The client library supports hierarchical access where parent tenants can access files from their subtenants:

```csharp
// List files from a subtenant
IEnumerable<ShelfFileMetadata> subtenantFiles = await provider.GetFilesForTenantAsync("subtenant-id");

// Download a file from a subtenant
ShelfFile subtenantFile = await provider.ReadFileForTenantAsync("subtenant-id", fileId);

// Upload a file to a subtenant
Guid uploadedFileId = await provider.WriteFileForTenantAsync("subtenant-id", "filename.txt", "text/plain", contentStream);

// Delete a file from a subtenant
await provider.DeleteFileForTenantAsync("subtenant-id", fileId);
```

**Access Control**: All tenant-specific operations require that the authenticated tenant has access to the target tenant (either be the same tenant or a parent). If access is denied, an `UnauthorizedAccessException` is thrown.