#!/bin/bash

# ByteShelf Linux Build Script
# This script builds a self-contained Linux deployment of ByteShelf

set -e

echo "Building ByteShelf for Linux..."

# Configuration
PROJECT_NAME="ByteShelf"
OUTPUT_DIR="linux-deploy"
RUNTIME="linux-x64"  # Change to linux-arm for ARM devices like Raspberry Pi

# Clean previous build
echo "Cleaning previous build..."
rm -rf $OUTPUT_DIR

# Build the project
echo "Building ByteShelf..."
dotnet publish ByteShelf/$PROJECT_NAME.csproj \
    -c Release \
    -r $RUNTIME \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -o $OUTPUT_DIR

echo "Build completed successfully!"
echo "Deployment files are in: $OUTPUT_DIR"
echo ""
echo "To deploy on Linux server:"
echo "1. Copy the contents of $OUTPUT_DIR to /opt/byteshelf/"
echo "2. Copy byteshelf.service to /etc/systemd/system/"
echo "3. Create user: sudo useradd -r -s /bin/false byteshelf"
echo "4. Create storage directory: sudo mkdir -p /var/byteshelf/storage"
echo "5. Set permissions: sudo chown -R byteshelf:byteshelf /var/byteshelf"
echo "6. Enable and start service: sudo systemctl enable byteshelf && sudo systemctl start byteshelf" 