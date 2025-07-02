# ByteShelf

ByteShelf is a three-part file storage server system that provides a simple API for writing and reading files with automatic chunking support. It consists of three main components that work together to provide a complete file storage solution.

## Overview

ByteShelf is designed to be simple yet powerful, offering:
- **Chunked file storage** for handling large files efficiently
- **RESTful HTTP API** for easy integration
- **C# client library** for seamless .NET integration
- **JSON metadata storage** for file information
- **Configurable chunk sizes** for optimal performance

## Architecture

The system consists of three separate projects:

1. **ByteShelf** - The HTTP API server that handles file storage and retrieval
2. **ByteShelfCommon** - Shared data structures and interfaces used by both client and server
3. **ByteShelfClient** - C# client library for easy integration with .NET applications

### How They Interact

```
┌─────────────────┐    HTTP/REST    ┌─────────────────┐
│                 │ ◄──────────────►│                 │
│ ByteShelfClient │                 │   ByteShelf     │
│                 │                 │   (API Server)  │
└─────────────────┘                 └─────────────────┘
         │                                   │
         │                                   │
         └───────────┐              ┌────────┘
                     │              │
                     ▼              ▼
              ┌─────────────────────────────────┐
              │        ByteShelfCommon          │
              │     (Shared Data Structures)    │
              └─────────────────────────────────┘
```

## Components

### ByteShelfCommon

The shared library containing the core data structures and interfaces:

- **`ShelfFileMetadata`** - Contains file information (ID, filename, content type, size, chunk IDs, creation date)
- **`ShelfFile`** - Represents a file with metadata and content stream
- **`IContentProvider`** - Interface for providing file content streams
- **`IShelfFileProvider`** - Interface defining file operations (read, write, delete, list)

This library ensures type safety and consistency between the client and server.

### ByteShelf (API Server)

The HTTP API server that provides REST endpoints for file operations.

#### Features
- **Chunked file storage** - Files are automatically split into configurable chunks
- **JSON metadata storage** - File information stored as JSON files
- **RESTful API** - Standard HTTP endpoints for all operations
- **Configurable storage** - Customizable storage path and chunk sizes
- **Logging support** - Comprehensive logging for debugging and monitoring

#### File Storage Structure

The server stores files in the following structure:
```
byte-shelf-storage/
├── metadata/
│   ├── [file-id-1].json
│   ├── [file-id-2].json
│   └── ...
└── bin/
    ├── [chunk-id-1].bin
    ├── [chunk-id-2].bin
    └── ...
```

#### API Endpoints

- `GET /api/config/chunk-size` - Get chunk size configuration
- `GET /api/files` - List all files
- `GET /api/files/{fileId}/metadata` - Get file metadata
- `POST /api/files/metadata` - Create file metadata
- `PUT /api/chunks/{chunkId}` - Upload a chunk
- `GET /api/chunks/{chunkId}` - Download a chunk
- `DELETE /api/files/{fileId}` - Delete a file and all its chunks

#### Running the Server

1. **Prerequisites**
   - .NET 8.0 SDK
   - Visual Studio 2022 or VS Code

