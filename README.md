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
│                 │ ◄──────────────► │                 │
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

3. **Configuration**

   The server can be configured through `appsettings.json`:

   ```json
   {
     "StoragePath": "byte-shelf-storage",
     "ChunkConfiguration": {
       "ChunkSizeBytes": 1048576
     }
   }
   ```

   - **StoragePath**: Directory where files will be stored (default: "byte-shelf-storage")
   - **ChunkSizeBytes**: Size of each chunk in bytes (default: 1MB)

4. **Environment Variables**

   You can also override settings using environment variables:
   ```bash
   set StoragePath=C:\MyStorage
   set ChunkConfiguration__ChunkSizeBytes=2097152
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

#### Usage

1. **Setup**
   ```csharp
   using HttpClient httpClient = new HttpClient();
   httpClient.BaseAddress = new Uri("https://localhost:7001");
   
   IShelfFileProvider provider = new HttpShelfFileProvider(httpClient);
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

## Configuration Examples

### Production Server Configuration

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

### Development Configuration

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "StoragePath": "byte-shelf-storage",
  "ChunkConfiguration": {
    "ChunkSizeBytes": 1048576
  }
}
```

## Performance Considerations

- **Chunk Size**: Larger chunks reduce HTTP overhead but increase memory usage
- **Concurrent Requests**: The server handles multiple concurrent file operations
- **Streaming**: Files are streamed to avoid loading entire files into memory
- **Storage**: Consider using fast storage (SSD) for better performance

## Security Notes

- The current implementation does not include authentication or authorization
- Consider adding HTTPS in production environments
- Implement proper access controls for production use
- Validate file types and sizes as needed for your use case

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass
6. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details. 