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
Tenants are managed through an external JSON file with hot-reload support:

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

### Environment Variables
```bash
# Set tenant configuration file path
export BYTESHELF_TENANT_CONFIG_PATH=/etc/byteshelf/tenants.json

# Set storage path
export BYTESHELF_STORAGE_PATH=/var/byteshelf/storage

# Set chunk size
export ChunkConfiguration__ChunkSizeBytes=2097152
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

### Best Practices
1. Use strong, cryptographically secure API keys
2. Store API keys in environment variables
3. Use HTTPS in production environments
4. Regularly rotate API keys
5. Monitor API access for suspicious activity

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