2. **Build and Run**
   ```bash
   cd ByteShelf
   dotnet run
   ```

   The server will start on `https://localhost:7001` (or the next available port).

   **Note:** By default, API key authentication is enabled. You must set an API key in your configuration (see below) and provide it in all client requests. See the [API Key Authentication](#api-key-authentication) section for details.

3. **Configuration**

   The server can be configured through `appsettings.json`:

   ```json
   {
     "StoragePath": "byte-shelf-storage",
     "Authentication": {
       "ApiKey": "your-secure-api-key-here",
       "RequireAuthentication": true
     },
     "ChunkConfiguration": {
       "ChunkSizeBytes": 1048576
     }
   }
   ```

   - **StoragePath**: Directory where files will be stored (default: "byte-shelf-storage")
   - **ChunkSizeBytes**: Size of each chunk in bytes (default: 1MB)
   - **Authentication**: See [API Key Authentication](#api-key-authentication)

4. **Environment Variables**

   You can also override settings using environment variables:
   ```bash
   set StoragePath=C:\MyStorage
   set ChunkConfiguration__ChunkSizeBytes=2097152
   set Authentication__ApiKey=your-secure-api-key-here
   set Authentication__RequireAuthentication=true
   dotnet run
   ```

5. **Swagger Documentation**

   When running in development mode, you can access the API documentation at:
   ```
   https://localhost:7001/swagger
   ```

### ByteShelfClient

A C# client library that provides a simple interface for interacting with the ByteShelf server.

#### Features
- **Simple API** - Easy-to-use methods for file operations
- **Automatic chunking** - Handles file splitting and reconstruction automatically
- **Streaming support** - Efficient memory usage for large files
- **Error handling** - Proper exception handling with meaningful error messages
- **API key authentication** - All requests include the API key if provided

#### Usage

1. **Setup**
   ```csharp
   using HttpClient httpClient = new HttpClient();
   httpClient.BaseAddress = new Uri("https://localhost:7001");
   
   // Pass the API key to the client
   IShelfFileProvider provider = new HttpShelfFileProvider(httpClient, "your-secure-api-key-here");
   ```

2. **Upload a File**
   ```csharp
   using FileStream fileStream = File.OpenRead("example.txt");
   Guid fileId = await provider.WriteFileAsync("example.txt", "text/plain", fileStream);
   Console.WriteLine($"File uploaded with ID: {fileId}");
   ```

3. **Download a File**
   ```csharp
   ShelfFile file = await provider.ReadFileAsync(fileId);
   using Stream content = file.GetContentStream();
   using FileStream output = File.Create("downloaded.txt");
   await content.CopyToAsync(output);
   ```

4. **List Files**
   ```csharp
   IEnumerable<ShelfFileMetadata> files = await provider.GetFilesAsync();
   foreach (ShelfFileMetadata file in files)
   {
       Console.WriteLine($"{file.OriginalFilename} ({file.FileSize} bytes)");
   }
   ```

5. **Delete a File**
   ```csharp
   await provider.DeleteFileAsync(fileId);
   ```

#### Integration with Dependency Injection

```csharp
// In Program.cs or Startup.cs
builder.Services.AddHttpClient<IShelfFileProvider, HttpShelfFileProvider>(client =>
{
    client.BaseAddress = new Uri("https://localhost:7001");
    client.DefaultRequestHeaders.Add("X-API-Key", "your-secure-api-key-here");
});
```

## Development

### Building the Solution

```bash
dotnet build
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests for specific project
dotnet test ByteShelfCommon.Tests
dotnet test ByteShelfClient.Tests
dotnet test ByteShelf.Tests
```

### Project Structure

```
ByteShelf/
├── ByteShelf/                 # API Server
│   ├── Controllers/           # HTTP API controllers
│   ├── Services/              # Business logic services
│   ├── Configuration/         # Configuration classes
│   └── Program.cs            # Application entry point
├── ByteShelfCommon/           # Shared library
│   ├── ShelfFileMetadata.cs   # File metadata structure
│   ├── ShelfFile.cs          # File representation
│   ├── IContentProvider.cs   # Content provider interface
│   └── IShelfFileProvider.cs # File provider interface
├── ByteShelfClient/           # Client library
│   └── HttpShelfFileProvider.cs # HTTP client implementation
├── ByteShelfCommon.Tests/     # Tests for shared library
├── ByteShelfClient.Tests/     # Tests for client library
├── ByteShelf.Tests/           # Tests for API server
└── README.md                  # This file
```

## Configuration

### Server Configuration

The ByteShelf server is configured through `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "StoragePath": "/var/byteshelf/storage",
  "ChunkConfiguration": {
    "ChunkSizeBytes": 2097152
  }
}
```

### Tenant Configuration

Tenants are managed through an external JSON configuration file that supports hot-reloading. The configuration file path is determined by the `BYTESHELF_TENANT_CONFIG_PATH` environment variable, or defaults to `./tenant-config.json` if not set.

The tenant configuration file has the following structure:

```json
{
  "RequireAuthentication": true,
  "Tenants": {
    "admin": {
      "ApiKey": "admin-secure-api-key-here",
      "StorageLimitBytes": 0,
      "DisplayName": "System Administrator",
      "IsAdmin": true
    },
    "tenant1": {
      "ApiKey": "tenant1-secure-api-key-here",
      "StorageLimitBytes": 1073741824,
      "DisplayName": "Tenant 1",
      "IsAdmin": false
    }
  }
}
```

- `RequireAuthentication`: Whether API key authentication is required for all endpoints
- `Tenants`: Dictionary of tenant configurations
  - `ApiKey`: The API key required for authentication
  - `StorageLimitBytes`: Maximum storage allowed (0 = unlimited for admins)
  - `DisplayName`: Human-readable name for the tenant
  - `IsAdmin`: Whether the tenant has administrative privileges

#### Tenant Management

Tenants can be managed through the admin API endpoints:

- `GET /api/admin/tenants` - List all tenants with usage information
- `GET /api/admin/tenants/{tenantId}` - Get specific tenant information
- `POST /api/admin/tenants` - Create a new tenant
- `PUT /api/admin/tenants/{tenantId}/storage-limit` - Update tenant storage limit
- `DELETE /api/admin/tenants/{tenantId}` - Delete tenant (only if no files exist)

The configuration file is automatically created with default settings if it doesn't exist, and changes are persisted immediately when using the admin API.

### Environment Variables

```bash
# Set tenant configuration file path
set BYTESHELF_TENANT_CONFIG_PATH=/etc/byteshelf/tenants.json

# Set storage path
set StoragePath=/var/byteshelf/storage

# Set chunk size
set ChunkConfiguration__ChunkSizeBytes=2097152
```

## Performance Considerations

- **Chunk Size**: Larger chunks reduce HTTP overhead but increase memory usage
- **Concurrent Requests**: The server handles multiple concurrent file operations
- **Streaming**: Files are streamed to avoid loading entire files into memory
- **Storage**: Consider using fast storage (SSD) for better performance

## Security Notes

- API key authentication is included by default. All API requests require a valid API key unless authentication is explicitly disabled in configuration.
- Consider adding HTTPS in production environments
- Implement proper access controls for production use
- Validate file types and sizes as needed for your use case

### Multi-Tenant API Key Authentication

ByteShelf supports multi-tenant API key authentication to secure access to the file storage API. Each tenant has their own API key and storage quota.

#### Configuration

Tenants are configured in the external tenant configuration file (see Tenant Configuration section above). Each tenant has:

- **ApiKey**: The secret key that clients must provide to access the API
- **StorageLimitBytes**: Maximum storage allowed (0 = unlimited for admins)
- **DisplayName**: Human-readable name for the tenant
- **IsAdmin**: Whether the tenant has administrative privileges

#### Client Usage

Update your client code to include the tenant's API key:

```csharp
using HttpClient httpClient = new HttpClient();
httpClient.BaseAddress = new Uri("https://localhost:7001");

// Pass the tenant's API key to the client
IShelfFileProvider provider = new HttpShelfFileProvider(httpClient, "tenant1-secure-api-key-here");
```

The client will automatically include the API key in the `X-API-Key` header for all requests.

#### Admin Access

Admin tenants have additional privileges:
- Access to admin API endpoints for tenant management
- Unlimited storage (when StorageLimitBytes is set to 0)
- Ability to view all tenant information and usage statistics

#### Security Best Practices

1. **Use Strong API Keys**: Generate cryptographically secure random keys for each tenant
2. **Environment Variables**: Store API keys in environment variables, not in source code
3. **HTTPS Only**: Always use HTTPS in production to protect API keys in transit
4. **Key Rotation**: Regularly rotate API keys for better security
5. **Access Logging**: Monitor API access for suspicious activity
6. **Tenant Isolation**: Each tenant's files are stored in separate directories
7. **Quota Enforcement**: Storage quotas are enforced per tenant

#### Excluded Endpoints

The following endpoints are excluded from authentication for system health monitoring:
- `/health` - Health check endpoints
- `/metrics` - Metrics endpoints  
- `/` - Root endpoint

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass
6. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details. 