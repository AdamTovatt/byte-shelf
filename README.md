# ByteShelf

ByteShelf is a comprehensive multi-tenant file storage solution built with .NET 8. It provides a scalable, secure, and efficient way to store and manage files with automatic chunking, tenant isolation, and quota management.

## 🏗️ Architecture Overview

ByteShelf consists of several interconnected projects that work together to provide a complete file storage solution:

```
┌─────────────────┐    HTTP/REST    ┌─────────────────┐
│                 │ ◄──────────────►│                 │
│ ByteShelfClient │                 │   ByteShelf     │
│   (Client Lib)  │                 │  (API Server)   │
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

## 📦 Project Structure

### Core Projects

| Project | Purpose | Description |
|---------|---------|-------------|
| **ByteShelf** | API Server | Main HTTP API server with multi-tenant support, authentication, and file storage |
| **ByteShelfClient** | Client Library | .NET client library for easy integration with the ByteShelf API |
| **ByteShelfCommon** | Shared Library | Common data structures, interfaces, and models used across all projects |

### Test Projects

| Project | Purpose | Description |
|---------|---------|-------------|
| **ByteShelf.Tests** | Unit Tests | Unit tests for the API server components |
| **ByteShelfClient.Tests** | Client Tests | Unit tests for the client library |
| **ByteShelfCommon.Tests** | Common Tests | Unit tests for shared data structures |
| **ByteShelf.Integration.Tests** | Integration Tests | End-to-end integration tests for the complete system |

## 🚀 Key Features

### Multi-Tenant Architecture
- **Tenant Isolation**: Each tenant's files are completely isolated
- **Per-Tenant Quotas**: Configurable storage limits per tenant
- **Subtenant Hierarchy**: Support for nested tenant structures with up to 10 levels deep
- **Shared Storage**: Parent and subtenants can share storage quotas
- **API Key Authentication**: Secure access with tenant-specific API keys
- **Admin Management**: Administrative interface for tenant management

### File Storage
- **Automatic Chunking**: Large files are automatically split into configurable chunks
- **Streaming Support**: Efficient memory usage for large files
- **Metadata Storage**: JSON-based metadata with file information
- **Content Types**: Full MIME type support

### Developer Experience
- **RESTful API**: Standard HTTP endpoints for all operations
- **Swagger Documentation**: Auto-generated API documentation
- **C# Client Library**: Easy-to-use .NET client
- **Comprehensive Testing**: Full test coverage across all components

## 🛠️ Quick Start

### Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code

### 1. Build the Solution
```bash
dotnet build
```

### 2. Run the API Server
```bash
cd ByteShelf
dotnet run
```

The server will start on `https://localhost:7001` with Swagger documentation available at `/swagger`.

### 3. Use the Client Library
```csharp
using HttpClient httpClient = new HttpClient();
httpClient.BaseAddress = new Uri("https://localhost:7001");

// Create client with tenant API key
IShelfFileProvider provider = new HttpShelfFileProvider(httpClient, "your-api-key");

// Upload a file
using FileStream fileStream = File.OpenRead("example.txt");
Guid fileId = await provider.WriteFileAsync("example.txt", "text/plain", fileStream);

// Download a file
ShelfFile file = await provider.ReadFileAsync(fileId);
using Stream content = file.GetContentStream();
// Process the file content...
```

## 📋 Configuration

### Server Configuration
The API server is configured through `appsettings.json` and environment variables:

```json
{
  "StoragePath": "/var/byteshelf/storage",
  "ChunkConfiguration": {
    "ChunkSizeBytes": 1048576
  }
}
```

### Tenant Configuration
Tenants are managed through an external JSON file with hot-reload support. The configuration supports hierarchical tenant structures with shared storage quotas:

```json
{
  "RequireAuthentication": true,
  "Tenants": {
    "admin": {
      "ApiKey": "admin-secure-api-key-here",
      "StorageLimitBytes": 0,
      "DisplayName": "System Administrator",
      "IsAdmin": true,
      "SubTenants": {}
    },
    "parent-tenant": {
      "ApiKey": "parent-secure-api-key-here",
      "StorageLimitBytes": 1073741824,
      "DisplayName": "Parent Organization",
      "IsAdmin": false,
      "SubTenants": {
        "child-tenant-1": {
          "ApiKey": "child1-secure-api-key-here",
          "StorageLimitBytes": 1073741824,
          "DisplayName": "Child Department 1",
          "IsAdmin": false,
          "SubTenants": {}
        },
        "child-tenant-2": {
          "ApiKey": "child2-secure-api-key-here",
          "StorageLimitBytes": 536870912,
          "DisplayName": "Child Department 2",
          "IsAdmin": false,
          "SubTenants": {}
        }
      }
    }
  }
}
```

