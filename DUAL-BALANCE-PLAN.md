# Dual-Balance Implementation Plan
## Bitcoin Rewards Plugin — CAD + Sats per Bolt Card

### Overview
Extend the existing `bitcoinrewards` BTCPay plugin to give each bolt card holder **two independent balances**:
1. **CAD Balance** — stable, locked at exchange rate when earned. Like a gift card.
2. **Sats Balance** — volatile, tracks BTC price. The existing pull payment balance.

No stablecoins, no external services. The CAD balance is just a number in the database.

---

## Architecture

### How It Works Today
```
Customer pays at Square POS
  → Webhook fires → Plugin calculates reward in sats
  → Creates pull payment (or tops up bolt card's existing one)
  → Customer taps bolt card → reward added to pull payment limit
  → Customer can withdraw sats via LNURL-withdraw
```

### How It Will Work
```
Customer pays at Square POS
  → Webhook fires → Plugin calculates reward in sats
  → If auto-convert ON:
      Lock CAD value at current BTC/CAD rate
      Add to card's CadBalance (stored as cents, never changes)
  → If auto-convert OFF:
      Top up pull payment limit as before (sats balance)
  → Customer taps bolt card → reward collected to chosen balance
  → PWA shows both balances independently
  → Customer can swap between them at current rate
  → Withdraw sats via LNURL-withdraw (from sats balance)
  → Spend CAD in-store (shop debits CAD balance at POS)
```

---

## Database Changes

### New Entity: `CustomerWallet`
Replaces the simple `BoltCardLink` tracking. One per bolt card.

```csharp
public class CustomerWallet
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required][MaxLength(50)]
    public string StoreId { get; set; } = string.Empty;

    // Link to bolt card
    [Required][MaxLength(100)]
    public string PullPaymentId { get; set; } = string.Empty;  // sats balance lives here

    [MaxLength(50)]
    public string? CardUid { get; set; }

    [MaxLength(100)]
    public string? BoltcardId { get; set; }

    // ── Dual Balances ──

    /// <summary>
    /// Stable CAD balance in cents. Never fluctuates once credited.
    /// Example: 150 = CA$1.50
    /// </summary>
    [Required]
    public long CadBalanceCents { get; set; } = 0;

    /// <summary>
    /// Whether incoming rewards auto-convert to CAD at current rate.
    /// When false, rewards go to sats (pull payment limit).
    /// </summary>
    public bool AutoConvertToCad { get; set; } = true;

    // ── Tracking ──
    public long TotalRewardedSatoshis { get; set; } = 0;
    public long TotalRewardedCadCents { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRewardedAt { get; set; }
}
```

### New Entity: `WalletTransaction`
Audit log of every balance change.

```csharp
public enum WalletTransactionType
{
    RewardEarned = 0,      // Reward credited (sats or CAD)
    SwapToCad = 1,         // User swapped sats → CAD
    SwapToSats = 2,        // User swapped CAD → sats
    CadSpent = 3,          // CAD redeemed in-store
    SatsWithdrawn = 4,     // Sats withdrawn via LNURL
    ManualAdjust = 5       // Admin adjustment
}

public class WalletTransaction
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CustomerWalletId { get; set; }

    [Required]
    public WalletTransactionType Type { get; set; }

    /// <summary>Amount in sats (positive = credit, negative = debit)</summary>
    public long SatsAmount { get; set; } = 0;

    /// <summary>Amount in CAD cents (positive = credit, negative = debit)</summary>
    public long CadCentsAmount { get; set; } = 0;

    /// <summary>BTC/CAD exchange rate at time of transaction (sats per CAD)</summary>
    public decimal ExchangeRate { get; set; }

    /// <summary>Reference (reward ID, swap ID, invoice ID, etc.)</summary>
    [MaxLength(255)]
    public string? Reference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### Migration
- New migration: `20260222000000_AddDualBalance.cs`
- Migrate existing `BoltCardLink` rows → `CustomerWallet` (copy fields, set CadBalanceCents = 0)
- Keep `BoltCardLink` table for backward compat or drop it

---

## New API Endpoints (Greenfield-style)

All endpoints under `/plugins/bitcoin-rewards/{storeId}/wallet/`

### Public (authenticated by bolt card NFC tap)
```
POST /plugins/bitcoin-rewards/wallet/tap
  Body: { p, c }
  Response: { walletId, satsBalance, cadBalanceCents, autoConvert }
  → Identifies card, returns both balances
