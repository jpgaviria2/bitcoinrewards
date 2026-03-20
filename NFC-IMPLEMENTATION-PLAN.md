# NFC LNURL-Withdraw Implementation Plan

**Date:** 2026-02-28
**Goal:** Add Web NFC to the DisplayRewards page so the Android display tablet pushes the LNURL-withdraw string via NFC. Customers tap their phone to the tablet (or a tag stuck on it) to claim rewards.

---

## Architecture Overview

The Android tablet running the display page uses the **Web NFC API** (`NDEFReader.write()`) to continuously write the current LNURL-withdraw string to a small NFC tag attached to the tablet. When a customer taps their phone on the tag, their phone reads the NDEF URI record (`lightning:lnurl1...`) and opens their Lightning wallet to claim.

**Key constraint:** Web NFC `write()` requires an initial **user gesture** (button tap). After that, it can re-write without gestures — but a full page reload resets the permission. The current display page uses `<meta http-equiv="refresh">` for auto-refresh, which causes full reloads.

**Solution:** When NFC is active, replace meta-refresh with AJAX polling that updates the QR/LNURL in-place without reloading, preserving the NFC write session.

**Hardware:** One NTAG215 NFC sticker (~$0.50) stuck to the back/bottom of the Android tablet with a "Tap here ⚡" label. The tablet writes to this tag; customers read from it.

---

## Files to Modify

### 1. `BitcoinRewardsStoreSettings.cs` — Add NFC setting

**File:** `BitcoinRewardsStoreSettings.cs` (line ~110, after `BoltCardEnabled`)

Add:
```csharp
/// <summary>Whether to enable NFC LNURL-withdraw push on the display page (requires NFC tag on device).</summary>
public bool NfcLnurlWriteEnabled { get; set; } = false;
```

### 2. `ViewModels/BitcoinRewardsSettingsViewModel.cs` — Add NFC to settings VM

**Line ~122**, after `BoltCardEnabled`:
```csharp
public bool NfcLnurlWriteEnabled { get; set; }
```

**Line ~209** (in `FromSettings`), after `BoltCardEnabled = settings.BoltCardEnabled;`:
```csharp
NfcLnurlWriteEnabled = settings.NfcLnurlWriteEnabled;
```

**Line ~240** (in `ToSettings`), after `settings.BoltCardEnabled = BoltCardEnabled;`:
```csharp
settings.NfcLnurlWriteEnabled = NfcLnurlWriteEnabled;
```

### 3. `ViewModels/DisplayRewardsViewModel.cs` — Add NFC flag

After `public bool BoltCardEnabled { get; set; }` (line 25):
```csharp
/// <summary>Whether Web NFC LNURL-write is enabled for this display.</summary>
public bool NfcLnurlWriteEnabled { get; set; }
```

### 4. `Controllers/UIBitcoinRewardsController.cs` — Pass NFC setting to view

**Line ~550**, after `BoltCardEnabled = settings.BoltCardEnabled,`:
```csharp
NfcLnurlWriteEnabled = settings.NfcLnurlWriteEnabled,
```

Also in the "no reward" ViewModels (lines ~455, ~481, ~498), add:
```csharp
NfcLnurlWriteEnabled = settings.NfcLnurlWriteEnabled,
```

**New API endpoint** — Add after the `DisplayRewards` action (~line 555). This is needed for AJAX polling when NFC is active:

```csharp
[HttpGet("~/plugins/bitcoin-rewards/{storeId}/display-rewards-json")]
[AllowAnonymous]
public async Task<IActionResult> DisplayRewardsJson(string storeId)
{
    var settings = await GetSettings(storeId);
    if (settings == null)
        return Json(new { hasReward = false });

    var latestReward = await _rewardsService.GetLatestUnclaimedRewardAsync(storeId, settings.DisplayTimeframeMinutes);
    
    if (latestReward?.ClaimLink == null)
        return Json(new { hasReward = false });

    // Check if already claimed
    if (!string.IsNullOrEmpty(latestReward.PullPaymentId))
    {
        var isClaimed = await _rewardsService.IsPullPaymentClaimedAsync(latestReward.PullPaymentId);
        if (isClaimed)
            return Json(new { hasReward = false });
    }

    var claimLink = latestReward.ClaimLink;
    var lnurlBech32 = GetLnurlBech32FromClaimLink(claimLink);
    var lnurlQrDataUri = !string.IsNullOrEmpty(lnurlBech32) ? GenerateQrDataUri(lnurlBech32) : null;

    var createdAt = latestReward.CreatedAt;
    var elapsed = createdAt.HasValue ? (DateTime.UtcNow - createdAt.Value).TotalSeconds : 0;
    var remaining = Math.Max(0, settings.DisplayTimeoutSeconds - (int)elapsed);

    return Json(new
    {
        hasReward = true,
        lnurlString = lnurlBech32,
        lnurlQrDataUri = lnurlQrDataUri,
        rewardAmountSatoshis = latestReward.RewardAmountSatoshis,
        orderId = latestReward.OrderId,
        rewardId = latestReward.Id?.ToString(),
        remainingSeconds = remaining,
        createdAt = createdAt?.ToLocalTime().ToString("h:mm:ss tt")
    });
}
```

