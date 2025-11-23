#!/bin/bash
# Bash script to build Bitcoin Rewards plugin locally
# Replicates Plugin Builder environment (Debian 12, .NET 8.0.416)

set -e

BTCPAYSERVER_PATH="${BTCPAYSERVER_PATH:-}"
BTCPAYSERVER_REPO="${BTCPAYSERVER_REPO:-https://github.com/btcpayserver/btcpayserver.git}"
BTCPAYSERVER_BRANCH="${BTCPAYSERVER_BRANCH:-master}"
SKIP_BTCPAYSERVER_BUILD="${SKIP_BTCPAYSERVER_BUILD:-false}"
CLEAN="${CLEAN:-false}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BTCPAYSERVER_DIR="${BTCPAYSERVER_PATH:-$ROOT_DIR/../btcpayserver}"

echo "=== Bitcoin Rewards Plugin Local Build ==="
echo "Root Path: $ROOT_DIR"
echo "BTCPay Server Path: $BTCPAYSERVER_DIR"

# Step 1: Build BTCPay Server if needed
if [ "$SKIP_BTCPAYSERVER_BUILD" != "true" ]; then
    echo ""
    echo "=== Step 1: Building BTCPay Server ==="
    
    if [ ! -d "$BTCPAYSERVER_DIR" ]; then
        echo "BTCPay Server not found. Cloning..."
        PARENT_DIR="$(dirname "$BTCPAYSERVER_DIR")"
        mkdir -p "$PARENT_DIR"
        cd "$PARENT_DIR"
        git clone -b "$BTCPAYSERVER_BRANCH" "$BTCPAYSERVER_REPO" "$(basename "$BTCPAYSERVER_DIR")"
    fi
    
    BTCPAYSERVER_PROJECT="$BTCPAYSERVER_DIR/BTCPayServer/BTCPayServer.csproj"
    if [ ! -f "$BTCPAYSERVER_PROJECT" ]; then
        echo "ERROR: BTCPay Server project not found at $BTCPAYSERVER_PROJECT"
        exit 1
    fi
    
    echo "Building BTCPay Server..."
    cd "$BTCPAYSERVER_DIR"
    dotnet build BTCPayServer/BTCPayServer.csproj -c Release
    if [ $? -ne 0 ]; then
        echo "ERROR: BTCPay Server build failed"
        exit 1
    fi
    
    DLL_PATH="$BTCPAYSERVER_DIR/BTCPayServer/bin/Release/net8.0"
    if [ ! -f "$DLL_PATH/BTCPayServer.dll" ]; then
        echo "ERROR: BTCPay Server DLLs not found at $DLL_PATH"
        exit 1
    fi
    
    export BTCPayServerPath="$DLL_PATH"
    echo "BTCPay Server DLLs found at: $DLL_PATH"
else
    if [ -n "$BTCPAYSERVER_PATH" ]; then
        export BTCPayServerPath="$BTCPAYSERVER_PATH"
        echo "Using provided BTCPayServerPath: $BTCPAYSERVER_PATH"
    else
        echo "ERROR: BTCPayServerPath not set and SKIP_BTCPAYSERVER_BUILD is true"
        exit 1
    fi
fi

# Step 2: Clean if requested
if [ "$CLEAN" = "true" ]; then
    echo ""
    echo "=== Cleaning Plugin Build ==="
    cd "$ROOT_DIR"
    dotnet clean BTCPayServer.Plugins.BitcoinRewards.csproj -c Release
fi

# Step 3: Build Plugin
echo ""
echo "=== Step 2: Building Plugin ==="
cd "$ROOT_DIR"

echo "BTCPayServerPath environment variable: $BTCPayServerPath"

if dotnet build BTCPayServer.Plugins.BitcoinRewards.csproj -c Release; then
    echo ""
    echo "=== Build SUCCESS ==="
    BTCPAY_FILE="$ROOT_DIR/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.btcpay"
    if [ -f "$BTCPAY_FILE" ]; then
        echo "Plugin built successfully: $BTCPAY_FILE"
        ls -lh "$BTCPAY_FILE"
    else
        echo "WARNING: .btcpay file not found at expected location"
    fi
    exit 0
else
    echo ""
    echo "=== Build FAILED ==="
    exit 1
fi

