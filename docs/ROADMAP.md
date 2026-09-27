# Roadmap (Beta Readiness)

Curated from TODO/FIXME markers and known issues in the repo. Choose which to implement next.

## Core rewards / pricing
- Integrate real rate service instead of fixed BTC price stub (`Services/BitcoinRewardsService.cs` – TODO x2).

## Square integration
- Implement webhook signature verification (`Controllers/SquareWebhookController.cs`, `Clients/SquareApiClient.cs`).

## Delivery channels
- Add SMS delivery support (TESTING_CHECKLIST.md).
- Document and finalize email template variables (TESTING_CHECKLIST.md).

## Database & migrations
- Ensure plugin migrations are generated/applied automatically; remove need for manual creation (TESTING_CHECKLIST.md).

## Docs & release hygiene
- Expand README/install notes with template variables and platform-specific steps (from doc TODOs).