### 5. `Views/UIBitcoinRewards/EditSettings.cshtml` — Add NFC toggle to settings UI

Find the BoltCard toggle section and add after it:

```html
<div class="form-group mb-4">
    <div class="form-check form-switch">
        <input type="hidden" name="NfcLnurlWriteEnabled" value="false">
        <input name="NfcLnurlWriteEnabled" class="form-check-input" type="checkbox" id="NfcLnurlWriteEnabled" value="true" @(Model.NfcLnurlWriteEnabled ? "checked" : "")>
        <label asp-for="NfcLnurlWriteEnabled" class="form-check-label">NFC LNURL Push</label>
    </div>
    <small class="form-text text-muted">
        Enable Web NFC on the display page to push LNURL-withdraw to an NFC tag. 
        Requires an NTAG215 sticker on the Android display device and Chrome 89+. 
        Customers tap their phone on the tag to claim rewards.
    </small>
</div>
```

### 6. `Views/UIBitcoinRewards/DisplayRewards.cshtml` — Main NFC integration

This is the core change. Add NFC write UI and JavaScript.

#### A. Add NFC status indicator (after the QR code section, before countdown timer, ~inside the `Model.HasReward` block)

After the BoltCard section (`@if (Model.BoltCardEnabled) { ... }`), add:

```html
@if (Model.NfcLnurlWriteEnabled)
{
<div id="nfc-write-section" style="margin-top: 25px;">
    <div style="border-top: 2px solid #eee; padding-top: 20px;">
        <div id="nfc-write-inactive">
            <button id="nfc-activate-btn" onclick="activateNfcWrite()" style="
                background: linear-gradient(135deg, #ff9800 0%, #f57c00 100%);
                color: white;
                border: none;
                padding: 18px 36px;
                font-size: 1.3rem;
                border-radius: 16px;
                cursor: pointer;
                font-weight: 600;
                box-shadow: 0 4px 15px rgba(255,152,0,0.4);
                transition: all 0.3s ease;
            ">
                📡 Activate NFC Push
            </button>
            <p style="color: #888; font-size: 0.9rem; margin-top: 8px;">
                Tap to start writing LNURL to NFC tag
            </p>
        </div>
        <div id="nfc-write-active" style="display: none;">
            <div style="background: #e8f5e9; border: 2px solid #4caf50; border-radius: 12px; padding: 15px; display: inline-block;">
                <span style="font-size: 1.4rem; color: #2e7d32; font-weight: 600;">
                    📡 NFC Active — Tap to claim!
                </span>
            </div>
            <p style="color: #666; font-size: 0.9rem; margin-top: 8px;">
                LNURL is being written to NFC tag
            </p>
        </div>
        <div id="nfc-write-error" style="display: none; margin-top: 10px;">
            <span style="color: #dc3545; font-size: 1rem;" id="nfc-write-error-msg"></span>
        </div>
        <div id="nfc-write-unavailable" style="display: none; margin-top: 10px;">
            <span style="color: #888; font-size: 0.9rem;">
                ℹ️ Web NFC not available — QR code only
            </span>
        </div>
    </div>
</div>
}
```

#### B. Add NFC write JavaScript (in the script section at the bottom, inside the `Model.HasReward` block)

