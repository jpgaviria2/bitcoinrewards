package com.bitcoinrewards.nfcdisplay.ndef;

import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.nfc.NfcAdapter;
import android.nfc.cardemulation.HostApduService;
import android.os.Bundle;
import android.util.Log;

/**
 * HCE service for broadcasting LNURL-withdraw via NFC NDEF Type 4 Tag emulation.
 * 
 * Android only creates this service when a phone taps the device.
 * The LNURL payload is stored in a static field so it's available
 * before the service is instantiated.
 */
public class NdefHostCardEmulationService extends HostApduService {
    private static final String TAG = "NdefHCEService";
    private static final byte[] STATUS_FAILED = {(byte) 0x6F, (byte) 0x00};

    private NdefProcessor ndefProcessor;
    private static NdefHostCardEmulationService instance;
    private boolean tapNotified = false;

    // Static payload — set by MainActivity BEFORE service exists
    // When a phone taps and Android creates the service, 
    // onCreate() picks up this payload
    private static String pendingPayload = "";
    private static NfcTapListener staticTapListener;

    public interface NfcTapListener {
        void onNfcTapDetected();
    }

    public static NdefHostCardEmulationService getInstance() {
        return instance;
    }

    /**
     * Set the LNURL payload to broadcast via NFC.
     * Can be called before the service exists — stored statically.
     */
    public static void setPayload(String uri) {
        pendingPayload = (uri != null) ? uri : "";
        Log.i(TAG, "Static payload set: " + (uri != null ? uri.substring(0, Math.min(40, uri.length())) + "..." : "empty"));
        
        // If service already running, update it immediately
        if (instance != null && instance.ndefProcessor != null) {
            instance.ndefProcessor.setMessageToSend(pendingPayload);
            Log.i(TAG, "Updated running service with new payload");
        }
    }

    /**
     * Clear the NFC payload.
     */
    public static void clearPayload() {
        pendingPayload = "";
        if (instance != null && instance.ndefProcessor != null) {
            instance.ndefProcessor.setMessageToSend("");
        }
        Log.i(TAG, "Payload cleared");
    }

    /**
     * Check if a payload is set (even before service exists).
     */
    public static boolean hasPayload() {
        return pendingPayload != null && !pendingPayload.isEmpty();
    }

    /**
     * Set tap listener (static so it works before service creation).
     */
    public static void setStaticTapListener(NfcTapListener listener) {
        staticTapListener = listener;
        if (instance != null) {
            instance.tapListener = listener;
        }
    }

    private NfcTapListener tapListener;

    @Override
    public void onCreate() {
        super.onCreate();
        ndefProcessor = new NdefProcessor();
        instance = this;
        tapListener = staticTapListener;

        // Load any pending payload that was set before service existed
        if (pendingPayload != null && !pendingPayload.isEmpty()) {
            ndefProcessor.setMessageToSend(pendingPayload);
            Log.i(TAG, "HCE Service created — loaded pending payload: " + 
                pendingPayload.substring(0, Math.min(40, pendingPayload.length())) + "...");
        } else {
            Log.i(TAG, "HCE Service created — no payload pending");
        }
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
            // Notify tap listener
            if (!tapNotified) {
                tapNotified = true;
                if (tapListener != null) {
                    tapListener.onNfcTapDetected();
                } else if (staticTapListener != null) {
                    staticTapListener.onNfcTapDetected();
                }
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
        tapNotified = false;
        Log.i(TAG, "HCE deactivated, reason: " + reason);
    }

    // Legacy instance methods — delegate to static
    public void setPaymentRequest(String uri) {
        setPayload(uri);
    }

    public void clearPaymentRequest() {
        clearPayload();
    }

    public void setTapListener(NfcTapListener listener) {
        this.tapListener = listener;
        staticTapListener = listener;
    }

    public static boolean isHceAvailable(Context context) {
        try {
            NfcAdapter adapter = NfcAdapter.getDefaultAdapter(context);
            return adapter != null && adapter.isEnabled() &&
                    context.getPackageManager().hasSystemFeature(
                            PackageManager.FEATURE_NFC_HOST_CARD_EMULATION);
        } catch (Exception e) {
            return false;
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        return START_STICKY;
    }
}
