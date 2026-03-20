# NFC-HCE Implementation Plan: LNURL-Withdraw via Android HCE

**Date:** 2026-02-28  
**Status:** Implementation Plan  
**Approach:** Android HCE (Host Card Emulation) — device emulates an NDEF Type 4 Tag  
**Based on:** Numo's NFC implementation (github.com/jpgaviria2/Numo)

---

## 1. Architecture Overview

### Component Diagram

```
┌──────────────┐     ┌───────────────────┐     ┌─────────────────────────────┐
│  BTCPay      │     │  Android Display  │     │  Customer's Phone           │
│  Server      │     │  App (WebView +   │     │                             │
│              │     │  HCE Service)     │     │  ┌───────────────────────┐  │
│  ┌────────┐  │HTTP │  ┌─────────────┐  │     │  │ OS reads NDEF tag     │  │
│  │Plugin: │  │◄───►│  │ WebView     │  │     │  │ → finds lightning:    │  │
│  │Bitcoin │  │     │  │ (display    │  │ NFC │  │   URI scheme          │  │
│  │Rewards │  │     │  │  page)      │  │◄───►│  │ → opens LN wallet    │  │
│  └────────┘  │     │  └──────┬──────┘  │     │  │ → wallet decodes     │  │
│              │     │         │ JS      │     │  │   LNURL-w             │  │
│              │     │         │ bridge  │     │  │ → claims sats         │  │
│              │     │  ┌──────▼──────┐  │     │  └───────────────────────┘  │
│              │     │  │ HCE Service │  │     │                             │
│              │     │  │ (NDEF tag   │  │     └─────────────────────────────┘
│              │     │  │  emulation) │  │
│              │     │  └─────────────┘  │
│              │     └───────────────────┘
└──────────────┘
```

### Data Flow

1. Invoice settles → plugin creates pull payment → generates LNURL-w string
2. Display page auto-refreshes (every 10s) → picks up new reward with `LnurlString`
3. JavaScript bridge calls native Android method: `AndroidBridge.setNfcPayload(lnurlString)`
4. Native code calls `hceService.setPaymentRequest("lightning:" + lnurlString)`
5. HCE service builds NDEF URI record and holds it in memory
6. Customer taps phone → Android OS routes APDU commands to HCE service
7. HCE responds with NDEF message containing `lightning:lnurl1dp68gurn8ghj7...`
8. Customer's phone reads NDEF → opens Lightning wallet → wallet claims LNURL-w → sats received ⚡
9. Display page countdown expires or "Done" tapped → JS bridge calls `AndroidBridge.clearNfcPayload()`

---

## 2. Android App Approach: WebView + HCE Service (Recommended)

### Options Evaluated

| Option | Description | Pros | Cons |
|--------|------------|------|------|
| **A. WebView + HCE (RECOMMENDED)** | Native app with WebView showing BTCPay display page + HCE service | Single app, JS bridge for NFC control, full HCE access | Requires building native Android app |
| B. Standalone native app | Fully native UI replacing the web display | Best UX control | Huge effort, duplicates existing display logic |
| C. Companion app | Separate HCE app runs alongside Chrome | No changes to display | Two apps to manage, no coordination, can't detect reward changes |

### Why Option A Wins

- The display page already works great in a browser — keep it in a WebView
- A JavaScript interface (`@JavascriptInterface`) lets the web page control the HCE service directly
- Single APK to install on the counter tablet
- The WebView loads the existing BTCPay display URL — zero server-side changes needed for basic functionality
- When the page detects a reward (already has `Model.LnurlString`), JS calls `AndroidBridge.setNfcPayload(lnurl)`
- When reward expires/dismissed, JS calls `AndroidBridge.clearNfcPayload()`

---

## 3. Stripped-Down HCE Service (from Numo)

### Files to Copy/Adapt from Numo

| Numo File | Action | Changes |
|-----------|--------|---------|
| `NdefHostCardEmulationService.java` | **Copy & simplify** | Remove: CashuPaymentCallback, onNdefMessageReceived, Cashu token extraction, write-back handling, NFC reading state. Keep: processCommandApdu, setPaymentRequest(String), clearPaymentRequest, singleton pattern, HCE lifecycle |
| `NdefProcessor.java` | **Copy & simplify** | Remove: NdefMessageCallback.onNdefMessageReceived, NdefMessageParser, NdefUpdateBinaryHandler, setProcessIncomingMessages. Keep: processCommandApdu (SELECT AID, SELECT FILE, READ BINARY only), setMessageToSend, setWriteMode |
| `NdefMessageBuilder.java` | **Replace** | Current builds NDEF Text records ("T"). We need NDEF URI records ("U"). Complete rewrite — see §4 |
| `NdefConstants.java` | **Copy as-is** | All APDU constants, file IDs, CC file are standard NDEF Type 4. No changes needed |
| `NdefApduHandler.java` | **Copy & simplify** | Remove: write-mode gate on NDEF file select (we always want to serve the message). Keep: handleSelectFile, handleReadBinary |
| `NdefStateManager.java` | **Copy & simplify** | Remove: processIncomingMessages, ndefData buffer, expectedNdefLength, lastMessageActivityTime. Keep: messageToSend, isInWriteMode, selectedFile |
| `NdefUtils.java` | **Copy as-is** | Just bytesToHex utility |
| `NdefUriProcessor.java` | **Not needed** | Only used for parsing received URIs |
| `NdefUpdateBinaryHandler.java` | **Not needed** | Handles write-back from peer device |
| `CashuPaymentHelper.kt` | **Not needed** | Cashu-specific |
| `NfcPaymentProcessor.kt` | **Not needed** | Cashu payment flow |
| `aid_list.xml` | **Copy as-is** | Standard NDEF AID: D2760000850101 |
| `AndroidManifest.xml` | **New, based on Numo's** | Keep: NFC permissions, HCE service declaration. Add: INTERNET permission, WebView activity |

