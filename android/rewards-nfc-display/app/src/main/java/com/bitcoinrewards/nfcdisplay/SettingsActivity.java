package com.bitcoinrewards.nfcdisplay;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;
import android.content.Intent;
import android.os.AsyncTask;
import android.util.Log;

import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.io.BufferedReader;
import java.io.InputStreamReader;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * Settings/Onboarding screen for configuring the BTCPay rewards display.
 * 
 * Authentication flow:
 * 1. User enters BTCPay URL + email + password
 * 2. App creates a Greenfield API key via POST /api/v1/api-keys 
 *    (using Basic auth with email:password)
 * 3. API key is stored in SharedPreferences
 * 4. App fetches available stores via GET /api/v1/stores
 * 5. User selects store (or auto-selects if only one)
 * 6. WebView loads display page using cookie session from login
 * 
 * The display page requires CanViewStoreSettings policy — 
 * this is NOT publicly accessible. Authentication is required.
 */
public class SettingsActivity extends Activity {
    private static final String TAG = "RewardsSettings";
    public static final String PREFS_NAME = "RewardsNfcPrefs";
    public static final String KEY_BTCPAY_URL = "btcpay_url";
    public static final String KEY_STORE_ID = "store_id";
    public static final String KEY_STORE_NAME = "store_name";
    public static final String KEY_API_KEY = "api_key";
    public static final String KEY_EMAIL = "email";
    public static final String KEY_PASSWORD = "password";
    public static final String KEY_REFRESH_SECONDS = "refresh_seconds";
    public static final String KEY_NFC_ENABLED = "nfc_enabled";
    public static final String KEY_ONBOARDED = "onboarded";

    private EditText inputUrl;
    private EditText inputEmail;
    private EditText inputPassword;
    private EditText inputRefreshSeconds;
    private Switch switchNfc;
    private Button btnConnect;
    private TextView statusText;
    private View storeSection;
    private TextView storeInfo;
    private Button btnSave;

