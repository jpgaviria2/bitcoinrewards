#!/bin/bash
# Bash script to build BTCPay Server and get DLLs

set -e

BTCPAYSERVER_PATH="${BTCPAYSERVER_PATH:-}"
BTCPAYSERVER_REPO="${BTCPAYSERVER_REPO:-https://github.com/btcpayserver/btcpayserver.git}"
BTCPAYSERVER_BRANCH="${BTCPAYSERVER_BRANCH:-master}"
CLEAN="${CLEAN:-false}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BTCPAYSERVER_DIR="${BTCPAYSERVER_PATH:-$ROOT_DIR/../btcpayserver}"

echo "=== BTCPay Server Build ==="

# Clone if needed
if [ ! -d "$BTCPAYSERVER_DIR" ]; then
    echo "Cloning BTCPay Server..."
    PARENT_DIR="$(dirname "$BTCPAYSERVER_DIR")"
    mkdir -p "$PARENT_DIR"
    cd "$PARENT_DIR"
    git clone -b "$BTCPAYSERVER_BRANCH" "$BTCPAYSERVER_REPO" "$(basename "$BTCPAYSERVER_DIR")"
fi

# Clean if requested
if [ "$CLEAN" = "true" ]; then
    echo "Cleaning BTCPay Server..."
    cd "$BTCPAYSERVER_DIR"
    dotnet clean BTCPayServer/BTCPayServer.csproj -c Release
fi

# Build
echo "Building BTCPay Server..."
cd "$BTCPAYSERVER_DIR"
dotnet build BTCPayServer/BTCPayServer.csproj -c Release
if [ $? -ne 0 ]; then
    echo "ERROR: BTCPay Server build failed"
    exit 1
fi

# Verify DLLs
DLL_PATH="$BTCPAYSERVER_DIR/BTCPayServer/bin/Release/net8.0"
REQUIRED_DLLS=(
    "BTCPayServer.dll"
    "BTCPayServer.Abstractions.dll"
    "BTCPayServer.Data.dll"
    "BTCPayServer.Client.dll"
    "BTCPayServer.Common.dll"
    "BTCPayServer.Rating.dll"
)

echo ""
echo "Verifying DLLs..."
ALL_FOUND=true
for dll in "${REQUIRED_DLLS[@]}"; do
    if [ -f "$DLL_PATH/$dll" ]; then
        echo "  ✓ $dll"
    else
        echo "  ✗ $dll (NOT FOUND)"
        ALL_FOUND=false
    fi
done

if [ "$ALL_FOUND" = true ]; then
    echo ""
    echo "✓ All required DLLs found at: $DLL_PATH"
    echo "Set environment variable: export BTCPayServerPath=\"$DLL_PATH\""
    echo "$DLL_PATH"
else
    echo ""
    echo "✗ Some DLLs are missing"
    exit 1
fi