### Detailed Changes to NdefHostCardEmulationService

```java
// REMOVE these entirely:
- CashuPaymentCallback interface (and all callback-related code)
- onNdefMessageReceived / Cashu token extraction
- isNfcReading / nfcTimeoutHandler / startOrResetNfcReading / stopNfcReading
- expectedAmount field
- paymentCallback field

// KEEP these:
- processCommandApdu() → delegates to NdefProcessor
- setPaymentRequest(String paymentRequest) → simplified, just sets on processor
- clearPaymentRequest() → clears processor message
- getInstance() singleton pattern
- isHceAvailable() static check
- onCreate/onDestroy lifecycle

// ADD:
- NfcTapListener interface with onTap() callback (for UI animation)
- Call listener.onTap() when processCommandApdu receives first APDU of a session
```

### Detailed Changes to NdefApduHandler

```java
// In handleSelectFile(), change the NDEF file selection:
// BEFORE (Numo): Only serves file if isInWriteMode && hasMessage
// AFTER: Always serve file if message is set (we're always "broadcasting")

// Replace this check:
if (stateManager.isInWriteMode() && !stateManager.getMessageToSend().isEmpty()) {
// With:
if (!stateManager.getMessageToSend().isEmpty()) {
```

---

## 4. NDEF Record Format

### Target Format: NDEF URI Record

The customer's phone needs to receive an NDEF message with a URI record containing `lightning:lnurl1dp68gurn8ghj7...`. This triggers the OS to open the registered Lightning wallet app.

```
NDEF URI Record:
  TNF: 0x01 (Well-Known)
  Type: "U" (0x55)
  Payload: 0x00 + "lightning:lnurl1dp68gurn8ghj7..."
           ^--- 0x00 = no URI prefix abbreviation
```

Note: `lightning:` is not in the standard URI prefix table (NdefUriProcessor shows codes 0x00-0x23), so we use 0x00 (no prefix) and include the full URI string.

### New NdefMessageBuilder (complete replacement)

```java
package com.example.rewardsnfc.ndef;

import java.nio.charset.Charset;

/**
 * Builder for creating NDEF URI record messages.
 * Creates an NDEF Type 4 Tag file containing a single URI record
 * with the lightning: scheme for LNURL-withdraw.
 */
public class NdefMessageBuilder {

    /**
     * Create an NDEF message containing a URI record.
     * 
     * @param uri Full URI string, e.g. "lightning:lnurl1dp68gurn..."
     * @return Complete NDEF file bytes (2-byte length prefix + NDEF record)
     */
    public static byte[] createNdefUriMessage(String uri) {
        byte[] uriBytes = uri.getBytes(Charset.forName("UTF-8"));

        // URI record payload: [URI identifier code][URI string]
        // 0x00 = no abbreviation, full URI follows
        byte[] payload = new byte[1 + uriBytes.length];
        payload[0] = 0x00; // No prefix abbreviation
        System.arraycopy(uriBytes, 0, payload, 1, uriBytes.length);

        // Type "U" (URI)
        byte[] type = { 0x55 }; // "U"

        // Build NDEF record
        boolean isShortRecord = payload.length <= 255;
        int headerLen = isShortRecord ? 3 : 6;
        byte[] record = new byte[headerLen + type.length + payload.length];

        // Header byte: MB=1, ME=1, CF=0, SR=?, IL=0, TNF=0x01 (well-known)
        // 0xD1 = 1101_0001 = MB|ME|SR|TNF=01
        // 0xC1 = 1100_0001 = MB|ME|TNF=01 (no SR)
        record[0] = isShortRecord ? (byte) 0xD1 : (byte) 0xC1;

        // Type length
        record[1] = (byte) type.length;

        int idx;
        if (isShortRecord) {
            record[2] = (byte) payload.length;
            idx = 3;
        } else {
            int pl = payload.length;
            record[2] = (byte) ((pl >> 24) & 0xFF);
            record[3] = (byte) ((pl >> 16) & 0xFF);
            record[4] = (byte) ((pl >> 8) & 0xFF);
            record[5] = (byte) (pl & 0xFF);
            idx = 6;
        }

        // Type field
        System.arraycopy(type, 0, record, idx, type.length);
        idx += type.length;

        // Payload
        System.arraycopy(payload, 0, record, idx, payload.length);

        // Wrap in NDEF file format: 2-byte length prefix + record
        int ndefLen = record.length;
        byte[] fullFile = new byte[2 + ndefLen];
        fullFile[0] = (byte) ((ndefLen >> 8) & 0xFF);
        fullFile[1] = (byte) (ndefLen & 0xFF);
        System.arraycopy(record, 0, fullFile, 2, ndefLen);

        return fullFile;
    }

    /**
     * Legacy method for compatibility. Creates a Text record.
     * Use createNdefUriMessage() for URI records instead.
     */
    public static byte[] createNdefMessage(String message) {
        // Keep Numo's original text record builder as fallback
        // (copy existing implementation)
        return createNdefUriMessage("lightning:" + message);
    }
}
```

