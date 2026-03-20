# NFC Delivery of LNURL-Withdraw Rewards — Research Notes

**Date:** 2026-02-28  
**Scope:** How to write the existing LNURL-withdraw string to an NFC NDEF tag so coffee shop customers can tap-to-claim instead of scanning a QR code.

---

## 1. Current Plugin State (What Already Exists)

The plugin **already generates everything needed**:

- `BitcoinRewardsService.ProcessRewardAsync()` → creates a pull payment → produces a `ClaimLink` like `https://anmore.cash/pull-payments/{ppId}`
- `UIBitcoinRewardsController` (line ~558) → `GetLnurlBech32FromClaimLink()` converts that into an LNURL-withdraw bech32 string: builds `/BTC/lnurl/withdraw/pp/{ppId}` endpoint → encodes via `LNURL.LNURL.EncodeUri()`
- `DisplayRewardsViewModel.LnurlString` — the raw LNURL bech32 string (e.g., `lnurl1dp68gurn8ghj7...`)
- The display page (`DisplayRewards.cshtml`) already shows a QR code of the LNURL and has a Bolt Card NFC tap section

**Key insight: The LNURL-w string is already available in the display page view model as `Model.LnurlString`. No new mint API or crypto operations needed.**

---

## 2. NDEF Format for LNURL URLs

An LNURL-withdraw string for NFC should be written as an **NDEF URI Record**:

### Option A: Raw LNURL bech32 (simplest, works with all LN wallets)
- NDEF Record Type: **URI Record** (TNF=0x01, Type="U")
- URI identifier code: `0x00` (no prefix) 
- Payload: `lightning:lnurl1dp68gurn8ghj7...`
- The `lightning:` URI scheme is what LN wallets register for on both iOS and Android
- Total payload size: ~200-300 bytes for a typical LNURL string (fits in any NFC tag ≥ NTAG213)

### Option B: HTTPS URL to pull payment page (fallback)
- NDEF URI Record with the ClaimLink directly: `https://anmore.cash/pull-payments/{ppId}`
- Opens in browser → BTCPay pull payment page
- Less smooth UX but works without a Lightning wallet

### Recommended: Option A (`lightning:lnurl1...`)
- Every Lightning wallet (Phoenix, Zeus, Wallet of Satoshi, Breez, Macadamia) registers for `lightning:` URI scheme
- Customer taps phone → OS opens their default Lightning wallet → wallet decodes LNURL-w → shows "Receive X sats?" → one tap to claim
- **This is the exact same flow as scanning the QR code, just triggered by NFC instead of camera**

### NDEF Technical Details
```
NDEF Record:
  TNF: 0x01 (Well-Known)
  Type: "U" (URI)
  Payload: 0x00 + "lightning:lnurl1dp68gurn8ghj7..."
           ^--- 0x00 means no URI prefix abbreviation
```

Alternative (also valid):
```
  Payload: "lightning:LNURL1DP68GURN8GHJ7..." (uppercase LNURL is valid per spec)
```

Typical LNURL string length: ~180-250 chars. With `lightning:` prefix ≈ 200-260 bytes. NTAG215 (504 bytes usable) is more than sufficient.

---

## 3. What Writes to the NFC Tag — Architecture Options

### Option A: Web NFC from the Display Device (RECOMMENDED for v1)

The display page already runs on a tablet/kiosk at the counter. Modern Android Chrome supports the **Web NFC API** (`NDEFReader`/`NDEFWriter`).

**Flow:**
1. Invoice settles → reward created → display page shows QR + LNURL
2. Display page JavaScript uses `NDEFWriter` to write LNURL to a rewritable NFC tag sitting on the counter
3. Customer taps their phone on the tag → phone reads NDEF URI → opens Lightning wallet → claim

**Code (add to DisplayRewards.cshtml):**
```javascript
async function writeToNfcTag() {
    if (!('NDEFReader' in window)) return;
    
    const lnurl = '@Model.LnurlString';
    if (!lnurl) return;
    
    try {
        const writer = new NDEFReader();
        await writer.write({
            records: [{
                recordType: "url",
                data: "lightning:" + lnurl
            }]
        });
        console.log("LNURL written to NFC tag");
    } catch (err) {
        console.error("NFC write failed:", err);
    }
}

// Auto-write when reward appears
if ('@Model.HasReward' === 'True' && '@Model.LnurlString') {
    writeToNfcTag();
}
```

**Pros:**
- Zero hardware cost beyond what you already have (Android tablet + rewritable NFC tag)
- No plugin changes needed — just a JS snippet in the display page
- Works today

**Cons:**
- Web NFC only works on Android Chrome (not iOS Safari, not Firefox)
- Requires HTTPS
- Display device needs to be physically close to the NFC tag

**Hardware needed:**
- 1x rewritable NFC tag (NTAG215 or NTAG216), stuck to the counter near the "tap here" spot — **$0.50-$1.00 each**
- The existing display tablet (must be Android with Chrome)

### Option B: Dedicated ESP32 NFC Writer

