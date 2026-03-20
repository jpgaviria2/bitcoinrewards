# claim-lnurl Production Fix — Task Brief

## Problem
When a user scans an LNURL-withdraw QR and claims it via `POST /plugins/bitcoin-rewards/wallet/{walletId}/claim-lnurl`, the endpoint:
1. Creates a Lightning invoice on the store's LN node
2. Sends it to the LNURL-withdraw callback
3. Polls for 15 seconds waiting for payment
4. If paid within 15s → credits CadBalance via `CreditCadAsync` ✅
5. If NOT paid within 15s → returns `pending: true` with **no balance credit** ❌

The payment IS arriving (Lightning invoice gets paid), but after the 15-second polling window. The wallet DB never gets credited.

## Required Changes

### 1. New Entity: `PendingLnurlClaim` (Data/)
Track pending LNURL claims that need payment monitoring:
```csharp
public class PendingLnurlClaim
{
    public Guid Id { get; set; }
    public Guid CustomerWalletId { get; set; }
    public string StoreId { get; set; }
    public string LightningInvoiceId { get; set; }  // LN invoice ID for polling
    public string Bolt11 { get; set; }               // Full BOLT11 for reference
    public long ExpectedSats { get; set; }
    public string? K1Prefix { get; set; }            // For reference logging
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }          // Invoice expiry (10 min from creation)
    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }
}
```

### 2. DB Migration: `20260223000000_AddPendingLnurlClaims.cs`
- Add `PendingLnurlClaims` table
- Add DbSet to `BitcoinRewardsPluginDbContext`
- Index on `IsCompleted` + `ExpiresAt` for efficient polling

### 3. New HostedService: `LnurlClaimWatcherService` (HostedServices/)
Background service that polls pending claims:
- Runs every 5 seconds
- Queries `PendingLnurlClaims` where `IsCompleted == false && ExpiresAt > UtcNow`
- For each: gets Lightning client for the store, checks invoice status
- If paid: calls `CustomerWalletService.CreditCadAsync()`, marks `IsCompleted = true`
- If expired (ExpiresAt < UtcNow): marks `IsFailed = true`
- Register as singleton + hosted service in `BitcoinRewardsPlugin.Execute()`
- Needs: `BitcoinRewardsPluginDbContextFactory`, `CustomerWalletService`, `ExchangeRateService`, `StoreRepository`, `PaymentMethodHandlerDictionary`, `LightningClientFactoryService`, `IOptions<LightningNetworkOptions>`, `BTCPayNetworkProvider`

### 4. Update `WalletApiController.ClaimLnurl()`
When the 15-second poll doesn't find payment:
- Instead of returning `pending: true` with no follow-up, **insert a `PendingLnurlClaim` record**
- The background service will pick it up and credit when payment arrives
- Keep the 15-second inline poll as fast-path (most payments arrive quickly)
- When payment IS found inline, still credit immediately (no change to happy path)

### 5. Build & Deploy to Dev Server
- Build with: `cd /home/ln/.openclaw/workspace/btcpay-research/bitcoinrewards && /home/ln/.dotnet/dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards -c Release`
- Pack: `/home/ln/.dotnet/dotnet run --project submodules/BTCPayServer.PluginPacker -- --output /tmp/plugin-out Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll $(pwd)`
- Copy to BTCPay Docker volume: `docker cp /tmp/plugin-out/*.btcpay btcpay-dev-btcpayserver-1:/root/.btcpayserver/Plugins/`
- Restart: `cd /home/ln/.openclaw/workspace/btcpay-dev && docker compose restart btcpayserver`
- Verify: `curl -s http://localhost:49392/plugins/bitcoin-rewards/wallet/9cc0a5bb-a59d-4ad9-8c01-24882b84c5f4/balance` (with auth)

## Key Files to Modify
- `Data/PendingLnurlClaim.cs` — **NEW**
- `Data/BitcoinRewardsPluginDbContext.cs` — add DbSet + OnModelCreating
- `Data/Migrations/20260223000000_AddPendingLnurlClaims.cs` — **NEW**
- `HostedServices/LnurlClaimWatcherService.cs` — **NEW**
- `Controllers/WalletApiController.cs` — update ClaimLnurl method
- `BitcoinRewardsPlugin.cs` — register new hosted service

## Environment
- .NET SDK: `/home/ln/.dotnet` (8.0.418)
- Plugin source: `/home/ln/.openclaw/workspace/btcpay-research/bitcoinrewards/Plugins/BTCPayServer.Plugins.BitcoinRewards/`
- BTCPay Docker: `/home/ln/.openclaw/workspace/btcpay-dev/docker-compose.yml`
- Dev server: `http://localhost:49392` (btcpay.anmore.me)
- Test wallet: `9cc0a5bb-a59d-4ad9-8c01-24882b84c5f4`

## DO NOT
- Leave any TODOs — implement everything fully
- Change any existing endpoint signatures or break backward compatibility
- Modify the happy path (inline 15s poll + immediate credit still works)
