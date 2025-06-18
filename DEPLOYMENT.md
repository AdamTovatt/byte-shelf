# ByteShelf Linux Deployment Guide

This guide explains how to deploy ByteShelf on a Linux server using systemd.

## Prerequisites

- Linux server (Ubuntu, Debian, CentOS, etc.)
- .NET 8.0 Runtime (if not using self-contained deployment)
- Root or sudo access

## Building for Linux

### Option 1: Using the Build Script

```bash
# Make the script executable
chmod +x build-linux.sh

# Run the build script
./build-linux.sh
```

### Option 2: Manual Build

```bash
# For x64 systems
dotnet publish ByteShelf/ByteShelf.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -o linux-deploy

# For ARM systems (Raspberry Pi, etc.)
dotnet publish ByteShelf/ByteShelf.csproj \
    -c Release \
    -r linux-arm \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -o linux-deploy
```

## Deployment Steps

### 1. Create Application Directory

```bash
sudo mkdir -p /opt/byteshelf
```

### 2. Copy Application Files

```bash
# Copy the built application
sudo cp -r linux-deploy/* /opt/byteshelf/

# Make the executable executable
sudo chmod +x /opt/byteshelf/ByteShelf
```

### 3. Create Service User

```bash
# Create a dedicated user for the service
sudo useradd -r -s /bin/false byteshelf

# Set ownership of the application directory
sudo chown -R byteshelf:byteshelf /opt/byteshelf
```

### 4. Create Storage Directory

```bash
# Create storage directory
sudo mkdir -p /var/byteshelf/storage

# Set ownership
sudo chown -R byteshelf:byteshelf /var/byteshelf
```

### 5. Install Systemd Service

```bash
# Copy the service file
sudo cp byteshelf.service /etc/systemd/system/

# Reload systemd configuration
sudo systemctl daemon-reload
```

### 6. Enable and Start Service

```bash
# Enable the service to start on boot
sudo systemctl enable byteshelf

# Start the service
sudo systemctl start byteshelf

# Check status
sudo systemctl status byteshelf
```

## Configuration

### Environment Variables

The service can be configured using environment variables in the service file:

```ini
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000
Environment=StoragePath=/var/byteshelf/storage
Environment=ChunkConfiguration__ChunkSizeBytes=1048576
Environment=Authentication__ApiKey=your-secure-production-api-key
Environment=Authentication__RequireAuthentication=true
```

### Configuration File

You can also create an `appsettings.Production.json` file in `/opt/byteshelf/`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "StoragePath": "/var/byteshelf/storage",
  "Authentication": {
    "ApiKey": "your-secure-production-api-key",
    "RequireAuthentication": true
  },
  "ChunkConfiguration": {
    "ChunkSizeBytes": 1048576
  }
}
```

### Security Configuration

For production deployments, it's recommended to:

1. **Generate a Strong API Key**:
   ```bash
   # Generate a secure random API key
   openssl rand -base64 32
   ```

2. **Set API Key via Environment Variable** (preferred for security):
   ```bash
   # Add to the service file or export in shell
   export Authentication__ApiKey="your-generated-api-key"
   ```

3. **Secure the Configuration File**:
   ```bash
   # Set restrictive permissions on config file
   sudo chmod 600 /opt/byteshelf/appsettings.Production.json
   sudo chown byteshelf:byteshelf /opt/byteshelf/appsettings.Production.json
   ```

## Service Management

### Start/Stop/Restart

```bash
# Start the service
sudo systemctl start byteshelf

# Stop the service
sudo systemctl stop byteshelf

# Restart the service
sudo systemctl restart byteshelf

# Check status
sudo systemctl status byteshelf
```

### View Logs

```bash
# View service logs
sudo journalctl -u byteshelf -f

# View recent logs
sudo journalctl -u byteshelf --since "1 hour ago"
```

### Enable/Disable Auto-start

```bash
# Enable auto-start on boot
sudo systemctl enable byteshelf

# Disable auto-start on boot
sudo systemctl disable byteshelf
```

## Reverse Proxy Setup (Optional)

If you want to expose the API through a reverse proxy like nginx:

### Nginx Configuration

Create `/etc/nginx/sites-available/byteshelf`:

```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}
```

Enable the site:

```bash
sudo ln -s /etc/nginx/sites-available/byteshelf /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

## Troubleshooting

### Check Service Status

```bash
sudo systemctl status byteshelf
```

### View Detailed Logs

```bash
sudo journalctl -u byteshelf -f
```

### Check File Permissions

```bash
# Check application directory permissions
ls -la /opt/byteshelf/

# Check storage directory permissions
ls -la /var/byteshelf/
```

### Test API Endpoint

```bash
# Test if the API is responding
curl http://localhost:5000/api/config/chunk-size
```

### Common Issues

1. **Permission Denied**: Ensure the `byteshelf` user owns the application and storage directories
2. **Port Already in Use**: Change the port in the service file or stop conflicting services
3. **Storage Directory Not Found**: Ensure `/var/byteshelf/storage` exists and has correct permissions

## Security Considerations

1. **Firewall**: Configure firewall to only allow necessary ports
2. **HTTPS**: Use a reverse proxy with SSL/TLS for production
3. **Authentication**: API key authentication is enabled by default - ensure you set a strong API key
4. **File Permissions**: Ensure proper file permissions for the service user
5. **API Key Security**: 
   - Use environment variables for API keys in production
   - Rotate API keys regularly
   - Monitor access logs for suspicious activity
   - Never commit API keys to version control

## Performance Tuning

1. **Chunk Size**: Adjust `ChunkConfiguration__ChunkSizeBytes` based on your use case
2. **Storage**: Use fast storage (SSD) for better performance
3. **Memory**: Monitor memory usage and adjust as needed
4. **Concurrency**: The service handles multiple concurrent requests by default 