```

### Public (authenticated by wallet token — see below)
```
GET  /plugins/bitcoin-rewards/wallet/{walletId}/balance
  → { satsBalance, cadBalanceCents, autoConvert, totalRewardedSats, totalRewardedCadCents }

POST /plugins/bitcoin-rewards/wallet/{walletId}/swap
  Body: { direction: "to_cad" | "to_sats", amount: 1000 }
  → amount is sats (if to_cad) or cad_cents (if to_sats)
  → Locks exchange rate at moment of swap
  → Returns new balances

POST /plugins/bitcoin-rewards/wallet/{walletId}/settings
  Body: { autoConvert: true/false }
  → Toggle auto-convert preference

GET  /plugins/bitcoin-rewards/wallet/{walletId}/history
  → List of WalletTransaction records
```

### Admin (BTCPay auth)
```
GET  /plugins/bitcoin-rewards/{storeId}/wallets
  → List all customer wallets with balances

POST /plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/adjust
  Body: { satsAmount, cadCentsAmount, reason }
  → Manual admin adjustment

POST /plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/spend-cad
  Body: { amountCents }
  → Debit CAD balance (called from POS integration or admin)
```

### Wallet Token Auth
For the PWA to query balances without NFC:
- On first NFC tap, generate a random bearer token, store hashed in `CustomerWallet.ApiTokenHash`
- Return token to PWA, stored in localStorage
- PWA uses token for subsequent API calls
- Token can be regenerated on next NFC tap

---

## Exchange Rate Service

New service: `ExchangeRateService`

```csharp
public class ExchangeRateService
{
    /// <summary>
    /// Get current BTC/CAD rate. Uses BTCPay's built-in rate provider.
    /// Returns sats per 1 CAD (e.g., if 1 BTC = $140,000 CAD → ~714 sats/CAD)
    /// </summary>
    public async Task<decimal> GetSatsPerCadAsync();

    /// <summary>
    /// Convert sats to CAD cents at current rate.
    /// </summary>
    public async Task<long> SatsToCadCentsAsync(long sats);

    /// <summary>
    /// Convert CAD cents to sats at current rate.
    /// </summary>
    public async Task<long> CadCentsToSatsAsync(long cadCents);
}
```

Uses BTCPay's built-in `IRateProvider` — no external API calls needed. BTCPay already fetches exchange rates for invoice generation.

---

## Modified Reward Flow

### In `BitcoinRewardsService.ProcessRewardAsync()`

```
Current:
  1. Calculate reward sats
  2. Create pull payment or top up existing

New:
  1. Calculate reward sats
  2. Find CustomerWallet by bolt card / pull payment
  3. If wallet.AutoConvertToCad:
     a. Get current BTC/CAD rate
     b. Convert sats → CAD cents
     c. Add to wallet.CadBalanceCents
     d. Log WalletTransaction (type: RewardEarned, cadCentsAmount)
  4. Else:
     a. Top up pull payment limit (existing behavior)
     b. Log WalletTransaction (type: RewardEarned, satsAmount)
```

---

## PWA Changes (wallet.trailscoffee.com)

### Repurpose existing PWA
- Remove all LNbits code
- Remove all Blink code
- Point to BTCPay API endpoints

### Screens
1. **Home** — Two balance cards (CAD + Sats), auto-convert toggle
2. **Tap to Connect** — NFC tap links PWA to bolt card, stores wallet token
3. **Swap** — Slide to swap between CAD ↔ Sats, shows current rate
4. **History** — Transaction log from WalletTransaction API
5. **Withdraw** — Generate LNURL-withdraw QR from sats balance

### Auth Flow
1. User opens PWA → sees "Tap your card to connect"
2. Phone NFC reads bolt card URL → redirects to BTCPay tap endpoint
3. BTCPay returns wallet token + balances
4. PWA stores token in localStorage
5. Subsequent visits use token to fetch balances

---

## Store Settings Changes

Add to `BitcoinRewardsStoreSettings`:
```csharp
/// <summary>Default auto-convert setting for new wallets</summary>
public bool DefaultAutoConvertToCad { get; set; } = true;

