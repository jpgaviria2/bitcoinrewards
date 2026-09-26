# Trails iOS Rewards — Bitcoin Rewards Part B

## Status

Version 1.6.0 is an **unreleased, default-off foundation**. It aligns the Bitcoin Rewards plugin with the signed customer profiles introduced by the Example Merchant iOS app without changing the authoritative reward-delivery path.

The existing Bitcoin Rewards plugin and LNURL-withdraw pull-payment flow remain authoritative. Direct Lightning payout is intentionally unavailable in this release.

## Delivered contract

1. An authenticated store operator associates one exact Square order ID with one app-managed Lightning address.
2. The plugin resolves that address through the Trails profile API using a server-to-server bearer credential.
3. The profile API accepts only canonical `user@pay.example.com` addresses and returns an exact signed-profile match.
4. The plugin stores the profile ID and a keyed address hash, never the plaintext Lightning address.
5. A completed Square webhook binds the association to the exact Square payment ID carried by that order.
6. The existing reward service creates the legacy pull payment. Only after successful reward processing is the association marked consumed.

There is no “next payment” or timing-window customer match.

## Data model

- `CustomerOrderAssociations` has unique `(StoreId, SquareOrderId)` and unique non-null `(StoreId, SquarePaymentId)` constraints.
- `RewardPayoutAttempts` has a unique `(RewardId, AttemptNumber)` constraint and durable retry/reconciliation fields for a later dispatcher.
- Reward records add profile ID, keyed address hash, delivery mode, and optional direct-payout state.
- The migration is additive and reversible; existing reward rows default to `LegacyPullPayment`.

## Rollout controls

- `CustomerProfileAssociationEnabled`: default `false`.
- `DirectLightningPayoutEnabled`: default `false` and forcibly reset to `false` by the current settings UI.
- `LegacyPullPaymentFallbackEnabled`: default `true` and required whenever profile association is enabled.
- Profile API origin is pinned to `https://profiles.example.com`.
- The server-to-server token is write-only in the settings form and must be entered locally; it must never be committed or pasted into support channels.

## Required production inventory before deployment

Do not install version 1.6.0 until all of the following are captured from the deployed BTCPay host:

- BTCPay Server version and container/image digest.
- Installed Bitcoin Rewards plugin version plus SHA-256 of the deployed plugin package/assembly.
- PostgreSQL version, plugin schema size, migration history, row counts, and integrity/backup result.
- Current store settings with credentials redacted.
- Current plugin package and database backup stored outside the live container.
- Tested commands for restoring both the prior plugin bytes and pre-migration database.

The deployed host was not reachable from the development Mac during this implementation, so this inventory is still a hard blocker.

## Acceptance gates

Before enabling profile association:

- Apply and reverse the migration against a private copy of the deployed database.
- Prove duplicate order association and duplicate Square webhook delivery create only one reward.
- Prove a Square order cannot be rebound to another profile or payment.
- Prove disabled association leaves legacy rewards unchanged.
- Prove API outage/401/404/409 cases fail closed without losing a completed Square payment.
- Run a physical Square register checkout that captures the exact order ID before payment completion.
- Resolve the pinned BTCPay dependency advisories or document an independently reviewed exception.
- Ship the iOS replacement for build 143 so the Home Rewards QR never falls back to a Nostr `npub`.

Before enabling direct payout:

- Implement the payout dispatcher and reconciliation worker.
- Prove invoice creation, payment, retry, duplicate callback, timeout, partial failure, and restart recovery.
- Prove the unique attempt constraint prevents duplicate sats under concurrent replay.
- Rehearse rollback while retaining legacy pull-payment fallback for at least one production release.

## Rollback

1. Disable `CustomerProfileAssociationEnabled`; legacy reward processing continues.
2. Keep `DirectLightningPayoutEnabled=false`.
3. If code rollback is required, restore the captured prior plugin package and restart BTCPay.
4. If database rollback is required, stop BTCPay, restore the verified pre-migration PostgreSQL backup, restore prior plugin bytes, then restart and run legacy reward smoke tests.
5. Never run the migration `Down` method against production as a substitute for a verified backup restore after Part B data has been written.

## Current verification

- Plugin suite: 158/158 passing.
- Trails API suite: 103/103 passing.
- Plugin Release build: passing.
- Disposable PostgreSQL 16 rehearsal: full migration apply, rollback, and reapply passing.
- Known upstream advisories remain in the pinned BTCPay submodule (`SSH.NET`, `MailKit`, and `HtmlSanitizer`) and block production approval pending upgrade or review.
