package com.bitcoinrewards.nfcdisplay;

import android.webkit.JavascriptInterface;

import com.bitcoinrewards.nfcdisplay.ndef.NdefHostCardEmulationService;

/**
 * JavaScript interface exposed to the WebView as window.AndroidBridge.
 * Allows the BTCPay display page to control the HCE NFC service.
 */
public class AndroidBridge {
    private final MainActivity activity;

    public AndroidBridge(MainActivity activity) {
        this.activity = activity;
    }

    @JavascriptInterface
    public void setNfcPayload(String lnurl) {
        activity.setNfcPayload(lnurl);
    }

    @JavascriptInterface
    public void clearNfcPayload() {
        activity.clearNfcPayload();
    }

    @JavascriptInterface
    public boolean isNfcAvailable() {
        return NdefHostCardEmulationService.isHceAvailable(activity);
    }

    @JavascriptInterface
    public boolean isReaderModeAvailable() {
        android.nfc.NfcAdapter adapter = android.nfc.NfcAdapter.getDefaultAdapter(activity);
        return adapter != null && adapter.isEnabled();
    }
}
