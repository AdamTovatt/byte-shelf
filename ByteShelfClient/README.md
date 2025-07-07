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

// Create HTTP client
using HttpClient httpClient = new HttpClient();
httpClient.BaseAddress = new Uri("https://localhost:7001");

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
```

### Delete a File
```csharp
// Delete a file and all its chunks
await provider.DeleteFileAsync(fileId);
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

## 📁 Project Structure

```
ByteShelfClient/
├── HttpShelfFileProvider.cs      # Main HTTP client implementation
├── ChunkedStream.cs              # Stream wrapper for chunked operations
├── ChunkedHttpContentProvider.cs # HTTP content provider for chunks
├── ChunkConfiguration.cs         # Chunk size configuration
└── ByteShelfClient.csproj        # Project file
```

## 🔌 API Reference

### IShelfFileProvider Interface

The core interface for file storage operations:

#### File Operations
- `WriteFileAsync(string filename, string contentType, Stream content)` - Upload a file
- `ReadFileAsync(Guid fileId)` - Download a file
- `DeleteFileAsync(Guid fileId)` - Delete a file
- `GetFilesAsync()` - List all files

### HttpShelfFileProvider Class

HTTP-based implementation of `IShelfFileProvider` with additional tenant-specific operations.

#### Constructor Overloads
- `HttpShelfFileProvider(HttpClient httpClient, string apiKey)` - Basic constructor
- `HttpShelfFileProvider(HttpClient httpClient, string apiKey, ChunkConfiguration config)` - With custom chunk size

#### Additional Methods (Beyond IShelfFileProvider)

##### Storage Operations
- `GetStorageInfoAsync()` - Get tenant storage information
- `CanStoreFileAsync(long fileSize)` - Check if file can be stored
- `WriteFileWithQuotaCheckAsync(string filename, string contentType, Stream content, bool checkQuotaFirst = true)` - Upload with optional quota checking

##### Tenant Operations
- `GetTenantInfoAsync()` - Get tenant information including admin status

## 🧪 Testing

### Unit Tests
```bash
dotnet test ByteShelfClient.Tests
```

### Integration Tests
```bash
dotnet test ByteShelf.Integration.Tests
```

## 🔒 Security Considerations

### API Key Management
- Store API keys in environment variables or secure configuration
- Never hardcode API keys in source code
- Rotate API keys regularly
- Use different API keys for different environments

### HTTPS Usage
- Always use HTTPS in production
- Validate SSL certificates
- Consider certificate pinning for additional security

### Error Handling
- Don't expose sensitive information in error messages
- Log errors appropriately for debugging
- Handle authentication failures gracefully

## 🚀 Performance Tips

### Chunk Size Optimization
- **Small files**: Use smaller chunks (512KB - 1MB)
- **Large files**: Use larger chunks (2MB - 4MB)
- **Network conditions**: Consider network latency when choosing chunk size

### Memory Management
- Use `using` statements for streams
- Dispose of resources properly
- Consider using `MemoryStream` for small files in memory

### Concurrent Operations
- The client supports concurrent operations
- Use `Task.WhenAll` for multiple file operations
- Be mindful of server-side rate limits

## 🔧 Troubleshooting

### Common Issues

#### Authentication Errors
```
System.Net.Http.HttpRequestException: Response status code does not indicate success: 401 (Unauthorized)
```
**Solution**: Check that your API key is correct and the tenant exists.

#### File Not Found
```
System.IO.FileNotFoundException: File not found
```
**Solution**: Verify the file ID exists and belongs to your tenant.

#### Quota Exceeded
```
ByteShelfClient.QuotaExceededException: Storage quota exceeded
```
**Solution**: Check your tenant's storage quota and delete unnecessary files.

#### Network Issues
```
System.Net.Http.HttpRequestException: Unable to connect to the remote server
```
**Solution**: Verify the server URL and network connectivity.

## 📚 Related Documentation

- [ByteShelf API Server](../ByteShelf/README.md) - Server documentation
- [ByteShelfCommon](../ByteShelfCommon/README.md) - Shared data structures
- [Main README](../README.md) - Overview of the entire solution
- [API Documentation](../ByteShelf/README.md#api-endpoints) - Complete API reference 