    // Stores fetched from API
    private String apiKey = null;
    private String selectedStoreId = null;
    private String selectedStoreName = null;
    private JSONArray availableStores = null;
    private int currentStoreIndex = 0;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_settings);

        inputUrl = findViewById(R.id.input_btcpay_url);
        inputEmail = findViewById(R.id.input_email);
        inputPassword = findViewById(R.id.input_password);
        inputRefreshSeconds = findViewById(R.id.input_refresh_seconds);
        switchNfc = findViewById(R.id.switch_nfc_enabled);
        btnConnect = findViewById(R.id.btn_connect);
        statusText = findViewById(R.id.status_text);
        storeSection = findViewById(R.id.store_section);
        storeInfo = findViewById(R.id.store_info);
        btnSave = findViewById(R.id.btn_save);
        Button btnCycleStore = findViewById(R.id.btn_cycle_store);

        // Load existing settings
        SharedPreferences prefs = getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        inputUrl.setText(prefs.getString(KEY_BTCPAY_URL, ""));
        inputEmail.setText(prefs.getString(KEY_EMAIL, ""));
        inputRefreshSeconds.setText(String.valueOf(prefs.getInt(KEY_REFRESH_SECONDS, 10)));
        switchNfc.setChecked(prefs.getBoolean(KEY_NFC_ENABLED, true));

        // If already configured, show store info
        String existingStoreId = prefs.getString(KEY_STORE_ID, "");
        String existingStoreName = prefs.getString(KEY_STORE_NAME, "");
        String existingApiKey = prefs.getString(KEY_API_KEY, "");
        if (!existingStoreId.isEmpty() && !existingApiKey.isEmpty()) {
            apiKey = existingApiKey;
            selectedStoreId = existingStoreId;
            selectedStoreName = existingStoreName;
            storeSection.setVisibility(View.VISIBLE);
            storeInfo.setText("✅ " + (existingStoreName.isEmpty() ? existingStoreId : existingStoreName));
            btnSave.setVisibility(View.VISIBLE);
        }

        btnConnect.setOnClickListener(v -> connectToBtcPay());
        btnSave.setOnClickListener(v -> saveAndLaunch());
        if (btnCycleStore != null) {
            btnCycleStore.setOnClickListener(v -> cycleStore());
        }
    }

    private void connectToBtcPay() {
        String url = inputUrl.getText().toString().trim();
        String email = inputEmail.getText().toString().trim();
        String password = inputPassword.getText().toString().trim();

        if (url.isEmpty()) {
            inputUrl.setError("Required");
            return;
        }
        if (email.isEmpty()) {
            inputEmail.setError("Required");
            return;
        }
        if (password.isEmpty()) {
            inputPassword.setError("Required");
            return;
        }

        // Clean URL
        if (!url.startsWith("http://") && !url.startsWith("https://")) {
            url = "https://" + url;
        }
        if (url.endsWith("/")) {
            url = url.substring(0, url.length() - 1);
        }

        btnConnect.setEnabled(false);
        btnConnect.setText("Connecting...");
        statusText.setText("🔄 Creating API key...");
        statusText.setVisibility(View.VISIBLE);

        final String finalUrl = url;
        new ConnectTask().execute(finalUrl, email, password);
    }

    private class ConnectTask extends AsyncTask<String, String, String> {
        private String serverUrl;
        private String error;

        @Override
        protected String doInBackground(String... params) {
            serverUrl = params[0];
            String email = params[1];
            String password = params[2];

            try {
                // Step 1: Create API key via Basic auth
                publishProgress("Creating API key...");
                String credentials = android.util.Base64.encodeToString(
                    (email + ":" + password).getBytes("UTF-8"),
                    android.util.Base64.NO_WRAP
                );

                URL apiKeyUrl = new URL(serverUrl + "/api/v1/api-keys");
                HttpURLConnection conn = (HttpURLConnection) apiKeyUrl.openConnection();
                conn.setRequestMethod("POST");
                conn.setRequestProperty("Authorization", "Basic " + credentials);
                conn.setRequestProperty("Content-Type", "application/json");
                conn.setDoOutput(true);

                String body = "{\"label\":\"Rewards NFC Display\",\"permissions\":[\"btcpay.store.canviewstoresettings\"]}";
                OutputStream os = conn.getOutputStream();
                os.write(body.getBytes("UTF-8"));
                os.flush();
                os.close();

                int code = conn.getResponseCode();
                if (code != 200) {
                    BufferedReader errReader = new BufferedReader(new InputStreamReader(conn.getErrorStream()));
                    StringBuilder errBody = new StringBuilder();
                    String line;
                    while ((line = errReader.readLine()) != null) errBody.append(line);
                    errReader.close();

                    if (code == 401) {
                        error = "Invalid email or password";
                    } else {
                        error = "API error (" + code + "): " + errBody.toString();
                    }
                    return null;
                }

                BufferedReader reader = new BufferedReader(new InputStreamReader(conn.getInputStream()));
                StringBuilder responseBody = new StringBuilder();
                String responseLine;
                while ((responseLine = reader.readLine()) != null) responseBody.append(responseLine);
                reader.close();

                JSONObject keyResponse = new JSONObject(responseBody.toString());
                String newApiKey = keyResponse.getString("apiKey");

                // Step 2: Fetch stores
                publishProgress("Fetching stores...");
                URL storesUrl = new URL(serverUrl + "/api/v1/stores");
                HttpURLConnection storesConn = (HttpURLConnection) storesUrl.openConnection();
                storesConn.setRequestProperty("Authorization", "token " + newApiKey);

                int storesCode = storesConn.getResponseCode();
                if (storesCode != 200) {
                    error = "Failed to fetch stores (code " + storesCode + ")";
                    return null;
                }

                BufferedReader storesReader = new BufferedReader(new InputStreamReader(storesConn.getInputStream()));
                StringBuilder storesBody = new StringBuilder();
                String storesLine;
                while ((storesLine = storesReader.readLine()) != null) storesBody.append(storesLine);
                storesReader.close();

                JSONArray stores = new JSONArray(storesBody.toString());
                if (stores.length() == 0) {
                    error = "No stores found for this account";
                    return null;
                }

                // Store results
                apiKey = newApiKey;
                availableStores = stores;
                currentStoreIndex = 0;

                JSONObject firstStore = stores.getJSONObject(0);
                selectedStoreId = firstStore.getString("id");
                selectedStoreName = firstStore.optString("name", selectedStoreId);

                return "OK";

            } catch (Exception e) {
                error = "Connection failed: " + e.getMessage();
                Log.e(TAG, "Connect error", e);
                return null;
            }
        }

        @Override
        protected void onProgressUpdate(String... values) {
            statusText.setText("🔄 " + values[0]);
        }

        @Override
        protected void onPostExecute(String result) {
            btnConnect.setEnabled(true);
            btnConnect.setText("Connect");

            if (result != null) {
                statusText.setText("✅ Connected! " + availableStores.length() + " store(s) found");
                storeSection.setVisibility(View.VISIBLE);
                updateStoreDisplay();
                btnSave.setVisibility(View.VISIBLE);
            } else {
                statusText.setText("❌ " + error);
                storeSection.setVisibility(View.GONE);
                btnSave.setVisibility(View.GONE);
            }
        }
    }

    private void updateStoreDisplay() {
        if (availableStores != null && availableStores.length() > 0) {
            String display = selectedStoreName;
            if (availableStores.length() > 1) {
                display += " (" + (currentStoreIndex + 1) + "/" + availableStores.length() + ")";
            }
            storeInfo.setText("🏪 " + display);
        }
    }

    private void cycleStore() {
        if (availableStores == null || availableStores.length() <= 1) return;
        currentStoreIndex = (currentStoreIndex + 1) % availableStores.length();
        try {
            JSONObject store = availableStores.getJSONObject(currentStoreIndex);
            selectedStoreId = store.getString("id");
            selectedStoreName = store.optString("name", selectedStoreId);
            updateStoreDisplay();
        } catch (Exception e) {
            Log.e(TAG, "Error cycling store", e);
        }
    }

    private void saveAndLaunch() {
        String url = inputUrl.getText().toString().trim();
        if (!url.startsWith("http://") && !url.startsWith("https://")) {
            url = "https://" + url;
        }
        if (url.endsWith("/")) {
            url = url.substring(0, url.length() - 1);
        }

        String refreshStr = inputRefreshSeconds.getText().toString().trim();
        int refreshSeconds = 10;
        try {
            refreshSeconds = Integer.parseInt(refreshStr);
            if (refreshSeconds < 3) refreshSeconds = 3;
            if (refreshSeconds > 300) refreshSeconds = 300;
        } catch (NumberFormatException ignored) {}

        // Save everything
        SharedPreferences.Editor editor = getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit();
        editor.putString(KEY_BTCPAY_URL, url);
        editor.putString(KEY_EMAIL, inputEmail.getText().toString().trim());
        editor.putString(KEY_PASSWORD, inputPassword.getText().toString());
        editor.putString(KEY_STORE_ID, selectedStoreId);
        editor.putString(KEY_STORE_NAME, selectedStoreName);
        editor.putString(KEY_API_KEY, apiKey);
        editor.putInt(KEY_REFRESH_SECONDS, refreshSeconds);
        editor.putBoolean(KEY_NFC_ENABLED, switchNfc.isChecked());
        editor.putBoolean(KEY_ONBOARDED, true);
        editor.apply();

        Toast.makeText(this, "Settings saved!", Toast.LENGTH_SHORT).show();

        Intent intent = new Intent(this, MainActivity.class);
        intent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        startActivity(intent);
        finish();
    }

    public static boolean isOnboarded(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        return prefs.getBoolean(KEY_ONBOARDED, false);
    }

    public static String getDisplayUrl(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        String url = prefs.getString(KEY_BTCPAY_URL, "");
        String storeId = prefs.getString(KEY_STORE_ID, "");
        if (url.isEmpty() || storeId.isEmpty()) return null;
        return url + "/plugins/bitcoin-rewards/" + storeId + "/display";
    }

    public static String getLoginUrl(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        return prefs.getString(KEY_BTCPAY_URL, "") + "/login";
    }

    public static String getApiKey(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        return prefs.getString(KEY_API_KEY, "");
    }

    public static String getEmail(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        return prefs.getString(KEY_EMAIL, "");
    }

    public static boolean isNfcEnabled(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        return prefs.getBoolean(KEY_NFC_ENABLED, true);
    }
}