/// <summary>CAD spending enabled (allows POS debit of CAD balance)</summary>
public bool CadSpendingEnabled { get; set; } = false;

/// <summary>Allow customers to swap between CAD and sats</summary>
public bool SwapEnabled { get; set; } = true;
```

---

## Deployment Plan

### Phase 1: Database + API (Week 1)
1. Add `CustomerWallet` and `WalletTransaction` entities
2. Create migration
3. Add `ExchangeRateService` using BTCPay's rate provider
4. Add wallet API controller with balance, swap, settings, history endpoints
5. Modify reward processing to check auto-convert flag
6. Add wallet token auth
7. **Test on dev server** (btcpay.anmore.me)

### Phase 2: PWA (Week 1-2)
1. Strip LNbits/Blink code from wallet PWA
2. Build new screens pointing to BTCPay API
3. Implement NFC → wallet token flow
4. Two balance cards with live rates
5. Swap UI
6. Deploy to wallet.trailscoffee.com

### Phase 3: POS Integration (Week 2-3)
1. Admin page to view/manage customer wallets
2. CAD spend endpoint for POS (Square webhook or manual)
3. "Pay with rewards" flow at register
4. Receipt shows reward earned + balance update

### Phase 4: Production Deploy (Week 3)
1. Install plugin on production BTCPay (209.53.47.45)
2. Migrate existing bolt card links → CustomerWallet
3. Test with real bolt cards
4. Go live

---

## Files to Modify/Create

### New Files
```
Data/CustomerWallet.cs
Data/WalletTransaction.cs
Data/Migrations/20260222000000_AddDualBalance.cs
Services/ExchangeRateService.cs
Services/CustomerWalletService.cs
Controllers/WalletApiController.cs
ViewModels/WalletBalanceViewModel.cs
```

### Modified Files
```
Data/BitcoinRewardsPluginDbContext.cs  — Add DbSet<CustomerWallet>, DbSet<WalletTransaction>
Services/BitcoinRewardsService.cs     — Add auto-convert logic in ProcessRewardAsync
Services/BoltCardRewardService.cs     — Use CustomerWallet instead of BoltCardLink
BitcoinRewardsStoreSettings.cs        — Add new settings
BitcoinRewardsPlugin.cs               — Register new services
Controllers/BoltCardRewardsController.cs — Return wallet token on tap
```

---

## Key Design Decisions

1. **CAD balance is cents in the DB** — `long`, not `decimal`. No floating point issues. CA$1.50 = 150.
2. **Exchange rate locked at earn/swap time** — Once CAD is credited, the number never changes.
3. **Sats balance stays in pull payment** — Existing LNURL-withdraw infrastructure works unchanged.
4. **Wallet token for PWA auth** — Simple bearer token, no username/password.
5. **BTCPay's rate provider for exchange rates** — No external API dependency.
6. **Audit trail** — Every balance change logged in `WalletTransaction`.

---

## Agent Configuration

Repurpose the **Trails Coffee Wallet Engineer** sub-agent:
- **New purpose**: Bitcoin Rewards Plugin development (C# / .NET)
- **Workspace**: `/home/ln/.openclaw/workspace/btcpay-research/bitcoinrewards/`
- **Dev server**: `btcpay.anmore.me` (Docker on P50, port 49392)
- **Build**: `dotnet build` in plugin directory
- **Deploy to dev**: Copy .btcpay to Docker volume, restart container
- **Test**: Greenfield API calls against dev server
- **PWA work**: `/home/ln/.openclaw/workspace/trails-wallet/` (vanilla JS, same as before)
