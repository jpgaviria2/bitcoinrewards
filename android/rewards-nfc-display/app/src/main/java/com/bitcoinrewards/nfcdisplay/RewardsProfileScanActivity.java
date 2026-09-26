package com.bitcoinrewards.nfcdisplay;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageManager;
import android.os.AsyncTask;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Build;
import android.os.Vibrator;
import android.util.Log;
import android.widget.Button;
import android.widget.TextView;
import android.widget.Toast;

import com.journeyapps.barcodescanner.BarcodeCallback;
import com.journeyapps.barcodescanner.BarcodeResult;
import com.journeyapps.barcodescanner.DecoratedBarcodeView;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.regex.Pattern;

/**
 * Native camera scanner for customer rewards profile QR codes.
 *
 * The QR payload should be a Lightning address, optionally prefixed with lightning:.
 * The app posts it directly to the BTCPay plugin check-in API so the next Square order
 * can consume it without using browser camera APIs in WebView/Chrome.
 */
public class RewardsProfileScanActivity extends Activity {
    private static final String TAG = "RewardsProfileScan";
    private static final int CAMERA_PERMISSION_REQUEST = 4201;
    private static final Pattern LIGHTNING_ADDRESS = Pattern.compile(
        "^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?@[a-z0-9.-]+\\.[a-z]{2,}$"
    );

    private DecoratedBarcodeView barcodeView;
    private TextView statusText;
    private boolean submitted = false;
    private final Handler handler = new Handler(Looper.getMainLooper());

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_rewards_profile_scan);

        barcodeView = findViewById(R.id.barcode_scanner);
        statusText = findViewById(R.id.scan_status);
        Button close = findViewById(R.id.btn_close_scan);
        if (close != null) {
            close.setOnClickListener(v -> finish());
        }

        String apiUrl = SettingsActivity.getCheckInApiUrl(this);
        if (apiUrl == null || apiUrl.isEmpty()) {
            setStatus("BTCPay store is not configured. Open settings first.");
            Toast.makeText(this, "BTCPay store is not configured", Toast.LENGTH_LONG).show();
            return;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[] { Manifest.permission.CAMERA }, CAMERA_PERMISSION_REQUEST);
        } else {
            startScanning();
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == CAMERA_PERMISSION_REQUEST && grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
            startScanning();
        } else {
            setStatus("Camera permission is required to scan rewards profile QR codes.");
            Toast.makeText(this, "Camera permission required", Toast.LENGTH_LONG).show();
        }
    }

    private void startScanning() {
        submitted = false;
        setStatus("Point the camera at the customer's wallet QR. It must contain a Lightning address.");
        barcodeView.decodeContinuous(callback);
        barcodeView.resume();
    }

    private final BarcodeCallback callback = new BarcodeCallback() {
        @Override
        public void barcodeResult(BarcodeResult result) {
            if (submitted || result == null || result.getText() == null) return;
            String normalized;
            try {
                normalized = normalizeLightningAddress(result.getText());
            } catch (IllegalArgumentException e) {
                setStatus(e.getMessage());
                return;
            }

            submitted = true;
            barcodeView.pause();
            vibrate();
            setStatus("Saving rewards profile...");
            new SubmitCheckInTask().execute(normalized);
        }
    };

    private static String normalizeLightningAddress(String raw) {
        String value = raw == null ? "" : raw.trim();
        if (value.toLowerCase().startsWith("lightning:")) {
            value = value.substring("lightning:".length()).trim();
        }
        value = value.toLowerCase();
        if (value.startsWith("lnbc") || value.startsWith("lnurl") || value.startsWith("http://") ||
            value.startsWith("https://") || value.startsWith("npub") || value.startsWith("nostr:")) {
            throw new IllegalArgumentException("Scan the wallet Lightning address QR, not an invoice, LNURL, link, or Nostr key.");
        }
        if (value.length() > 128 || !LIGHTNING_ADDRESS.matcher(value).matches()) {
            throw new IllegalArgumentException("QR must contain a Lightning address like user@example.com.");
        }
        return value;
    }

    private class SubmitCheckInTask extends AsyncTask<String, Void, Boolean> {
        private String error = null;
        private String lightningAddress = null;

        @Override
        protected Boolean doInBackground(String... params) {
            lightningAddress = params[0];
            try {
                String apiUrl = SettingsActivity.getCheckInApiUrl(RewardsProfileScanActivity.this);
                JSONObject body = new JSONObject();
                body.put("lightningAddress", lightningAddress);
                body.put("deviceId", "rewards-nfc-display");

                HttpURLConnection conn = (HttpURLConnection) new URL(apiUrl).openConnection();
                conn.setRequestMethod("POST");
                conn.setRequestProperty("Content-Type", "application/json");
                conn.setRequestProperty("Accept", "application/json");
                conn.setConnectTimeout(10000);
                conn.setReadTimeout(15000);
                conn.setDoOutput(true);

                OutputStream os = conn.getOutputStream();
                os.write(body.toString().getBytes("UTF-8"));
                os.flush();
                os.close();

                int code = conn.getResponseCode();
                if (code >= 200 && code < 300) return true;

                BufferedReader reader = new BufferedReader(new InputStreamReader(
                    code >= 400 && conn.getErrorStream() != null ? conn.getErrorStream() : conn.getInputStream()
                ));
                StringBuilder response = new StringBuilder();
                String line;
                while ((line = reader.readLine()) != null) response.append(line);
                reader.close();
                error = "BTCPay rejected check-in (" + code + "): " + response;
                return false;
            } catch (Exception e) {
                Log.e(TAG, "Failed to submit rewards profile check-in", e);
                error = e.getMessage();
                return false;
            }
        }

        @Override
        protected void onPostExecute(Boolean ok) {
            if (ok) {
                setStatus("✅ Rewards profile saved. The next Square order will use " + lightningAddress + ".");
                Toast.makeText(RewardsProfileScanActivity.this, "Rewards profile saved", Toast.LENGTH_LONG).show();
                handler.postDelayed(() -> finish(), 1800);
            } else {
                submitted = false;
                setStatus("Could not save rewards profile: " + error);
                Toast.makeText(RewardsProfileScanActivity.this, "Check-in failed", Toast.LENGTH_LONG).show();
                handler.postDelayed(() -> {
                    if (!submitted) barcodeView.resume();
                }, 1200);
            }
        }
    }

    private void setStatus(String message) {
        if (statusText != null) statusText.setText(message);
    }

    private void vibrate() {
        try {
            Vibrator vibrator = (Vibrator) getSystemService(VIBRATOR_SERVICE);
            if (vibrator != null) vibrator.vibrate(80);
        } catch (Exception ignored) { }
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (barcodeView != null && !submitted) barcodeView.resume();
    }

    @Override
    protected void onPause() {
        if (barcodeView != null) barcodeView.pause();
        super.onPause();
    }
}
