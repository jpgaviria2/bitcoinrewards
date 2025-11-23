# PowerShell script to build Bitcoin Rewards plugin locally
# Replicates Plugin Builder environment for troubleshooting

param(
    [string]$BTCPayServerPath = "",
    [string]$BTCPayServerRepo = "https://github.com/btcpayserver/btcpayserver.git",
    [string]$BTCPayServerBranch = "master",
    [switch]$SkipBTCPayServerBuild = $false,
    [switch]$Clean = $false
)

$ErrorActionPreference = "Stop"
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootPath = Split-Path -Parent $scriptPath
$btcpayserverPath = if ($BTCPayServerPath) { $BTCPayServerPath } else { Join-Path $rootPath "..\btcpayserver" }

Write-Host "=== Bitcoin Rewards Plugin Local Build ===" -ForegroundColor Cyan
Write-Host "Root Path: $rootPath" -ForegroundColor Gray
Write-Host "BTCPay Server Path: $btcpayserverPath" -ForegroundColor Gray

# Step 1: Build BTCPay Server if needed
if (-not $SkipBTCPayServerBuild) {
    Write-Host "`n=== Step 1: Building BTCPay Server ===" -ForegroundColor Yellow
    
    if (-not (Test-Path $btcpayserverPath)) {
        Write-Host "BTCPay Server not found. Cloning..." -ForegroundColor Yellow
        $parentPath = Split-Path -Parent $btcpayserverPath
        New-Item -ItemType Directory -Force -Path $parentPath | Out-Null
        Push-Location $parentPath
        git clone -b $BTCPayServerBranch $BTCPayServerRepo (Split-Path -Leaf $btcpayserverPath)
        Pop-Location
    }
    
    $btcpayServerProject = Join-Path $btcpayserverPath "BTCPayServer\BTCPayServer.csproj"
    if (-not (Test-Path $btcpayServerProject)) {
        Write-Host "ERROR: BTCPay Server project not found at $btcpayServerProject" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Building BTCPay Server..." -ForegroundColor Yellow
    Push-Location $btcpayserverPath
    dotnet build BTCPayServer\BTCPayServer.csproj -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: BTCPay Server build failed" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    Pop-Location
    
    $dllPath = Join-Path $btcpayserverPath "BTCPayServer\bin\Release\net8.0"
    if (-not (Test-Path (Join-Path $dllPath "BTCPayServer.dll"))) {
        Write-Host "ERROR: BTCPay Server DLLs not found at $dllPath" -ForegroundColor Red
        exit 1
    }
    
    $env:BTCPayServerPath = $dllPath
    Write-Host "BTCPay Server DLLs found at: $dllPath" -ForegroundColor Green
} else {
    if ($BTCPayServerPath) {
        $env:BTCPayServerPath = $BTCPayServerPath
        Write-Host "Using provided BTCPayServerPath: $BTCPayServerPath" -ForegroundColor Green
    } else {
        Write-Host "ERROR: BTCPayServerPath not set and SkipBTCPayServerBuild is true" -ForegroundColor Red
        exit 1
    }
}

# Step 2: Clean if requested
if ($Clean) {
    Write-Host "`n=== Cleaning Plugin Build ===" -ForegroundColor Yellow
    Push-Location $rootPath
    dotnet clean BTCPayServer.Plugins.BitcoinRewards.csproj -c Release
    Pop-Location
}

# Step 3: Build Plugin
Write-Host "`n=== Step 2: Building Plugin ===" -ForegroundColor Yellow
Push-Location $rootPath

Write-Host "BTCPayServerPath environment variable: $env:BTCPayServerPath" -ForegroundColor Gray

$buildOutput = dotnet build BTCPayServer.Plugins.BitcoinRewards.csproj -c Release 2>&1
$buildExitCode = $LASTEXITCODE

Write-Host $buildOutput

if ($buildExitCode -eq 0) {
    Write-Host "`n=== Build SUCCESS ===" -ForegroundColor Green
    $btcpayFile = Join-Path $rootPath "bin\Release\net8.0\BTCPayServer.Plugins.BitcoinRewards.btcpay"
    if (Test-Path $btcpayFile) {
        Write-Host "Plugin built successfully: $btcpayFile" -ForegroundColor Green
        $fileInfo = Get-Item $btcpayFile
        Write-Host "File size: $($fileInfo.Length) bytes" -ForegroundColor Gray
        Write-Host "Last modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
    } else {
        Write-Host "WARNING: .btcpay file not found at expected location" -ForegroundColor Yellow
    }
    Pop-Location
    exit 0
} else {
    Write-Host "`n=== Build FAILED ===" -ForegroundColor Red
    Write-Host "Exit code: $buildExitCode" -ForegroundColor Red
    Pop-Location
    exit $buildExitCode
}