```html
@if (Model.NfcLnurlWriteEnabled)
{
<script>
    // === NFC LNURL-Write Module ===
    let nfcWriteActive = false;
    let currentLnurl = '@(Model.LnurlString ?? "")';
    let nfcWriter = null;
    let nfcAbortController = null;
    let ajaxPollingActive = false;

    // Check Web NFC availability on load
    (function() {
        if (!('NDEFReader' in window)) {
            document.getElementById('nfc-write-inactive').style.display = 'none';
            document.getElementById('nfc-write-unavailable').style.display = 'block';
            return;
        }
    })();

    // Activate NFC write — requires user gesture (button click)
    async function activateNfcWrite() {
        if (!('NDEFReader' in window)) return;

        try {
            nfcWriter = new NDEFReader();
            nfcAbortController = new AbortController();

            // Write current LNURL to tag
            await writeCurrentLnurl();

            // Update UI
            document.getElementById('nfc-write-inactive').style.display = 'none';
            document.getElementById('nfc-write-active').style.display = 'block';
            document.getElementById('nfc-write-error').style.display = 'none';
            nfcWriteActive = true;

            // Switch from meta-refresh to AJAX polling to preserve NFC session
            disableMetaRefresh();
            startAjaxPolling();

        } catch (err) {
            const errEl = document.getElementById('nfc-write-error');
            document.getElementById('nfc-write-error-msg').textContent = 'NFC error: ' + err.message;
            errEl.style.display = 'block';
            console.error('NFC activation failed:', err);
        }
    }

    async function writeCurrentLnurl() {
        if (!nfcWriter || !currentLnurl) return;

        try {
            await nfcWriter.write({
                records: [{
                    recordType: "url",
                    data: "lightning:" + currentLnurl
                }]
            }, { signal: nfcAbortController.signal });

            console.log('NFC: LNURL written to tag');
        } catch (err) {
            if (err.name === 'AbortError') return; // Expected on cleanup
            console.error('NFC write failed:', err);
            // Don't show error — tag might just not be in range yet
            // write() waits for a tag to come into proximity
        }
    }

    // Write empty/placeholder when no reward is active
    async function clearNfcTag() {
        if (!nfcWriter) return;
        try {
            await nfcWriter.write({
                records: [{
                    recordType: "url",
                    data: "https://anmore.cash"
                }]
            });
        } catch (err) {
            // Ignore — tag may not be present
        }
    }

    // Disable meta refresh to keep NFC session alive
    function disableMetaRefresh() {
        // Remove meta refresh tag
        const metaRefresh = document.querySelector('meta[http-equiv="refresh"]');
        if (metaRefresh) metaRefresh.remove();
    }

    // AJAX polling replaces full page reload
    function startAjaxPolling() {
        if (ajaxPollingActive) return;
        ajaxPollingActive = true;

        setInterval(async () => {
            try {
                const resp = await fetch('/plugins/bitcoin-rewards/@Model.StoreId/display-rewards-json');
                const data = await resp.json();

                if (data.hasReward && data.lnurlString) {
                    // Update QR code if LNURL changed
                    if (data.lnurlString !== currentLnurl) {
                        currentLnurl = data.lnurlString;
                        
                        // Update QR image
                        const qrImg = document.querySelector('.qr-code img');
                        if (qrImg && data.lnurlQrDataUri) {
                            qrImg.src = data.lnurlQrDataUri;
                        }

                        // Update amount display
                        const amountEl = document.querySelector('.amount');
                        if (amountEl) amountEl.textContent = data.rewardAmountSatoshis.toLocaleString() + ' sats';

                        // Update order ID
                        const orderEl = document.querySelector('.order-id');
                        if (orderEl && data.orderId) orderEl.textContent = 'Order: ' + data.orderId;

                        // Re-write NFC tag with new LNURL
                        writeCurrentLnurl();

                        // Reset countdown
                        remainingSeconds = data.remainingSeconds;
                    }
                } else if (!data.hasReward) {
                    // Reward claimed or expired — show waiting state
                    // Full reload is fine here since reward is done
                    clearNfcTag();
                    window.location.reload();
                }
            } catch (err) {
                console.error('Polling error:', err);
            }
        }, @(Model.AutoRefreshSeconds) * 1000);
    }
</script>
}
```

#### C. Also add NFC section to the "waiting" state (no reward)

In the waiting display section, after the waiting message paragraph:

```html
@if (Model.NfcLnurlWriteEnabled)
{
<div style="margin-top: 15px;">
    <span style="color: #999; font-size: 0.9rem;">📡 NFC will activate when a reward is ready</span>
</div>
}
```

