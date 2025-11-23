# PowerShell script to build BTCPay Server and get DLLs

param(
    [string]$BTCPayServerPath = "",
    [string]$BTCPayServerRepo = "https://github.com/btcpayserver/btcpayserver.git",
    [string]$BTCPayServerBranch = "master",
    [switch]$Clean = $false
)

$ErrorActionPreference = "Stop"
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootPath = Split-Path -Parent $scriptPath
$btcpayserverPath = if ($BTCPayServerPath) { $BTCPayServerPath } else { Join-Path $rootPath "..\btcpayserver" }

Write-Host "=== BTCPay Server Build ===" -ForegroundColor Cyan

# Clone if needed
if (-not (Test-Path $btcpayserverPath)) {
    Write-Host "Cloning BTCPay Server..." -ForegroundColor Yellow
    $parentPath = Split-Path -Parent $btcpayserverPath
    New-Item -ItemType Directory -Force -Path $parentPath | Out-Null
    Push-Location $parentPath
    git clone -b $BTCPayServerBranch $BTCPayServerRepo (Split-Path -Leaf $btcpayserverPath)
    Pop-Location
}

# Clean if requested
if ($Clean) {
    Write-Host "Cleaning BTCPay Server..." -ForegroundColor Yellow
    Push-Location $btcpayserverPath
    dotnet clean BTCPayServer\BTCPayServer.csproj -c Release
    Pop-Location
}

# Build
Write-Host "Building BTCPay Server..." -ForegroundColor Yellow
Push-Location $btcpayserverPath
dotnet build BTCPayServer\BTCPayServer.csproj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: BTCPay Server build failed" -ForegroundColor Red
    Pop-Location
    exit 1
}
Pop-Location

# Verify DLLs
$dllPath = Join-Path $btcpayserverPath "BTCPayServer\bin\Release\net8.0"
$requiredDlls = @(
    "BTCPayServer.dll",
    "BTCPayServer.Abstractions.dll",
    "BTCPayServer.Data.dll",
    "BTCPayServer.Client.dll",
    "BTCPayServer.Common.dll",
    "BTCPayServer.Rating.dll"
)

Write-Host "`nVerifying DLLs..." -ForegroundColor Yellow
$allFound = $true
foreach ($dll in $requiredDlls) {
    $dllFile = Join-Path $dllPath $dll
    if (Test-Path $dllFile) {
        Write-Host "  ✓ $dll" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $dll (NOT FOUND)" -ForegroundColor Red
        $allFound = $false
    }
}

if ($allFound) {
    Write-Host "`n✓ All required DLLs found at: $dllPath" -ForegroundColor Green
    Write-Host "Set environment variable: `$env:BTCPayServerPath = `"$dllPath`"" -ForegroundColor Cyan
    return $dllPath
} else {
    Write-Host "`n✗ Some DLLs are missing" -ForegroundColor Red
    exit 1
}

