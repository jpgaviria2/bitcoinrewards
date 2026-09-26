package com.bitcoinrewards.nfcdisplay;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Vibrator;
import android.widget.Button;
import android.widget.TextView;
import android.widget.Toast;

import com.journeyapps.barcodescanner.BarcodeCallback;
import com.journeyapps.barcodescanner.BarcodeResult;
import com.journeyapps.barcodescanner.DecoratedBarcodeView;

/**
 * Native camera scanner for BTCPay login-code QR payloads.
 *
 * BTCPay QR payloads may be just the login code, a login URL, or
 * "code;baseUrl;email". SettingsActivity normalizes the payload.
 */
public class LoginCodeScanActivity extends Activity {
    public static final String EXTRA_LOGIN_CODE_PAYLOAD = "login_code_payload";
    private static final int CAMERA_PERMISSION_REQUEST = 4301;

    private DecoratedBarcodeView barcodeView;
    private TextView statusText;
    private boolean handled = false;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_login_code_scan);

        barcodeView = findViewById(R.id.login_barcode_scanner);
        statusText = findViewById(R.id.login_scan_status);
        Button close = findViewById(R.id.btn_close_login_scan);
        if (close != null) {
            close.setOnClickListener(v -> {
                setResult(RESULT_CANCELED);
                finish();
            });
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
            setStatus("Camera permission is required to scan the BTCPay login QR.");
            Toast.makeText(this, "Camera permission required", Toast.LENGTH_LONG).show();
        }
    }

    private void startScanning() {
        handled = false;
        setStatus("Point camera at the BTCPay login QR.");
        barcodeView.decodeContinuous(callback);
        barcodeView.resume();
    }

    private final BarcodeCallback callback = new BarcodeCallback() {
        @Override
        public void barcodeResult(BarcodeResult result) {
            if (handled || result == null || result.getText() == null) return;
            String payload = result.getText().trim();
            if (payload.isEmpty()) return;
            handled = true;
            barcodeView.pause();
            vibrate();
            Intent data = new Intent();
            data.putExtra(EXTRA_LOGIN_CODE_PAYLOAD, payload);
            setResult(RESULT_OK, data);
            finish();
        }
    };

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
        if (barcodeView != null && !handled) barcodeView.resume();
    }

    @Override
    protected void onPause() {
        if (barcodeView != null) barcodeView.pause();
        super.onPause();
    }
}