An ESP32 with an NFC writer module (PN532 or ST25R3916) that:
1. Polls an API endpoint for the latest LNURL-w
2. Writes it to a rewritable tag on the counter
3. Or uses NFC card emulation (HCE) to present the NDEF record directly

**Hardware:**
- ESP32-S3 dev board: ~$8-15
- PN532 NFC module: ~$5-10
- Total: ~$15-25

**Flow:**
```
Plugin API ──HTTP──> ESP32 ──NFC write──> NTAG on counter
                                              ↑
                                    Customer taps phone
```

**This requires a new API endpoint** in the plugin:
```
GET /plugins/bitcoin-rewards/{storeId}/api/latest-lnurl
→ { "lnurl": "lnurl1dp68...", "rewardId": "...", "sats": 150 }
```

**Pros:**
- Works regardless of display device OS
- Can also drive an e-ink display or LED indicator
- More reliable than Web NFC

**Cons:**
- Custom firmware needed (Arduino/PlatformIO, straightforward)
- Extra hardware to maintain
- More complex setup

### Option C: Phone-as-Writer (Barista's Phone)

A simple Android app or PWA that:
1. Barista opens app after sale
2. App fetches latest LNURL from plugin API  
3. Barista holds phone near counter tag → writes LNURL
4. Customer taps the tag

**Simplest to prototype but adds a manual step — not ideal for busy coffee shop.**

### Recommendation

**Start with Option A (Web NFC).** The display page is already an Android tablet running Chrome. Add ~20 lines of JS to auto-write LNURL to a $1 NFC tag on the counter. If the display device can't do NFC writing (e.g., it's an old tablet without NFC), fall back to Option B with an ESP32.

---

## 4. Plugin Changes Needed

### For Option A (Web NFC from display page): MINIMAL CHANGES

Only the display page needs updating. No backend changes.

**File: `Views/UIBitcoinRewards/DisplayRewards.cshtml`**

Add an NFC write section after the QR code display:

```html
<!-- NFC Write Tag Section -->
<div id="nfc-write-section" style="display: none; margin-top: 20px;">
    <div style="border-top: 2px solid #eee; padding-top: 20px;">
        <p style="color: #28a745; font-size: 1.3rem; font-weight: 600;">
            📡 NFC Tag Updated — Customer can tap to claim!
        </p>
        <p style="color: #888; font-size: 0.95rem;">
            Tag contains: @Model.RewardAmountSatoshis sats reward
        </p>
    </div>
</div>
<div id="nfc-write-error" style="display: none; margin-top: 15px;">
    <p style="color: #dc3545;">⚠️ Could not write to NFC tag. Customer can scan QR instead.</p>
</div>
```

Plus the JS `writeToNfcTag()` function shown above.

**File: `ViewModels/DisplayRewardsViewModel.cs`**

Already has `LnurlString` — no change needed.

**File: `BitcoinRewardsStoreSettings.cs`**

Optional: Add a `NfcWriteEnabled` bool setting so the shop can toggle NFC tag writing on/off.

```csharp
/// <summary>Whether to auto-write LNURL to NFC tag on the display page.</summary>
public bool NfcTagWriteEnabled { get; set; } = false;
```

### For Option B (ESP32 writer): Add an API endpoint

**New or modified file: `Controllers/UIBitcoinRewardsController.cs`** (or a new `NfcApiController.cs`)

```csharp
[HttpGet("~/plugins/bitcoin-rewards/{storeId}/api/latest-lnurl")]
[AllowAnonymous] // or use a simple API key
public async Task<IActionResult> GetLatestLnurl(string storeId, [FromQuery] string? apiKey)
{
    // Validate API key if configured
    // Return latest unclaimed reward's LNURL string
    // { "lnurl": "lnurl1...", "rewardId": "...", "sats": 150, "createdAt": "..." }
}
```

This endpoint already essentially exists as the display page logic — just needs to return JSON instead of HTML.

---

## 5. Architecture: Reward-to-NFC Flow

```
┌─────────────┐     ┌──────────────┐     ┌────────────────┐
│  Customer    │     │  BTCPay      │     │  Display       │
│  pays $5     │────>│  Invoice     │────>│  Tablet        │
│  for coffee  │     │  settles     │     │  (Android)     │
└─────────────┘     └──────┬───────┘     └───────┬────────┘
                           │                      │
                    ┌──────▼───────┐              │
                    │ Plugin:      │              │
                    │ ProcessReward│              │
                    │ → PullPayment│              │
                    │ → LNURL-w    │              │
                    └──────┬───────┘              │
                           │                      │
                    ┌──────▼───────┐       ┌──────▼────────┐
                    │ Display page │       │ Auto-refresh   │
                    │ shows QR +   │◄──────│ picks up new   │
                    │ LNURL string │       │ reward         │
                    └──────┬───────┘       └───────────────┘
                           │
                    ┌──────▼───────┐
                    │ Web NFC API  │
                    │ writes LNURL │
                    │ to NTAG215   │
                    │ on counter   │
                    └──────┬───────┘
                           │
                    ┌──────▼───────┐
                    │ Customer     │
                    │ taps phone   │──> Phone reads NDEF URI
                    │ on tag       │──> Opens Lightning wallet
                    └──────────────┘──> Wallet claims LNURL-w
                                    ──> Sats received! ⚡
```

