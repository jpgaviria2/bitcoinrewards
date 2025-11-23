#!/bin/bash
# Docker build script that replicates Plugin Builder environment exactly

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
IMAGE_NAME="btcpayserver-plugin-bitcoinrewards-build"
CONTAINER_NAME="bitcoinrewards-build-$$"

echo "=== Docker Build Environment ==="
echo "Building Docker image..."

# Build Docker image
cd "$SCRIPT_DIR"
docker build -t "$IMAGE_NAME" -f Dockerfile.build .

if [ $? -ne 0 ]; then
    echo "ERROR: Docker image build failed"
    exit 1
fi

echo "Docker image built successfully"
echo ""
echo "To build the plugin in Docker, run:"
echo "  docker run --rm -v \"$ROOT_DIR:/build\" -w /build $IMAGE_NAME /build/scripts/build-local.sh"
echo ""
echo "Or enter interactive shell:"
echo "  docker run --rm -it -v \"$ROOT_DIR:/build\" -w /build $IMAGE_NAME /bin/bash"