**Subtenant Features:**
- **Hierarchical Structure**: Up to 10 levels of nesting supported
- **Shared Storage**: Parent and subtenants share the parent's storage quota
- **Individual Limits**: Subtenants can have their own storage limits (must not exceed parent's limit)
- **API Key Inheritance**: Subtenants can access parent's files, but not vice versa
- **Automatic Parent Relationships**: Parent references are automatically rebuilt when configuration is loaded

### Environment Variables
```bash
# Set tenant configuration file path
export BYTESHELF_TENANT_CONFIG_PATH=/etc/byteshelf/tenants.json

# Set storage path
export BYTESHELF_STORAGE_PATH=/var/byteshelf/storage

# Set chunk size
export BYTESHELF_CHUNK_SIZE_BYTES=2097152
```

## 🧪 Testing

### Run All Tests
```bash
dotnet test
```

### Run Specific Test Projects
```bash
# Unit tests
dotnet test ByteShelf.Tests
dotnet test ByteShelfClient.Tests
dotnet test ByteShelfCommon.Tests

# Integration tests
dotnet test ByteShelf.Integration.Tests
```

## 📚 API Documentation

### Core Endpoints
- `GET /api/files` - List all files for the authenticated tenant
- `GET /api/files/{fileId}/metadata` - Get file metadata
- `POST /api/files/metadata` - Create file metadata
- `PUT /api/chunks/{chunkId}` - Upload a chunk
- `GET /api/chunks/{chunkId}` - Download a chunk
- `DELETE /api/files/{fileId}` - Delete a file and all its chunks

### Admin Endpoints
- `GET /api/admin/tenants` - List all tenants with usage information
- `POST /api/admin/tenants` - Create a new tenant
- `PUT /api/admin/tenants/{tenantId}/storage-limit` - Update tenant storage limit
- `DELETE /api/admin/tenants/{tenantId}` - Delete tenant

### Subtenant Management Endpoints
- `POST /api/tenant/subtenants` - Create a new subtenant under the authenticated tenant
- `GET /api/tenant/subtenants` - List all subtenants of the authenticated tenant
- `GET /api/tenant/subtenants/{subTenantId}` - Get information about a specific subtenant
- `PUT /api/tenant/subtenants/{subTenantId}/storage-limit` - Update subtenant storage limit
- `DELETE /api/tenant/subtenants/{subTenantId}` - Delete a subtenant

### Configuration Endpoints
- `GET /api/config/chunk-size` - Get chunk size configuration
- `GET /api/tenant/info` - Get tenant information including admin status
- `GET /api/tenant/storage` - Get storage usage for authenticated tenant
- `GET /api/tenant/storage/can-store` - Check if tenant can store a file of given size

## 🔒 Security

### Authentication
- **API Key Authentication**: All requests require a valid API key
- **Tenant Isolation**: Files are completely isolated between tenants
- **Quota Enforcement**: Storage limits are enforced per tenant
- **Admin Privileges**: Admin tenants have additional management capabilities

### Shared Storage Behavior
When using subtenants, storage quotas work as follows:

- **Parent Quota**: The parent tenant's storage limit is shared among all subtenants
- **Individual Limits**: Subtenants can have their own storage limits (must not exceed parent's limit)
- **Shared Consumption**: When a subtenant stores data, it consumes from the parent's shared quota
- **Quota Enforcement**: Storage is denied when the combined usage would exceed the parent's limit
- **Unlimited Storage**: Admin tenants (with 0 storage limit) have unlimited storage for themselves and subtenants

**Example Scenario:**
- Parent tenant has 500MB storage limit
- Two subtenants each think they can use 500MB (inheriting parent's limit)
- If subtenant A uses 400MB, subtenant B can only use 100MB
- Parent tenant can also only use 100MB remaining

### Best Practices
1. Use strong, cryptographically secure API keys
2. Store API keys in environment variables
3. Use HTTPS in production environments
4. Regularly rotate API keys
5. Monitor API access for suspicious activity
6. Plan storage quotas carefully when using subtenants
7. Consider the shared nature of storage when designing tenant hierarchies

## 🚀 Deployment

See [DEPLOYMENT.md](DEPLOYMENT.md) for detailed deployment instructions, including:
- Docker deployment
- Linux service setup
- Production configuration
- Performance tuning

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass (`dotnet test`)
6. Commit your changes (`git commit -m 'Add amazing feature'`)
7. Push to the branch (`git push origin feature/amazing-feature`)
8. Open a Pull Request

## 📄 License

This project is licensed under the MIT License.

## 📖 Documentation

- [ByteShelf API Server](ByteShelf/README.md) - Detailed documentation for the API server
- [ByteShelfClient](ByteShelfClient/README.md) - Client library documentation
- [ByteShelfCommon](ByteShelfCommon/README.md) - Shared library documentation
- [Deployment Guide](DEPLOYMENT.md) - Production deployment instructions 