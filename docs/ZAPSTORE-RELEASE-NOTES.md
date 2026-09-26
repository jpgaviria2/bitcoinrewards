# Rewards NFC Display v1.11.3

Production/Zapstore readiness update with persistent release signing for future APK updates.

- Added production launcher icon and round icon.
- Added Zapstore metadata/config and store-ready promotional assets.
- Set Android app to disallow cleartext HTTP traffic.
- Added Scan BTCPay Login QR onboarding in APK settings.
- Login QR scanner accepts raw login codes, `/login/code?loginCode=...` URLs, and `code;baseUrl;email` QR payloads.
- Password login fallback now points users to login QR when BTCPay rejects Basic auth, such as with 2FA/passkeys enabled.
- Preserved native customer rewards-profile QR scanning.
- Preserved open-source cleanup with no merchant-specific defaults.
