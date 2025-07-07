# ByteShelf API Server

The ByteShelf API Server is a multi-tenant file storage service built with ASP.NET Core 8. It provides a RESTful HTTP API for storing, retrieving, and managing files with automatic chunking, tenant isolation, and quota management.

## 🚀 Features

### Multi-Tenant Architecture
- **Tenant Isolation**: Each tenant's files are stored in separate directories
- **API Key Authentication**: Secure access with tenant-specific API keys
- **Per-Tenant Quotas**: Configurable storage limits per tenant
- **Admin Management**: Administrative interface for tenant management

### File Storage
- **Automatic Chunking**: Large files are automatically split into configurable chunks
- **Streaming Support**: Efficient memory usage for large files
- **Metadata Storage**: JSON-based metadata with file information
- **Content Types**: Full MIME type support

### API Features
- **RESTful Endpoints**: Standard HTTP methods for all operations
- **Swagger Documentation**: Auto-generated API documentation
- **Error Handling**: Comprehensive error responses with meaningful messages
- **Logging**: Detailed logging for debugging and monitoring

## 🏗️ Architecture

### Project Structure
```
ByteShelf/
├── Controllers/           # HTTP API controllers
│   ├── AdminController.cs      # Tenant management endpoints
│   ├── ChunksController.cs     # File chunk operations
│   ├── ConfigController.cs     # Configuration endpoints
│   ├── FilesController.cs      # File metadata operations
│   └── TenantController.cs     # Tenant-specific operations
├── Services/              # Business logic services
│   ├── FileStorageService.cs   # File storage operations
│   ├── StorageService.cs       # Storage abstraction
│   └── TenantConfigurationService.cs # Tenant configuration management
├── Configuration/         # Configuration classes
│   ├── AuthenticationConfiguration.cs
│   ├── ChunkConfiguration.cs
│   └── TenantConfiguration.cs
├── Middleware/            # Custom middleware
│   └── ApiKeyAuthenticationMiddleware.cs
├── Extensions/            # Extension methods
│   └── HttpContextExtensions.cs
└── Program.cs            # Application entry point
```

### File Storage Structure
```
storage-path/
├── [tenant-id]/
│   ├── metadata/
│   │   ├── [file-id-1].json
│   │   ├── [file-id-2].json
│   │   └── ...
│   └── bin/
│       ├── [chunk-id-1].bin
│       ├── [chunk-id-2].bin
│       └── ...
└── ...
```

## 📋 Configuration

### appsettings.json
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
    "ChunkSizeBytes": 1048576
  }
}
```

### Environment Variables
```bash
# Storage configuration
export BYTESHELF_STORAGE_PATH=/var/byteshelf/storage
export BYTESHELF_CHUNK_SIZE_BYTES=2097152

# Tenant configuration
export BYTESHELF_TENANT_CONFIG_PATH=/etc/byteshelf/tenants.json
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

## 🔌 API Endpoints

### File Operations
- `GET /api/files` - List all files for the authenticated tenant
- `GET /api/files/{fileId}/metadata` - Get file metadata
- `POST /api/files/metadata` - Create file metadata
- `DELETE /api/files/{fileId}` - Delete a file and all its chunks

### Chunk Operations
- `PUT /api/chunks/{chunkId}` - Upload a chunk
- `GET /api/chunks/{chunkId}` - Download a chunk

### Tenant Operations
- `GET /api/tenant/info` - Get tenant information including admin status
- `GET /api/tenant/storage` - Get storage usage for authenticated tenant
- `GET /api/tenant/storage/can-store` - Check if tenant can store a file of given size

### Admin Operations
- `GET /api/admin/tenants` - List all tenants with usage information
- `GET /api/admin/tenants/{tenantId}` - Get specific tenant information
- `POST /api/admin/tenants` - Create a new tenant
- `PUT /api/admin/tenants/{tenantId}/storage-limit` - Update tenant storage limit
- `DELETE /api/admin/tenants/{tenantId}` - Delete tenant

### Configuration
- `GET /api/config/chunk-size` - Get chunk size configuration

## 🔒 Security

### Authentication
All API endpoints require authentication via API key in the `X-API-Key` header, except for:
- `/health` - Health check endpoints
- `/metrics` - Metrics endpoints
- `/` - Root endpoint

### Tenant Isolation
- Each tenant's files are stored in separate directories
- API keys are tenant-specific
- Cross-tenant access is prevented at the API level

### Quota Enforcement
- Storage limits are enforced per tenant
- File uploads are rejected if they would exceed the tenant's quota
- Admin tenants can have unlimited storage (when StorageLimitBytes is 0)

## 🚀 Running the Server

### Development
```bash
cd ByteShelf
dotnet run
```

The server will start on `https://localhost:7001` with Swagger documentation available at `/swagger`.

### Production
```bash
# Build the application
dotnet publish -c Release -o ./publish

# Run the published application
cd publish
dotnet ByteShelf.dll
```

### Docker
```bash
# Build the Docker image
docker build -t byteshelf .

# Run the container
docker run -p 7001:7001 \
  -e BYTESHELF_STORAGE_PATH=/app/storage \
  -e BYTESHELF_TENANT_CONFIG_PATH=/app/config/tenants.json \
  -v /host/storage:/app/storage \
  -v /host/config:/app/config \
  byteshelf
```

## 🧪 Testing

### Unit Tests
```bash
dotnet test ByteShelf.Tests
```

### Integration Tests
```bash
dotnet test ByteShelf.Integration.Tests
```

## 📊 Monitoring

### Health Checks
- `GET /health` - Basic health check
- `GET /health/ready` - Readiness probe
- `GET /health/live` - Liveness probe

### Logging
The application uses structured logging with different log levels:
- **Information**: Normal operation events
- **Warning**: Non-critical issues
- **Error**: Errors that need attention
- **Debug**: Detailed debugging information

### Metrics
- `GET /metrics` - Prometheus-compatible metrics endpoint

## 🔧 Development

### Adding New Endpoints
1. Create a new controller in the `Controllers/` directory
2. Add the controller to dependency injection in `Program.cs`
3. Add appropriate authentication attributes
4. Write unit tests for the new functionality

### Adding New Services
1. Create the service interface and implementation
2. Register the service in `Program.cs`
3. Inject the service into controllers as needed
4. Write unit tests for the service

### Configuration Changes
1. Add configuration properties to the appropriate configuration class
2. Update `appsettings.json` with default values
3. Document environment variable overrides
4. Update this README if needed

## 📚 Related Documentation

- [ByteShelfClient](../ByteShelfClient/README.md) - Client library for .NET applications
- [ByteShelfCommon](../ByteShelfCommon/README.md) - Shared data structures and interfaces
- [Main README](../README.md) - Overview of the entire ByteShelf solution
- [Deployment Guide](../DEPLOYMENT.md) - Production deployment instructions 