### Typical payload size

- LNURL bech32 string: ~180-250 chars
- `lightning:` prefix: 10 chars
- URI identifier code: 1 byte
- NDEF record overhead: ~6 bytes
- Length prefix: 2 bytes
- **Total: ~200-270 bytes** — well within NDEF Type 4 limits

### Wallet Compatibility

Both iOS and Android Lightning wallets register for `lightning:` URI scheme:
- **Phoenix** ✅ (iOS + Android)
- **Zeus** ✅
- **Wallet of Satoshi** ✅
- **Breez** ✅
- **BlueWallet** ✅
- **Alby Go** ✅

When the phone's NFC reads an NDEF URI record with `lightning:lnurl1...`, the OS prompts to open the registered handler — same as clicking a `lightning:` link in a web page.

---

## 5. Integration with BTCPay Display

### How the App Knows About New Rewards

The existing display page already handles this via `<meta http-equiv="refresh" content="@Model.AutoRefreshSeconds">`. The WebView reloads the page every 10 seconds. When a reward exists, `Model.LnurlString` is populated.

**Approach: Inject JavaScript that detects the LNURL and calls the native bridge.**

No new API endpoints needed. The WebView loads the same URL Chrome would: 
```
https://<btcpay-host>/plugins/bitcoin-rewards/<storeId>/display
```

### JavaScript Bridge Integration

Add a small script injection after page load that:

1. Checks if `Model.LnurlString` is present in the page
2. If present → calls `AndroidBridge.setNfcPayload(lnurl)`  
3. If not present (waiting state) → calls `AndroidBridge.clearNfcPayload()`
4. On countdown expiry / "Done" click → calls `AndroidBridge.clearNfcPayload()`

Two ways to inject this:

