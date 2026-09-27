# Security Policy

## Supported scope

`main` contains the open-source BTCPay Server plugin source. Store-specific deployment configuration, signing material, generated plugin archives, Android app releases, Zapstore keys, BTCPay/Square credentials, LND macaroons, and production overrides are out of scope and must not be committed.

The Android kiosk app lives in the separate `RewardsApp` repository.

## Reporting a vulnerability

Please open a private GitHub security advisory or contact the repository maintainer privately. Do not publish exploit details until a fix is available.

Include:

- Affected commit/version
- Reproduction steps
- Expected vs actual behavior
- Whether funds, credentials, or customer data can be affected
- Logs with secrets redacted

## Secret handling

Never commit:

- Square access tokens or webhook signing keys
- BTCPay admin/API keys
- LND macaroons, TLS keys, or connection strings with credentials
- Wallet seed phrases or recovery material
- Android signing keystores/passwords
- Nostr private keys (`nsec...`)
- Generated `.btcpay`, APK, AAB, or release artifacts

The repository ignores common secret/build locations, including `.secrets/`, keystores, Android build output, plugin package output, and local Hermes state.

## Operational hardening

- Keep BTCPay Server and this plugin updated.
- Use HTTPS with a trusted certificate for all public BTCPay domains.
- Configure Square webhook signature validation before accepting production webhooks.
- Use least-privilege API keys and rotate them regularly.
- Use BTCPay store authorization for all admin/test actions.
- Keep LND admin interfaces private unless intentionally exposed and protected.
- Treat direct Lightning test payouts as real payments; use low amounts and confirm state in BTCPay payout history.
- Review plugin logs after upgrades for plugin load, migration, and payout errors.

## Current dependency review notes

This plugin references BTCPay Server and Lightning libraries, which bring a large transitive dependency graph. Run this before releases:

```bash
dotnet list Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj package --vulnerable --include-transitive
```

If vulnerabilities are reported in BTCPay Server transitive dependencies, update the BTCPay submodule/dependency baseline where compatible with the target BTCPay Server release. Do not suppress vulnerability output without documenting why the vulnerable package is unreachable or inherited from the host BTCPay version.

## Release checklist

Before publishing a release:

1. `git status --short` shows no accidental artifacts/secrets.
2. Secret scan returns no tokens/private keys.
3. `dotnet build -c Release` succeeds.
4. Vulnerability scan has no unresolved plugin-owned vulnerable direct dependencies.
5. Generated `.btcpay` artifact is uploaded as a GitHub release asset, not committed.
6. BTCPay test server loads the plugin and logs `Running plugin BTCPayServer.Plugins.BitcoinRewards` without disable/migration errors.

## Dependency vulnerability review notes

Current plugin builds against the BTCPay Server submodule pinned in this repository. `dotnet list package --vulnerable --include-transitive` currently reports advisories from BTCPay Server's own dependency graph, including `MailKit`, `MimeKit`, `HtmlSanitizer`, `AngleSharp`, and `SSH.NET`.

These are upstream/submodule dependencies rather than plugin-owned secrets or plugin-specific hard-coded credentials. A test bump to BTCPay Server `v2.4.4` resolves newer pins upstream but requires a .NET 10 SDK/runtime, so it is not safe to adopt for the current .NET 8 plugin/runtime target. Revisit this when the target BTCPay deployment moves to the matching runtime.
