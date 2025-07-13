# ByteShelf API Server

The ByteShelf API Server is a multi-tenant file storage service built with ASP.NET Core 8. It provides a RESTful HTTP API for storing, retrieving, and managing files with automatic chunking, tenant isolation, and quota management.

## 🚀 Features

### Multi-Tenant Architecture
- **Tenant Isolation**: Each tenant's files are stored in separate directories
- **Hierarchical Tenants**: Support for subtenants with parent-child relationships and unlimited nesting depth
- **API Key Authentication**: Secure access with tenant-specific API keys
- **Per-Tenant Quotas**: Configurable storage limits per tenant
- **Shared Storage Quotas**: Parent and subtenants can share storage limits
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
Tenants are managed through an external JSON file with hot-reload support. The configuration supports hierarchical tenants with subtenants:

```json
{
  "RequireAuthentication": true,
  "Tenants": {
    "admin": {
      "ApiKey": "admin-secure-api-key-here",
      "StorageLimitBytes": 0,
      "DisplayName": "System Administrator",
      "IsAdmin": true,
      "SubTenants": {
        "subtenant1": {
          "ApiKey": "subtenant1-secure-api-key-here",
          "StorageLimitBytes": 536870912,
          "DisplayName": "Subtenant 1",
          "IsAdmin": false,
          "SubTenants": {}
        }
      }
    },
    "tenant1": {
      "ApiKey": "tenant1-secure-api-key-here",
      "StorageLimitBytes": 1073741824,
      "DisplayName": "Tenant 1",
      "IsAdmin": false,
      "SubTenants": {}
    }
  }
}
```

## 🔌 API Endpoints

### File Operations
- `GET /api/files` - List all files for the authenticated tenant
- `GET /api/files/{targetTenantId}` - List all files for a specific tenant (parent access required)
- `GET /api/files/{fileId}/metadata` - Get file metadata
- `GET /api/files/{targetTenantId}/{fileId}/metadata` - Get file metadata for a specific tenant (parent access required)
- `POST /api/files/metadata` - Create file metadata
- `POST /api/files/{targetTenantId}/metadata` - Create file metadata for a specific tenant (parent access required)
- `GET /api/files/{fileId}/download` - Download a complete file
- `GET /api/files/{targetTenantId}/{fileId}/download` - Download a complete file from a specific tenant (parent access required)
- `DELETE /api/files/{fileId}` - Delete a file and all its chunks
- `DELETE /api/files/{targetTenantId}/{fileId}` - Delete a file and all its chunks from a specific tenant (parent access required)

### Chunk Operations
- `PUT /api/chunks/{chunkId}` - Upload a chunk
- `PUT /api/chunks/{targetTenantId}/{chunkId}` - Upload a chunk for a specific tenant (parent access required)
- `GET /api/chunks/{chunkId}` - Download a chunk
- `GET /api/chunks/{targetTenantId}/{chunkId}` - Download a chunk from a specific tenant (parent access required)

### Tenant Operations
- `GET /api/tenant/info` - Get tenant information including admin status
- `GET /api/tenant/storage` - Get storage usage for authenticated tenant
- `GET /api/tenant/storage/can-store` - Check if tenant can store a file of given size

### Subtenant Operations
- `GET /api/tenant/subtenants` - List all subtenants for the authenticated tenant
- `POST /api/tenant/subtenants` - Create a new subtenant
- `POST /api/tenant/subtenants/{parentSubtenantId}/subtenants` - Create a new subtenant under a specific subtenant (hierarchical folder creation)
- `GET /api/tenant/subtenants/{subtenantId}` - Get specific subtenant information
- `GET /api/tenant/subtenants/{parentSubtenantId}/subtenants` - List all subtenants under a specific subtenant (hierarchical folder browsing)
- `PUT /api/tenant/subtenants/{subtenantId}/storage-limit` - Update subtenant storage limit
- `DELETE /api/tenant/subtenants/{subtenantId}` - Delete a subtenant

### Parent Access to Subtenant Files
ByteShelf supports hierarchical access where parent tenants can access files from their subtenants:

- **File Listing**: Parent tenants can list files from any of their subtenants using `/api/files/{targetTenantId}`
- **File Download**: Parent tenants can download files from subtenants using `/api/files/{targetTenantId}/{fileId}/download`
- **File Upload**: Parent tenants can upload files to subtenants using `/api/files/{targetTenantId}/metadata` and `/api/chunks/{targetTenantId}/{chunkId}`
- **File Deletion**: Parent tenants can delete files from subtenants using `/api/files/{targetTenantId}/{fileId}`
- **Access Control**: The system validates that the authenticated tenant has access to the target tenant before allowing any operations

### Hierarchical Folder Creation
ByteShelf supports true hierarchical folder creation through nested subtenants:

- **Unlimited Nesting**: Create subtenants under subtenants to any depth (up to 10 levels by default)
- **Folder-like Structure**: Each subtenant acts as a folder in the hierarchy
- **Parent Access**: Parent tenants can create subtenants under any of their descendants
- **Automatic API Keys**: Each subtenant gets a unique API key for secure access
- **Shared Quotas**: Storage quotas are inherited and shared through the hierarchy

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
- Subtenant API keys are valid for their own operations and descendant subtenants

### Quota Enforcement
- Storage limits are enforced per tenant
- File uploads are rejected if they would exceed the tenant's quota
- Admin tenants can have unlimited storage (when StorageLimitBytes is 0)
- Subtenants are limited by both their own quota and their parent's quota

## 📊 Shared Storage Behavior

### Hierarchical Quota Management
ByteShelf supports shared storage quotas between parent tenants and their subtenants:

- **Parent Quota**: The total storage limit for a parent tenant
- **Subtenant Quota**: Individual storage limits for each subtenant
- **Shared Enforcement**: Subtenants cannot exceed either their own quota or their parent's quota
- **Recursive Calculation**: Storage usage is calculated recursively through the tenant hierarchy

### Example Scenarios

**Scenario 1: Parent with 1GB limit, Subtenant with 500MB limit**
```json
{
  "parent": {
    "StorageLimitBytes": 1073741824,
    "SubTenants": {
      "subtenant": {
        "StorageLimitBytes": 536870912
      }
    }
  }
}
```
- Subtenant can use up to 500MB (their own limit)
- Parent can use up to 1GB total (including subtenant usage)

**Scenario 2: Parent with 1GB limit, Subtenant with unlimited**
```json
{
  "parent": {
    "StorageLimitBytes": 1073741824,
    "SubTenants": {
      "subtenant": {
        "StorageLimitBytes": 0
      }
    }
  }
}
```
- Subtenant can use up to 1GB (parent's limit)
- Parent can use up to 1GB total

### Best Practices
- Set parent quotas to accommodate expected subtenant usage
- Monitor shared storage usage through admin endpoints
- Use subtenant quotas to enforce fair usage policies
- Consider storage patterns when designing tenant hierarchies

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