#!/usr/bin/env python3
"""Static end-to-end safety checks for the Square-only BitcoinRewards plugin build.

These checks catch the BTCPay production crashes seen during refactor:
- compiled package must not ship removed controllers/services/views
- Razor views must not link to removed controllers/actions
- Square association + webhook + email pull-payment code paths must remain present
"""
from __future__ import annotations

import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PLUGIN = ROOT / "Plugins" / "BTCPayServer.Plugins.BitcoinRewards"
PACKAGE_VERSION = "1.7.8"
PACKAGE = ROOT / "output" / f"BTCPayServer.Plugins.BitcoinRewards-{PACKAGE_VERSION}-test-rewards.btcpay"

REMOVED_TOKENS = [
    "UIWalletManagement",
    "WalletApiController",
    "CustomerWalletService",
    "CustomerWallet",
    "WalletTransaction",
    "ErrorTrackingService",
    "ErrorDashboard",
    "RewardError",
    "AutoRecoveryWatchdog",
    "LnurlClaimWatcher",
    "LnurlpController",
    "Nip05",
    "BoltCardRewardService",
    "BoltCardRewardsController",
]

# Tokens allowed in source for compatibility settings/data objects, but not in views or package file names.
SOURCE_FORBIDDEN = [
    "asp-controller=\"UIWalletManagement\"",
    "asp-action=\"ErrorDashboard\"",
    "Manage Customer Wallets",
    "Customer Wallets</span>",
    "Bolt Card NFC Rewards",
    "Dual Balance Wallet Settings",
]

REQUIRED_SOURCE = {
    "scan_association_api": [
        "Controllers/RewardsAssociationController.cs",
        "SquareOrderId",
        "LightningAddress",
        "AssociateAsync",
    ],
    "square_settled_webhook": [
        "Controllers/SquareWebhookController.cs",
        "payment.updated",
        "status == \"COMPLETED\"",
        "BindPaymentAsync",
        "ProcessRewardAsync",
    ],
    "online_email_pull_payment": [
        "Services/BitcoinRewardsService.cs",
        "CreatePullPaymentAsync",
        "SendRewardNotificationAsync",
        "ClaimLink",
        "CustomerEmail",
    ],
    "direct_lightning_payout": [
        "Services/BitcoinRewardsService.cs",
        "RewardDeliveryMode.DirectLightning",
        "DirectLightningPayoutService",
        "direct_lightning_payout_queued",
    ],
    "customer_wallet_qr_check_in": [
        "Services/PendingLightningAddressCheckInService.cs",
        "NormalizeWalletQrPayload",
        "ConsumeNextForSquarePaymentAsync",
        "PendingLightningAddressCheckIns",
    ],
    "square_next_order_consumes_check_in": [
        "Controllers/SquareWebhookController.cs",
        "ConsumeNextForSquarePaymentAsync",
        "transaction.LightningAddress = checkIn.LightningAddress",
        "ProcessRewardAsync",
    ],
}


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(1)


def read_rel(rel: str) -> str:
    path = PLUGIN / rel
    if not path.exists():
        fail(f"missing required source file {rel}")
    return path.read_text(errors="replace")


def main() -> None:
    if not PACKAGE.exists():
        fail(f"package not found: {PACKAGE}")

    for path in list((PLUGIN / "Views").rglob("*.cshtml")) + [PLUGIN / "BTCPayServer.Plugins.BitcoinRewards.csproj"]:
        text = path.read_text(errors="replace")
        for token in SOURCE_FORBIDDEN:
            if token in text:
                fail(f"forbidden legacy token {token!r} in {path.relative_to(PLUGIN)}")

    for flow, checks in REQUIRED_SOURCE.items():
        source = read_rel(checks[0])
        for needle in checks[1:]:
            if needle not in source:
                fail(f"{flow} missing {needle!r} in {checks[0]}")

    settings = read_rel("ViewModels/BitcoinRewardsSettingsViewModel.cs")
    if "settings.DirectLightningPayoutEnabled = false" in settings:
        fail("direct payout is still forcibly disabled in settings conversion")
    direct = read_rel("Services/DirectLightningPayoutService.cs")
    for needle in ["Claim(claim)", "ParseClaimDestination", "PreApprove = true", "NonInteractiveOnly = true"]:
        if needle not in direct:
            fail(f"direct payout service missing {needle!r}")

    with zipfile.ZipFile(PACKAGE) as zf:
        names = zf.namelist()
        joined = "\n".join(names)
        for token in REMOVED_TOKENS:
            if re.search(re.escape(token), joined, re.IGNORECASE):
                fail(f"package ships removed surface matching {token!r}")
        manifest = zf.read("BTCPayServer.Plugins.BitcoinRewards.json").decode()
        if f'"Version": "{PACKAGE_VERSION}"' not in manifest:
            fail(f"package manifest is not version {PACKAGE_VERSION}")
        if not any(name == "BTCPayServer.Plugins.BitcoinRewards.dll" for name in names):
            fail("package does not include plugin assembly")

    print("PASS: Square-only package has no removed surface filenames or legacy Razor links")
    print("PASS: required flow entrypoints present: customer QR check-in, next Square order consumption, email pull-payment fallback")
    print("PASS: direct Lightning payout path is present and configurable; online fallback remains present")


if __name__ == "__main__":
    main()