**Timing:**
1. Invoice settles → reward created (instant)
2. Display page auto-refreshes (every 10s configurable)
3. Web NFC writes to tag (< 1 second)
4. Customer taps (instant read)
5. Lightning wallet claims LNURL-w (1-3 seconds)

**Total: ~10-15 seconds from payment to NFC tag ready**

---

## 6. Hardware Shopping List (Coffee Shop Setup)

### Minimum Viable (Option A):
| Item | Cost | Notes |
|------|------|-------|
| NTAG215 NFC sticker tags (10-pack) | $8-12 | Rewritable, 504 bytes, stick to counter |
| Existing Android tablet | $0 | Already running display page |
| **Total** | **~$10** | |

### Enhanced (Option B, if tablet lacks NFC):
| Item | Cost | Notes |
|------|------|-------|
| ESP32-S3 DevKitC | $10-15 | WiFi + BLE built-in |
| PN532 NFC module | $5-8 | Read/write, I2C/SPI |
| NTAG215 stickers (10-pack) | $8-12 | |
| 3D-printed enclosure | $5 | Or use a simple project box |
| USB-C power | $0 | Power from any USB port |
| **Total** | **~$30-40** | |

### Tag Recommendations:
- **NTAG215** (504 bytes): Sweet spot for LNURL strings. Same chip as Amiibo.
- **NTAG216** (888 bytes): If you need room for longer URLs or future expansion.
- Avoid NTAG213 (144 bytes) — too small for LNURL strings.
- Get **sticker/adhesive** form factor — stick directly to the counter with a "Tap here ⚡" label.

### Where to buy (Canada):
- Amazon.ca: "NTAG215 NFC sticker" — various sellers, ~$1/tag
- AliExpress: Bulk NTAG215 cheaper but slower shipping
- GoToTags.com: Premium NFC tags, fast shipping

---

## 7. Security Considerations

### LNURL-w is single-use (pull payment)
- Each reward creates a unique pull payment with a specific sat amount
- Once claimed, the LNURL-w is spent — re-tapping gets nothing
- The display page already has a timeout + "Done" button to clear old rewards

### NFC tag contains ephemeral data
- Tag is rewritten with each new reward
- If no active reward, tag could contain stale LNURL (already claimed)
- **Mitigation:** Write a "no reward available" placeholder URL when reward expires/is claimed
- **Mitigation:** The LNURL-w endpoint will return an error if already claimed

### Physical proximity
- NFC read range: 1-4 cm — customer must physically be at counter
- No remote sniping risk

### Tag cloning
- Someone could read the tag and claim the reward on their phone
- **Same risk as QR code** — whoever scans/taps first wins
- Acceptable for a coffee shop reward (small amounts, 50-500 sats)

---

## 8. Implementation Plan

### Phase 1: Web NFC Tag Writing (1-2 hours of work)
1. Add NFC write JavaScript to `DisplayRewards.cshtml` (~20 lines)
2. Add `NfcTagWriteEnabled` setting to `BitcoinRewardsStoreSettings.cs`
3. Add toggle to settings page
4. Buy NTAG215 stickers, stick one to counter
5. Test on Android tablet with Chrome

### Phase 2: Clear Tag on Claim/Expiry (optional, nice-to-have)
1. When reward is claimed or expires, write a placeholder to tag
2. Placeholder could be shop URL or "no reward" page

### Phase 3: ESP32 Writer (only if needed)
1. Build ESP32 firmware that polls `/api/latest-lnurl`
2. Writes to NTAG on counter
3. LED indicator: green = reward ready, off = no reward

### Phase 4: QR Code Fallback Display
- Already exists! The QR code on the display page is the same LNURL
- NFC is just a faster/cooler claiming method alongside the QR

---

## 9. Summary: What's Needed vs. What Exists

| Component | Status | Work Needed |
|-----------|--------|-------------|
| LNURL-w generation | ✅ Exists | None |
| LNURL bech32 encoding | ✅ Exists | None |
| Display page with LNURL | ✅ Exists | None |
| QR code display | ✅ Exists | None |
| Auto-refresh on new reward | ✅ Exists | None |
| Reward timeout/dismiss | ✅ Exists | None |
| Web NFC write to tag | ❌ Missing | ~20 lines JS |
| NFC settings toggle | ❌ Missing | ~5 lines C# |
| Settings UI for NFC toggle | ❌ Missing | ~10 lines Razor |
| NTAG215 sticker on counter | ❌ Missing | Buy $1 tag |
| ESP32 writer (optional) | ❌ Missing | Future phase |
| New API endpoint (for ESP32) | ❌ Missing | Future phase |

**Bottom line: This is a ~30-minute code change + a $1 NFC sticker.**
