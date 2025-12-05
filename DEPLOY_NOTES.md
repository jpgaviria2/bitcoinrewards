# BitcoinRewards plugin – Cashu removal and payout-agnostic build (2025-12-04)

## Summary of changes
- Removed all Cashu-specific code, handlers, processors, services, controllers, views, and data models from BitcoinRewards.
- DbContext now only includes reward tables; migration `20251204000000_RemoveCashuTables` drops Proofs/Mints/MintKeys/FailedTransactions/ExportedTokens.
- Reward flow is pull-payment-only; no token mint/reclaim paths; UI/settings/history are payout-agnostic.
- Build artifact rebuilt without Cashu: `BTCPayServer.Plugins.BitcoinRewards.btcpay` (timestamp ~2025-12-04 15:48, size ~1,956,250 bytes).

## Server deployment steps
1) Copy `BTCPayServer.Plugins.BitcoinRewards.btcpay` to the BTCPay plugins directory on the server.
2) Remove/disable any Cashu plugins (e.g., btcnutserver-test) to avoid CASHU handler conflicts.
3) Restart BTCPay Server to load the plugin and apply migrations.
4) Clean payout processors:
   ```
   DELETE FROM "PayoutProcessors" WHERE "Processor"='CashuAutomatedPayoutSenderFactory';
   ```
   Restart BTCPay after this delete.
5) Verify schema/tables (PostgreSQL):
   ```
   SELECT migrationid FROM "BTCPayServer.Plugins.BitcoinRewards"."__EFMigrationsHistory" ORDER BY migrationid;
   SELECT table_name FROM information_schema.tables WHERE table_schema='BTCPayServer.Plugins.BitcoinRewards';
   ```
   Expected tables: `BitcoinRewardRecords`, `RewardsConfigs`, `RewardIssues`, `RewardFundingTxs`.
6) Validate payout processors: only the configured non-Cashu processors (e.g., Lightning/Ark) should appear.
7) Test a reward: create a test reward and confirm it creates a pull payment + claim link; no Cashu wallet/token should appear.

## Notes
- If Cashu still appears in payout processors, another plugin is registering it. Remove that plugin and restart.
- Ensure migrations run on first start after deploying the new build.

