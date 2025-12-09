## Handoff Notes

### Build & Package
- From repo root: `dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj -c Release`
- Output DLL: `Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll`
- Package for BTCPay: copy/rename to `.btcpay`  
  `cp .../BTCPayServer.Plugins.BitcoinRewards.dll .../BTCPayServer.Plugins.BitcoinRewards.btcpay`

### Deploy to local BTCPay (docker-compose)
- Container name used here: `generated_btcpayserver_1`
- Copy plugin:  
  `docker cp Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.btcpay generated_btcpayserver_1:/datadir/plugins/`
- Restart BTCPay: `docker restart generated_btcpayserver_1`

### Logs & Troubleshooting
- Tail BTCPay logs: `docker logs -f generated_btcpayserver_1`
- Quick recent view: `docker logs --tail 200 generated_btcpayserver_1`
- Reward flow notes:
  - Square webhook amount is treated as **major units** (no /100). 
  - Minimum transaction check now converts webhook currency into store currency using CoinGecko; set minimum to 0/blank to allow tiny orders.
  - Email template override tokens: `{ORDER_ID}`, `{AMOUNT_BTC}`, `{AMOUNT_SATS}`, `{CLAIM_LINK}`, `{LNURL}`.
  - Secrets (Square token/webhook key) stay hidden in UI; "Saved (hidden)" shows when present.
  - Shopify remains disabled in UI and server-side.

### Implement/Fix Workflow
- Edit code, then build (`dotnet build ... -c Release`).
- Repackage `.btcpay`, copy into container, restart BTCPay.
- Retest by triggering a Square payment; watch logs for warnings like min-amount blocks or duplicate transaction notices.

### Git: Commit & Tag (SSH)
- Check status: `git status`
- Stage: `git add .`
- Commit (example):  
  `git commit -m "Handle Square major-unit amounts and email template override"`
- Tag next version (next available after v1.1.11 is v1.1.12):  
  `git tag v1.1.12`
- Push via SSH (origin is HTTPS; use full SSH URL):  
  `git push git@github.com:jpgaviria2/bitcoinrewards.git main`  
  `git push git@github.com:jpgaviria2/bitcoinrewards.git v1.1.12`