**Option A (preferred): Modify DisplayRewards.cshtml to include NFC HCE bridge code**
Add a small `<script>` block that checks for `window.AndroidBridge` and calls it. Zero impact if not running in the native app (bridge won't exist, calls are no-ops).

**Option B: Inject JS from the Android WebView client**
Override `onPageFinished()` in `WebViewClient` and run `evaluateJavascript()` to extract the LNURL from the page DOM.

**Recommendation: Option B** — requires zero server-side changes. The Android app scrapes the LNURL from the loaded page.

### WebView JS Injection (Option B Detail)

```java
webView.setWebViewClient(new WebViewClient() {
    @Override
    public void onPageFinished(WebView view, String url) {
        // Extract LNURL from the page and set on HCE service
        view.evaluateJavascript(
            "(function() {" +
            "  var img = document.querySelector('.qr-code img');" +
            "  var waiting = document.getElementById('waiting-display');" +
            "  if (waiting) return JSON.stringify({hasReward: false});" +
            "  // Look for LNURL in the page source" +
            "  var scripts = document.querySelectorAll('script');" +
            "  for (var s of scripts) {" +
            "    var m = s.textContent.match(/lnurl[0-9a-z]{50,}/);" +
            "    if (m) return JSON.stringify({hasReward: true, lnurl: m[0]});" +
            "  }" +
            "  return JSON.stringify({hasReward: false});" +
            "})()",
            value -> {
                // Parse result and update HCE service
                handleLnurlExtraction(value);
            }
        );
    }
});
```

**Better approach: Add a hidden element to the display page (tiny server change):**

```html
<!-- Add to DisplayRewards.cshtml, inside the reward block -->
<div id="nfc-lnurl-data" data-lnurl="@Model.LnurlString" style="display:none;"></div>
```

Then the WebView extraction is trivial:
```javascript
"document.getElementById('nfc-lnurl-data')?.dataset?.lnurl || ''"
```

### Reward Lifecycle → NFC State

| Display State | NFC State |
|--------------|-----------|
| Waiting for rewards | HCE cleared (no NDEF message) |
| Reward displayed with QR | HCE broadcasting `lightning:lnurl1...` |
| Countdown expiring | Still broadcasting |
| "Done" clicked / timeout | HCE cleared |
| Page refresh → new reward | HCE updated with new LNURL |
| Page refresh → no reward | HCE cleared |

---

## 6. UI/UX

### Display Changes

Add an NFC indicator to the display page when a reward is active. This is a **minimal** server-side change to `DisplayRewards.cshtml`:

```html
<!-- Add below the QR code section, inside the reward block -->
<div id="nfc-hce-indicator" style="margin-top: 20px; display: none;">
    <div style="border-top: 2px solid #eee; padding-top: 20px;">
        <div style="font-size: 2.5rem; animation: pulse 2s ease-in-out infinite;">📱</div>
        <p style="color: #28a745; font-size: 1.3rem; font-weight: 600; margin-top: 10px;">
            or tap your phone here to claim
        </p>
        <p style="color: #888; font-size: 0.95rem; margin-top: 5px;">
            Hold your phone near this device
        </p>
    </div>
</div>
```

The Android app shows/hides this indicator via JS injection when the HCE service is active.

### Tap Feedback

When a customer taps their phone (HCE receives APDU commands), the app:
1. Briefly shows a "📡 Tap detected!" overlay animation (1-2 seconds)
2. Optionally vibrates the display device
3. Does NOT auto-dismiss the reward (the customer's wallet still needs to complete the LNURL-w claim; first-tap doesn't mean claimed)

### Staff Experience

- **Zero staff action required** — fully automatic
- Staff sees same display as before (QR code + countdown)
- Additional "📱 or tap your phone here" indicator below QR
- Staff can still use "Done" button to dismiss manually

---

## 7. Security Considerations

### LNURL-w is Single-Use
Each reward creates a unique pull payment. Once claimed via LNURL-w, subsequent claims fail. NFC replay is a non-issue.

### No Stale Broadcasts
- HCE only broadcasts when `LnurlString` is present on the display page
- When reward expires/is dismissed, `clearPaymentRequest()` is called
- NdefProcessor returns `NDEF_RESPONSE_ERROR` when no message is set (Numo's existing behavior in NdefApduHandler: returns error when message is empty)

### Two Customers Tap Simultaneously
- First phone to complete the LNURL-w claim gets the sats
- Second phone's wallet will show "already claimed" or similar error from BTCPay's LNURL endpoint
- This is identical to the QR code race condition — acceptable for small reward amounts

### Physical Proximity
- NFC HCE range: 1-4 cm — customer must physically be at the counter device
- No remote sniping possible

### HCE vs Physical Tag Security
- HCE is actually **more secure** than a physical NFC tag: the message only exists in volatile memory, controlled by the app
- Physical tags can be read/cloned anytime; HCE only responds when the app chooses to

---

## 8. Plugin-Side Changes

### Minimal Required Changes

**Only one small change to `DisplayRewards.cshtml`:**

Add a data attribute so the Android WebView can reliably extract the LNURL:

```html
<!-- Add inside the reward display block -->
<div id="nfc-lnurl-data" 
     data-lnurl="@Model.LnurlString" 
     data-reward-id="@Model.RewardId"
     data-amount-sats="@Model.RewardAmountSatoshis"
     style="display:none;"></div>
```

**That's it.** No new endpoints, no new settings, no backend changes.

### Optional Future Enhancements

1. **NFC HCE enabled setting** — `BitcoinRewardsStoreSettings.NfcHceEnabled` bool toggle in settings page, controls whether the `nfc-hce-indicator` div renders
2. **Reward claimed webhook** — when pull payment is claimed, plugin could push a notification (but the page auto-refresh already handles this)
3. **API endpoint for polling** — `GET /plugins/bitcoin-rewards/{storeId}/api/current-reward` returning JSON. Useful if we later want to skip WebView entirely. But WebView approach means this isn't needed now.

---

## 9. File-by-File Implementation List

### Android App Project Structure

```
RewardsNfcDisplay/
├── app/
│   ├── src/main/
│   │   ├── java/com/bitcoinrewards/nfcdisplay/
│   │   │   ├── MainActivity.java
│   │   │   ├── AndroidBridge.java
│   │   │   └── ndef/
│   │   │       ├── NdefHostCardEmulationService.java
│   │   │       ├── NdefProcessor.java
│   │   │       ├── NdefMessageBuilder.java
│   │   │       ├── NdefConstants.java
│   │   │       ├── NdefApduHandler.java
│   │   │       ├── NdefStateManager.java
│   │   │       └── NdefUtils.java
│   │   ├── res/
│   │   │   ├── xml/
│   │   │   │   └── aid_list.xml
│   │   │   ├── layout/
│   │   │   │   └── activity_main.xml
│   │   │   └── values/
│   │   │       └── strings.xml
│   │   └── AndroidManifest.xml
│   └── build.gradle
├── build.gradle
├── settings.gradle
└── gradle.properties
```

### File Details

---

#### `AndroidManifest.xml`
**What:** App manifest with NFC/HCE permissions and service declaration.

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    package="com.bitcoinrewards.nfcdisplay">

    <uses-permission android:name="android.permission.NFC" />
    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.VIBRATE" />
    <uses-feature android:name="android.hardware.nfc" android:required="true" />
    <uses-feature android:name="android.hardware.nfc.hce" android:required="true" />

    <application
        android:allowBackup="false"
        android:label="@string/app_name"
        android:theme="@style/Theme.MaterialComponents.NoActionBar">

        <activity
            android:name=".MainActivity"
            android:exported="true"
            android:configChanges="orientation|screenSize|keyboardHidden"
            android:screenOrientation="portrait">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>

        <service
            android:name=".ndef.NdefHostCardEmulationService"
            android:exported="true"
            android:permission="android.permission.BIND_NFC_SERVICE">
            <intent-filter>
                <action android:name="android.nfc.cardemulation.action.HOST_APDU_SERVICE" />
                <category android:name="android.intent.category.DEFAULT" />
            </intent-filter>
            <meta-data
                android:name="android.nfc.cardemulation.host_apdu_service"
                android:resource="@xml/aid_list" />
        </service>
    </application>
</manifest>
```

---

#### `res/xml/aid_list.xml`
**What:** AID registration for NDEF Type 4 tag emulation. Copied from Numo as-is.

```xml
<?xml version="1.0" encoding="utf-8"?>
<host-apdu-service xmlns:android="http://schemas.android.com/apk/res/android"
    android:description="@string/hce_service_description"
    android:requireDeviceUnlock="false">
    <aid-group android:description="@string/ndef_aid_description" android:category="other">
        <aid-filter android:name="D2760000850101" />
    </aid-group>
</host-apdu-service>
```

---

#### `res/layout/activity_main.xml`
**What:** Simple fullscreen WebView layout with NFC status overlay.

```xml
<?xml version="1.0" encoding="utf-8"?>
<FrameLayout xmlns:android="http://schemas.android.com/apk/res/android"
    android:layout_width="match_parent"
    android:layout_height="match_parent">

    <WebView
        android:id="@+id/webview"
        android:layout_width="match_parent"
        android:layout_height="match_parent" />

    <!-- NFC tap feedback overlay -->
    <TextView
        android:id="@+id/nfc_tap_overlay"
        android:layout_width="match_parent"
        android:layout_height="match_parent"
        android:gravity="center"
        android:background="#CC000000"
        android:textColor="#FFFFFF"
        android:textSize="28sp"
        android:text="📡 Tap detected!"
        android:visibility="gone" />
</FrameLayout>
```

---

#### `MainActivity.java`
**What:** Main activity hosting WebView + JS bridge + HCE service binding.

```java
package com.bitcoinrewards.nfcdisplay;

import android.app.Activity;
import android.nfc.NfcAdapter;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Vibrator;
import android.util.Log;
import android.view.View;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.TextView;

import com.bitcoinrewards.nfcdisplay.ndef.NdefHostCardEmulationService;

public class MainActivity extends Activity {
    private static final String TAG = "RewardsNFC";

    // CONFIGURE THIS: your BTCPay display URL
    private static final String DISPLAY_URL = "https://anmore.cash/plugins/bitcoin-rewards/{STORE_ID}/display";

    private WebView webView;
    private TextView nfcTapOverlay;
    private Handler handler = new Handler(Looper.getMainLooper());
    private String currentLnurl = null;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // Keep screen on (kiosk display)
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        setContentView(R.layout.activity_main);

        nfcTapOverlay = findViewById(R.id.nfc_tap_overlay);

        webView = findViewById(R.id.webview);
        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setCacheMode(WebSettings.LOAD_DEFAULT);

        // Add JS bridge
        webView.addJavascriptInterface(new AndroidBridge(), "AndroidBridge");

        webView.setWebViewClient(new WebViewClient() {
            @Override
            public void onPageFinished(WebView view, String url) {
                // Extract LNURL from page after load
                extractLnurlFromPage(view);
            }
        });

        webView.loadUrl(DISPLAY_URL);

        // Check NFC availability
        if (!NdefHostCardEmulationService.isHceAvailable(this)) {
            Log.e(TAG, "HCE not available on this device!");
        }
    }

    private void extractLnurlFromPage(WebView view) {
        // Try to get LNURL from the data attribute we added, or scrape from page
        view.evaluateJavascript(
            "(function() {" +
            "  var el = document.getElementById('nfc-lnurl-data');" +
            "  if (el && el.dataset.lnurl) return el.dataset.lnurl;" +
            "  // Fallback: find lnurl in page text" +
            "  var html = document.body.innerHTML;" +
            "  var m = html.match(/lnurl[0-9a-zA-Z]{50,}/);" +
            "  return m ? m[0] : '';" +
            "})()",
            value -> {
                // evaluateJavascript wraps result in quotes
                String lnurl = value != null ? value.replace("\"", "").trim() : "";
                if (!lnurl.isEmpty() && lnurl.startsWith("lnurl")) {
                    setNfcPayload(lnurl);
                } else {
                    clearNfcPayload();
                }
            }
        );
    }

    private void setNfcPayload(String lnurl) {
        if (lnurl.equals(currentLnurl)) return; // No change
        currentLnurl = lnurl;

        NdefHostCardEmulationService hce = NdefHostCardEmulationService.getInstance();
        if (hce != null) {
            hce.setPaymentRequest("lightning:" + lnurl);
            Log.i(TAG, "NFC HCE broadcasting: lightning:" + lnurl.substring(0, Math.min(30, lnurl.length())) + "...");

            // Show NFC indicator on the page
            webView.evaluateJavascript(
                "var ind = document.getElementById('nfc-hce-indicator');" +
                "if (ind) ind.style.display = 'block';",
                null
            );
        }
    }

    private void clearNfcPayload() {
        if (currentLnurl == null) return;
        currentLnurl = null;

        NdefHostCardEmulationService hce = NdefHostCardEmulationService.getInstance();
        if (hce != null) {
            hce.clearPaymentRequest();
            Log.i(TAG, "NFC HCE cleared");
        }
    }

    /**
     * Called by HCE service when a customer taps their phone
     */
    public void onNfcTapDetected() {
        handler.post(() -> {
            // Show tap overlay briefly
            nfcTapOverlay.setVisibility(View.VISIBLE);

            // Vibrate
            Vibrator v = (Vibrator) getSystemService(VIBRATOR_SERVICE);
            if (v != null) v.vibrate(100);

            // Hide after 1.5 seconds
            handler.postDelayed(() -> nfcTapOverlay.setVisibility(View.GONE), 1500);
        });
    }

    /**
     * JavaScript interface for the web page (optional, if using Option A approach)
     */
    private class AndroidBridge {
        @JavascriptInterface
        public void setNfcPayload(String lnurl) {
            MainActivity.this.setNfcPayload(lnurl);
        }

        @JavascriptInterface
        public void clearNfcPayload() {
            MainActivity.this.clearNfcPayload();
        }

        @JavascriptInterface
        public boolean isNfcAvailable() {
            return NdefHostCardEmulationService.isHceAvailable(MainActivity.this);
        }
    }

    @Override
    protected void onDestroy() {
        clearNfcPayload();
        super.onDestroy();
    }
}
```

---

#### `ndef/NdefHostCardEmulationService.java`
**What:** Simplified HCE service. Emulates NDEF Type 4 Tag, broadcasts LNURL-w URI.

```java
package com.bitcoinrewards.nfcdisplay.ndef;

import android.content.Intent;
import android.nfc.cardemulation.HostApduService;
import android.os.Bundle;
import android.util.Log;

/**
 * Simplified HCE service for broadcasting LNURL-withdraw via NFC.
 * Based on Numo's NdefHostCardEmulationService, stripped to read-only NDEF emulation.
 */
public class NdefHostCardEmulationService extends HostApduService {
    private static final String TAG = "NdefHCEService";
    private static final byte[] STATUS_FAILED = {(byte) 0x6F, (byte) 0x00};

    private NdefProcessor ndefProcessor;
    private static NdefHostCardEmulationService instance;
    private boolean tapNotified = false; // Avoid spamming tap callbacks

    public interface NfcTapListener {
        void onNfcTapDetected();
    }
    private NfcTapListener tapListener;

    public static NdefHostCardEmulationService getInstance() { return instance; }

    @Override
    public void onCreate() {
        super.onCreate();
        ndefProcessor = new NdefProcessor();
        instance = this;
        Log.i(TAG, "HCE Service created");
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        if (instance == this) instance = null;
        Log.i(TAG, "HCE Service destroyed");
    }

    @Override
    public byte[] processCommandApdu(byte[] commandApdu, Bundle extras) {
        try {
            // Notify tap listener on first APDU of a session
            if (!tapNotified && tapListener != null) {
                tapNotified = true;
                tapListener.onNfcTapDetected();
            }

            byte[] response = ndefProcessor.processCommandApdu(commandApdu);
            if (response != NdefConstants.NDEF_RESPONSE_ERROR) {
                return response;
            }
            return STATUS_FAILED;
        } catch (Exception e) {
            Log.e(TAG, "Error processing APDU: " + e.getMessage(), e);
            return STATUS_FAILED;
        }
    }

    @Override
    public void onDeactivated(int reason) {
        tapNotified = false; // Reset for next tap session
        Log.i(TAG, "HCE deactivated, reason: " + reason);
    }

    public void setPaymentRequest(String uri) {
        if (ndefProcessor != null) {
            ndefProcessor.setMessageToSend(uri);
            Log.i(TAG, "Broadcasting: " + uri.substring(0, Math.min(40, uri.length())) + "...");
        }
    }

    public void clearPaymentRequest() {
        if (ndefProcessor != null) {
            ndefProcessor.setMessageToSend("");
            Log.i(TAG, "Broadcast cleared");
        }
    }

    public void setTapListener(NfcTapListener listener) {
        this.tapListener = listener;
    }

    public static boolean isHceAvailable(android.content.Context context) {
        try {
            android.nfc.NfcAdapter adapter = android.nfc.NfcAdapter.getDefaultAdapter(context);
            return adapter != null && adapter.isEnabled() &&
                context.getPackageManager().hasSystemFeature(
                    android.content.pm.PackageManager.FEATURE_NFC_HOST_CARD_EMULATION);
        } catch (Exception e) {
            return false;
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        return START_STICKY;
    }
}
```

---

#### `ndef/NdefProcessor.java`
**What:** Simplified APDU command router. Read-only (no UPDATE BINARY support).

```java
package com.bitcoinrewards.nfcdisplay.ndef;

import android.util.Log;
import java.util.Arrays;

/**
 * Processes APDU commands for NDEF Type 4 Tag emulation (read-only).
 */
public class NdefProcessor {
    private static final String TAG = "NdefProcessor";

    private final NdefStateManager stateManager;
    private final NdefApduHandler apduHandler;

    public NdefProcessor() {
        this.stateManager = new NdefStateManager();
        this.apduHandler = new NdefApduHandler(stateManager);
    }

    public void setMessageToSend(String message) {
        stateManager.setMessageToSend(message);
    }

    public byte[] processCommandApdu(byte[] commandApdu) {
        // SELECT AID
        if (Arrays.equals(commandApdu, NdefConstants.NDEF_SELECT_AID)) {
            return NdefConstants.NDEF_RESPONSE_OK;
        }

        // SELECT FILE
        if (commandApdu.length >= 7 &&
            Arrays.equals(Arrays.copyOfRange(commandApdu, 0, 4), NdefConstants.NDEF_SELECT_FILE_HEADER)) {
            return apduHandler.handleSelectFile(commandApdu);
        }

        // READ BINARY
        if (commandApdu.length >= 5 &&
            Arrays.equals(Arrays.copyOfRange(commandApdu, 0, 2), NdefConstants.NDEF_READ_BINARY_HEADER)) {
            return apduHandler.handleReadBinary(commandApdu);
        }

        // No UPDATE BINARY support — read-only tag
        return NdefConstants.NDEF_RESPONSE_ERROR;
    }
}
```

---

#### `ndef/NdefApduHandler.java`
**What:** Handles SELECT FILE and READ BINARY. Simplified from Numo — always serves NDEF if message is set.

```java
package com.bitcoinrewards.nfcdisplay.ndef;

import android.util.Log;
import java.util.Arrays;

public class NdefApduHandler {
    private static final String TAG = "NdefApduHandler";
    private final NdefStateManager stateManager;

    public NdefApduHandler(NdefStateManager stateManager) {
        this.stateManager = stateManager;
    }

    public byte[] handleSelectFile(byte[] apdu) {
        byte[] fileId = Arrays.copyOfRange(apdu, 5, 7);

        if (Arrays.equals(fileId, NdefConstants.CC_FILE_ID)) {
            stateManager.setSelectedFile(NdefConstants.CC_FILE);
            return NdefConstants.NDEF_RESPONSE_OK;
        }

        if (Arrays.equals(fileId, NdefConstants.NDEF_FILE_ID)) {
            String message = stateManager.getMessageToSend();
            if (!message.isEmpty()) {
                byte[] ndefMessage = NdefMessageBuilder.createNdefUriMessage(message);
                stateManager.setSelectedFile(ndefMessage);
                return NdefConstants.NDEF_RESPONSE_OK;
            }
            // No message to broadcast
            return NdefConstants.NDEF_RESPONSE_ERROR;
        }

        return NdefConstants.NDEF_RESPONSE_ERROR;
    }

    public byte[] handleReadBinary(byte[] apdu) {
        byte[] selectedFile = stateManager.getSelectedFile();
        if (selectedFile == null || apdu.length < 5) {
            return NdefConstants.NDEF_RESPONSE_ERROR;
        }

        int length = (apdu[4] & 0xFF);
        if (length == 0) length = 256;
        int offset = ((apdu[2] & 0xFF) << 8) | (apdu[3] & 0xFF);

        if (offset + length > selectedFile.length) {
            return NdefConstants.NDEF_RESPONSE_ERROR;
        }

        byte[] data = Arrays.copyOfRange(selectedFile, offset, offset + length);
        byte[] response = new byte[data.length + 2];
        System.arraycopy(data, 0, response, 0, data.length);
        System.arraycopy(NdefConstants.NDEF_RESPONSE_OK, 0, response, data.length, 2);

        return response;
    }
}
```

---

#### `ndef/NdefStateManager.java`
**What:** Minimal state — just holds current message and selected file.

```java
package com.bitcoinrewards.nfcdisplay.ndef;

public class NdefStateManager {
    private String messageToSend = "";
    private byte[] selectedFile = null;

    public String getMessageToSend() { return messageToSend; }
    public void setMessageToSend(String message) { this.messageToSend = message; }
    public byte[] getSelectedFile() { return selectedFile; }
    public void setSelectedFile(byte[] file) { this.selectedFile = file; }
}
```

---

#### `ndef/NdefConstants.java`
**What:** APDU constants. Copied from Numo as-is (all standard NDEF Type 4).

*(Same as Numo's NdefConstants.java — see source. No changes needed.)*

---

#### `ndef/NdefMessageBuilder.java`
**What:** Builds NDEF URI records. **New implementation** (replaces Numo's Text record builder).

*(Full implementation provided in §4 above.)*

---

#### `ndef/NdefUtils.java`
**What:** bytesToHex utility. Copied from Numo as-is.

---

### BTCPay Plugin Change (1 file, 1 line)

#### `Views/UIBitcoinRewards/DisplayRewards.cshtml`

**Add inside the reward display block (after the QR code img, before reward-info div):**

```html
<div id="nfc-lnurl-data" data-lnurl="@Model.LnurlString" data-reward-id="@Model.RewardId" style="display:none;"></div>

<!-- NFC HCE indicator (shown by Android app when HCE is active) -->
<div id="nfc-hce-indicator" style="margin-top: 20px; display: none;">
    <div style="border-top: 2px solid #eee; padding-top: 20px;">
        <div style="font-size: 2.5rem; animation: pulse 2s ease-in-out infinite;">📱</div>
        <p style="color: #28a745; font-size: 1.3rem; font-weight: 600; margin-top: 10px;">
            or tap your phone on this device to claim
        </p>
    </div>
</div>
```

---

## 10. Build & Deploy

### Prerequisites

- **Android SDK** (not currently installed on P50 — install via `sdkmanager` or Android Studio)
- **Java 17+** (for Gradle)
- Minimum Android API: **19 (Android 4.4 KitKat)** — HCE was introduced in API 19
- Recommended target: **API 34 (Android 14)**
- Target device: Any Android phone/tablet with NFC and HCE support

### Build Steps

```bash
# 1. Install Android SDK (if not present)
# On Ubuntu/Debian:
sudo apt install android-sdk
# Or download Android Studio from developer.android.com

# 2. Create the project
mkdir RewardsNfcDisplay && cd RewardsNfcDisplay

# 3. Initialize Gradle project (or use Android Studio to create)
# Copy all files from the implementation plan above

# 4. Build debug APK
./gradlew assembleDebug

# 5. Install on device via USB
adb install app/build/outputs/apk/debug/app-debug.apk
```

### `app/build.gradle`

```groovy
plugins {
    id 'com.android.application'
}

android {
    namespace 'com.bitcoinrewards.nfcdisplay'
    compileSdk 34
    
    defaultConfig {
        applicationId "com.bitcoinrewards.nfcdisplay"
        minSdk 19
        targetSdk 34
        versionCode 1
        versionName "1.0"
    }
    
    buildTypes {
        release {
            minifyEnabled false
        }
    }
    
    compileOptions {
        sourceCompatibility JavaVersion.VERSION_17
        targetCompatibility JavaVersion.VERSION_17
    }
}

dependencies {
    implementation 'com.google.android.material:material:1.11.0'
}
```

### Configuration

Before building, edit `MainActivity.java` to set the correct display URL:

```java
private static final String DISPLAY_URL = "https://anmore.cash/plugins/bitcoin-rewards/YOUR_STORE_ID/display";
```

Or make it configurable via a settings screen / SharedPreferences (future enhancement).

### Deploy to Display Device

1. Enable Developer Options + USB Debugging on the Android tablet
2. Connect via USB, run `adb install`
3. Set the app as the default launcher (kiosk mode) or add to startup
4. Enable NFC in Android Settings
5. Ensure the app is set as the default HCE handler for the NDEF AID

### Testing Procedure

1. **Unit test NDEF message format:**
   - Create a test that calls `NdefMessageBuilder.createNdefUriMessage("lightning:lnurl1test...")` 
   - Verify the byte structure matches NDEF Type 4 URI record spec
   - Use NFC Tools app on a separate phone to verify the emulated tag is readable

2. **Integration test with BTCPay:**
   - Create a test reward via BTCPay admin
   - Verify the display page shows in the WebView
   - Verify the LNURL is extracted and set on HCE service (check logcat)
   - Tap a test phone → verify Lightning wallet opens with LNURL-w
   - Claim the reward → verify sats received

3. **Edge case tests:**
   - No reward → verify HCE returns error (no stale data)
   - Reward expires → verify HCE clears
   - New reward while old one displayed → verify HCE updates
   - Two phones tap → verify first wins, second gets error from wallet
   - App backgrounded → verify HCE still works (it should, HCE runs as a service)

4. **Wallet compatibility:**
   - Test with Phoenix (iOS + Android)
   - Test with Zeus
   - Test with Wallet of Satoshi
   - Verify the `lightning:` URI scheme triggers wallet on both platforms

---

## Summary: Effort Estimate

| Component | Effort | Dependencies |
|-----------|--------|-------------|
| Android app skeleton (WebView + Gradle) | 1 hour | Android SDK |
| HCE service (copy/simplify from Numo) | 2 hours | None |
| NdefMessageBuilder URI records | 30 min | None |
| MainActivity + JS bridge | 1 hour | None |
| Plugin change (1 HTML snippet) | 10 min | None |
| Testing & debugging | 2-3 hours | Android device with NFC |
| **Total** | **~7 hours** | |

### What's NOT Needed (confirmed)

- ❌ No new BTCPay API endpoints
- ❌ No backend code changes (except 1 HTML snippet)
- ❌ No external NFC hardware
- ❌ No Cashu/mint integration
- ❌ No write-back handling
- ❌ No NFC tag to purchase
- ❌ No ESP32 firmware

The Android device that already displays the QR code becomes the NFC tag. Customer taps their phone on the display device. That's it.