---

## NDEF Record Format

```
NDEF Record:
  TNF: 0x01 (Well-Known)
  Type: "U" (URI)
  Payload: "lightning:lnurl1dp68gurn8ghj7..."
```

Web NFC API creates this automatically when using `recordType: "url"` with `data: "lightning:lnurl1..."`.

When a customer's phone reads this tag:
1. Android/iOS detects NDEF URI record with `lightning:` scheme
2. OS dispatches to registered Lightning wallet app (Phoenix, Zeus, WoS, etc.)
3. Wallet decodes LNURL-withdraw → shows "Receive X sats?" → user confirms → sats received

---

## User Flows

### Staff Setup (one-time)
1. Enable "NFC LNURL Push" in plugin settings
2. Stick an NTAG215 sticker to the back/bottom of the Android display tablet
3. Add a "Tap here ⚡" label near the sticker
4. Open the display page in Chrome on the tablet
5. When first reward appears, tap "📡 Activate NFC Push" button once
6. NFC stays active until page is closed/navigated away

### Customer Claiming via NFC
1. Customer pays for coffee → invoice settles → reward created
2. Display shows QR code + "📡 NFC Active — Tap to claim!"
3. Customer holds their phone near the NFC tag on the tablet
4. Phone reads NDEF → opens Lightning wallet → "Receive 150 sats?" → Confirm → Done!
5. Display auto-refreshes to waiting state

### Customer Claiming via QR (fallback)
- Unchanged. QR code always visible regardless of NFC state.

---

## Graceful Degradation

| Scenario | Behavior |
|----------|----------|
| Web NFC not available (iOS, Firefox, old Chrome) | NFC UI hidden, "QR code only" note shown |
| NFC permission denied | Error message, QR still works |
| No NFC tag in range | `write()` waits silently, QR still works |
| Setting disabled | No NFC UI shown at all |
| LNURL already claimed | Customer's wallet shows error, display refreshes |

---

## Security Notes

- **Single-use LNURL-w:** Each reward generates a unique pull payment. Once claimed, the LNURL is spent. NFC replay gets nothing.
- **Physical proximity:** NFC read range is 1-4cm. No remote attack vector.
- **Same risk as QR:** First person to scan/tap wins. Acceptable for small reward amounts (50-500 sats).
- **Tag overwrite:** Tag content changes with each reward. Between rewards, tag points to shop URL (harmless).
- **No authentication needed:** LNURL-withdraw is inherently "bearer" — whoever has it can claim. Same as a paper coupon.

---

## Build & Test

### Build
```bash
cd /home/ln/.openclaw/workspace/btcpay-research/bitcoinrewards
PATH="/home/ln/.dotnet:$PATH" dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards/ -c Release
```

### Deploy to dev server
```bash
# Copy built plugin to Docker BTCPay instance
docker cp Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll btcpayserver_btcpayserver_1:/opt/btcpayserver/Plugins/
docker restart btcpayserver_btcpayserver_1
```

### Test NFC (requires physical Android device)
1. Open display page on Android Chrome (must be HTTPS)
2. Enable NFC in plugin settings
3. Create a test reward
4. Tap "Activate NFC Push" on display
5. Hold NTAG215 tag to back of tablet — verify write succeeds
6. Hold customer phone to tag — verify Lightning wallet opens with LNURL-w

### Test without NFC hardware
- Verify NFC UI shows/hides based on setting
- Verify AJAX polling works (check network tab)
- Verify QR code updates without page reload when NFC is active
- Verify graceful degradation in non-Chrome browsers

---

## Summary of Changes

| File | Change |
|------|--------|
| `BitcoinRewardsStoreSettings.cs` | Add `NfcLnurlWriteEnabled` bool property |
| `ViewModels/BitcoinRewardsSettingsViewModel.cs` | Add property + FromSettings/ToSettings mapping |
| `ViewModels/DisplayRewardsViewModel.cs` | Add `NfcLnurlWriteEnabled` bool property |
| `Controllers/UIBitcoinRewardsController.cs` | Pass setting to view + new JSON API endpoint |
| `Views/UIBitcoinRewards/EditSettings.cshtml` | Add NFC toggle checkbox |
| `Views/UIBitcoinRewards/DisplayRewards.cshtml` | Add NFC write UI + JavaScript (~100 lines) |

**Total: ~6 files, ~200 lines of code**
