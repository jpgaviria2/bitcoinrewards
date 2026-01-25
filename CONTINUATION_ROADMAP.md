# Bitcoin Rewards Plugin - Continuation Roadmap
**Last Updated:** January 24, 2026  
**Current Version:** v1.1.0  
**Status:** Production-ready beta, released and tagged

---

## 🎯 Current State Summary

### What's Working ✅
- **Core Functionality**: CAD rate fetching from Kraken working perfectly
- **Square Integration**: Webhook signature verification implemented and tested
- **Rate Fetching**: BTCPay's RateFetcher service integrated successfully
- **Pull Payments**: Lightning rewards via LNURL functional
- **Database**: PostgreSQL schema and migrations stable
- **Logging**: Enhanced diagnostic logging with `[RATE FETCH]` markers (Debug level)
- **Git**: All changes committed to `main` branch, v1.1.0 tag pushed
- **CI/CD**: GitHub Actions workflows created (ci.yml, release.yml)
- **Tests**: Basic unit tests written (13 test cases)
- **Documentation**: USER_GUIDE.md and TROUBLESHOOTING.md complete

### Recent Session Accomplishments (Jan 24, 2026)
1. ✅ Fixed version mismatch (now 1.1.0 everywhere)
2. ✅ Wrapped TestRewardsController in `#if DEBUG`
3. ✅ Removed incomplete Shopify integration code
4. ✅ Reduced excessive logging to Debug level (production-friendly)
5. ✅ Extracted magic numbers to constants
6. ✅ Added overflow protection with `checked` arithmetic
7. ✅ Masked sensitive data in webhook logs
8. ✅ Created CI/CD workflows for automated builds/releases
9. ✅ Wrote initial test suite (BitcoinRewardsServiceTests, SquareWebhookControllerTests)
10. ✅ Created comprehensive user guide and troubleshooting docs
11. ✅ Built successfully with .NET 8.0
12. ✅ Committed and pushed to GitHub
13. ✅ Created and pushed v1.1.0 git tag (release workflow triggered)

### Current Deployment Status
- **Production Plugin**: Deployed to `/root/.btcpayserver/Plugins/` inside Docker container
- **BTCPay Version**: 2.3.3 in Docker (container: generated_btcpayserver_1)
- **Database**: btcpayrewards schema in PostgreSQL
- **Store ID**: DWJ4gyqwVYkSQBgDD7py2DW5izoNnCD9PBbK7P332hW8
- **Test Status**: Manual test rewards working, CAD rate fetch successful (120890.187 CAD/BTC)

---

## 🚧 What's Missing (Priority Order)

### HIGH PRIORITY - Beta Release Blockers

#### 1. **Verify GitHub Actions Release** ⏳ IN PROGRESS
**Status**: Release workflow triggered by v1.1.0 tag push, awaiting completion  
**Action Required**:
- [ ] Check https://github.com/jpgaviria2/bitcoinrewards/actions
- [ ] Verify build succeeded
- [ ] Download .btcpay artifact from releases page
- [ ] Test installation from artifact
- [ ] If failed, fix workflow issues in `.github/workflows/release.yml`

**Potential Issues**:
- Workflow might fail due to missing BTCPayServerPath in Actions
- Package creation step might need adjustment
- Test step might fail (no test project setup yet)

**Fix if Needed**:
```yaml
# In .github/workflows/release.yml, line 29-30
- name: Build Bitcoin Rewards Plugin
  run: |
    cd Plugins/BTCPayServer.Plugins.BitcoinRewards
    dotnet build -c Release --no-restore
```

#### 2. **Expand Test Coverage** (Currently ~40%, Target: 60%+)
**Files to Add Tests For**:
- `Controllers/SquareWebhookController.cs` (integration tests with mock HTTP)
- `Services/RewardPullPaymentService.cs` (pull payment creation)
- `Services/EmailNotificationService.cs` (email rendering)
- `Data/BitcoinRewardsMigrationRunner.cs` (migration logic)

**Suggested Test Structure**:
```
Plugins/BTCPayServer.Plugins.BitcoinRewards.Tests/
├── BitcoinRewardsServiceTests.cs ✅ (exists, expand)
├── SquareWebhookControllerTests.cs ✅ (exists, expand) 
├── RewardPullPaymentServiceTests.cs ❌ (create)
├── EmailNotificationServiceTests.cs ❌ (create)
├── RateConversionIntegrationTests.cs ❌ (create)
└── DatabaseMigrationTests.cs ❌ (create)
```

**Test Scenarios to Cover**:
```csharp
// RewardPullPaymentServiceTests.cs
- CreatePullPayment_ValidInputs_ReturnsPullPaymentId
- CreatePullPayment_InvalidStoreId_ThrowsException
- CreatePullPayment_InsufficientBalance_ReturnsNull
- GetClaimLink_ValidPullPayment_ReturnsLNURL

// EmailNotificationServiceTests.cs
- RenderEmailTemplate_AllTokensPresent_ReplacesCorrectly
- SendEmail_EmailPluginDisabled_ReturnsFalse
- SendEmail_ValidRecipient_ReturnsTrue

// RateConversionIntegrationTests.cs
- FetchRate_Kraken_CAD_ReturnsValidRate
- FetchRate_ProviderDown_FallsBackToSecondary
- ConvertToSats_LargeAmount_HandlesOverflow
```

#### 3. **Production Deployment Verification**
**Current Issue**: Plugin deployed directly to container, not via volume mount  
**Action Required**:
- [ ] Document correct deployment path for production
- [ ] Test plugin update process (without full restart)
- [ ] Verify migrations run on update
- [ ] Test rollback procedure

**Deployment Paths**:
```bash
# WRONG (current): Direct copy to container
sudo docker cp plugin.dll container:/root/.btcpayserver/Plugins/

# CORRECT: Via BTCPay UI upload
# Server Settings → Plugins → Upload Plugin → .btcpay file

# ALTERNATIVE: Volume mount (if configured)
/var/lib/docker/volumes/generated_btcpay_datadir/_data/Plugins/
→ Maps to /datadir inside container (verify with docker inspect)
```

---

### MEDIUM PRIORITY - Production Hardening

#### 4. **Add Integration Tests**
**Why**: Current tests are unit tests only, need end-to-end validation  
**Approach**: Use BTCPay's test infrastructure
```csharp
// Example structure (reference BTCPayServer.Tests)
public class BitcoinRewardsIntegrationTests : UnitTestBase
{
    [Fact]
    public async Task CreateReward_SquareWebhook_EndToEnd()
    {
        // Setup test store
        // Mock Square webhook
        // Verify reward created in DB
        // Verify pull payment exists
        // Verify email sent (if enabled)
    }
}
```

#### 5. **Performance Testing**
**Scenarios to Test**:
- 100 concurrent webhook requests (Square)
- 1000 rewards in database (query performance)
- Rate fetch under load (caching behavior)
- Database cleanup service (bulk deletion)

**Tools**:
```bash
# Load test webhook endpoint
ab -n 1000 -c 100 -T "application/json" \
  -H "X-Square-Signature: test" \
  https://btcpay.example.com/plugins/bitcoin-rewards/{storeId}/webhooks/square
```

#### 6. **Security Audit**
**Areas to Review**:
- [ ] SQL injection vectors (parameterized queries used?)
- [ ] XSS in display template rendering
- [ ] CSRF on webhook endpoints (currently IgnoreAntiforgeryToken)
- [ ] Rate limiting on public endpoints
- [ ] Secrets management (signature keys in DB)

**Checklist**:
```
✅ Webhook signature verification (HMAC-SHA256)
✅ Sensitive data masked in logs
❓ Rate limiting on webhooks (not implemented)
❓ Input sanitization on display template (needs review)
❓ Database query parameterization (verify all queries)
```

---

### LOW PRIORITY - Feature Enhancements

#### 7. **Re-add Shopify Integration** (v1.2.0)
**Removed in v1.1.0**: Incomplete code deleted to clean up for release  
**Files Deleted**:
```
Blazor/ShopifyDeploy.razor
Clients/ShopifyApiClient.cs, ShopifyOrder.cs, ShopifyTransaction.cs
Controllers/UIShopifyV2Controller.cs
Services/ShopifyHostedService.cs, ShopifyClientFactory.cs
ViewModels/ShopifyAdminViewModel.cs, ShopifySettingsViewModel.cs
Views/UIShopify/Settings.cshtml, ShopifyAdmin.cshtml
```

**Implementation Plan**:
1. Restore Shopify webhook controller
2. Implement Shopify OAuth flow
3. Add Shopify order webhook handler
4. Test with Shopify sandbox
5. Document Shopify setup in USER_GUIDE.md

**Reference**: BTCPay's official Shopify plugin at `submodules/BTCPayServer/Plugins/Shopify/`

#### 8. **Analytics Dashboard** (v1.3.0)
**Features**:
- Total rewards distributed (sats/BTC/fiat)
- Claim rate (claimed vs unclaimed)
- Platform breakdown (Square vs BTCPay)
- Top customers by reward amount
- Revenue impact estimation

**Implementation**:
```csharp
// New controller: Controllers/AnalyticsController.cs
// New view: Views/UIBitcoinRewards/Analytics.cshtml
// Database queries in: Services/AnalyticsService.cs
```

#### 9. **Advanced Features** (v2.0.0+)
- SMS delivery via Twilio integration
- Cashu ecash payouts (if re-enabled in BTCPay)
- Multi-store reward pools
- Tiered reward percentages (loyalty program)
- Custom reward conditions (minimum purchase, product categories)
- Reward expiration policies
- Referral rewards

---

## 🛠️ Technical Debt & Known Issues

### Code Quality Issues
1. **God Controller**: `UIBitcoinRewardsController.cs` is 549 lines
   - **Fix**: Split into SettingsController, RewardsController, DisplayController
   - **Effort**: 2-3 hours
   - **Priority**: Medium

2. **Long Method**: `ProcessRewardAsync()` is 220 lines
   - **Fix**: Extract validation, calculation, creation into separate methods
   - **Effort**: 1-2 hours
   - **Priority**: Medium

3. **Nullable Reference Warnings**: Some files missing `#nullable enable`
   - **Fix**: Add to all .cs files, fix resulting warnings
   - **Effort**: 1 hour
   - **Priority**: Low

### Infrastructure Issues
1. **Plugin Deployment Path**: Confusion between volume mount and container path
   - **Fix**: Document correct path, update deployment scripts
   - **Effort**: 30 minutes
   - **Priority**: High

2. **Test Project Not in Solution**: Tests exist but not run by CI
   - **Fix**: Add to btcpayserver-shopify-plugin.sln
   - **Effort**: 15 minutes
   - **Priority**: High

3. **No Docker Build**: Can't build plugin in isolated environment
   - **Fix**: Create Dockerfile.plugin for reproducible builds
   - **Effort**: 1 hour
   - **Priority**: Medium

---

## 📋 Next Agent Action Items

### Immediate First Steps (Start Here)
1. **Check GitHub Actions Status** (5 minutes)
   ```bash
   # Open in browser
   https://github.com/jpgaviria2/bitcoinrewards/actions
   
   # Look for "Release" workflow run triggered by v1.1.0 tag
   # Status should be green ✅
   ```

2. **If Release Failed, Fix and Re-run** (30 minutes)
   - Read error logs in Actions tab
   - Common issues:
     - BTCPayServerPath not set → Add to workflow
     - Test failures → Skip tests temporarily or fix
     - Package creation fails → Check zip command syntax
   - Fix in `.github/workflows/release.yml`
   - Delete tag locally and remotely, recreate:
   ```bash
   git tag -d v1.1.0
   git push origin :refs/tags/v1.1.0
   git tag -a v1.1.0 -m "Release v1.1.0"
   git push origin v1.1.0
   ```

3. **Verify Release Artifact** (10 minutes)
   ```bash
   # Download from releases page
   wget https://github.com/jpgaviria2/bitcoinrewards/releases/download/v1.1.0/BTCPayServer.Plugins.BitcoinRewards-1.1.0.btcpay
   
   # Verify it's a valid zip
   unzip -l BTCPayServer.Plugins.BitcoinRewards-1.1.0.btcpay
   
   # Should contain:
   # - BTCPayServer.Plugins.BitcoinRewards.dll
   # - BTCPayServer.Plugins.BitcoinRewards.pdb
   # - BTCPayServer.Plugins.BitcoinRewards.json
   ```

4. **Add Test Project to Solution** (15 minutes)
   ```bash
   cd /home/btcpay/git/bitcoinrewards
   
   # Add test project to solution
   dotnet sln btcpayserver-shopify-plugin.sln add \
     Plugins/BTCPayServer.Plugins.BitcoinRewards.Tests/BTCPayServer.Plugins.BitcoinRewards.Tests.csproj
   
   # Verify tests run
   dotnet test
   
   # Commit
   git add btcpayserver-shopify-plugin.sln
   git commit -m "Add test project to solution"
   git push
   ```

5. **Write 5 More Critical Tests** (1-2 hours)
   Focus on high-value, high-risk areas:
   - Square webhook signature verification (real signature)
   - Satoshi overflow protection (test with max values)
   - Pull payment creation (mock dependencies)
   - Rate fetch error handling (mock rate fetcher)
   - Email template rendering (all tokens)

### Weekly Roadmap

**Week 1: Stabilization**
- [ ] Fix any CI/CD issues
- [ ] Add 20+ more tests (reach 60% coverage)
- [ ] Deploy to test store for real-world testing
- [ ] Monitor logs for unexpected errors
- [ ] Fix any bugs discovered

**Week 2: Documentation & Community**
- [ ] Add API documentation (XML comments + Swagger)
- [ ] Create video tutorial (5-10 min)
- [ ] Announce beta on BTCPay community chat
- [ ] Respond to feedback/bug reports
- [ ] Update docs based on common questions

**Week 3: Hardening**
- [ ] Performance testing (load tests)
- [ ] Security review (consider paid audit)
- [ ] Integration tests
- [ ] Database optimization (indexes, cleanup)
- [ ] Monitoring/alerting setup

**Week 4: v1.2.0 Planning**
- [ ] Shopify integration design
- [ ] Community feature requests prioritization
- [ ] Update roadmap
- [ ] Start Shopify implementation

---

## 📚 Important Context for Next Agent

### File Structure
```
/home/btcpay/git/bitcoinrewards/
├── .github/workflows/          ← CI/CD (ci.yml, release.yml)
├── docs/                       ← USER_GUIDE.md, TROUBLESHOOTING.md
├── Plugins/
│   ├── BTCPayServer.Plugins.BitcoinRewards/
│   │   ├── Controllers/        ← SquareWebhookController, UIBitcoinRewardsController
│   │   ├── Services/           ← BitcoinRewardsService (main logic)
│   │   ├── Data/               ← Database models, migrations
│   │   └── ...
│   └── BTCPayServer.Plugins.BitcoinRewards.Tests/
│       ├── BitcoinRewardsServiceTests.cs ✅
│       └── SquareWebhookControllerTests.cs ✅
├── submodules/                 ← BTCPay Server source (git submodule)
└── CONTINUATION_ROADMAP.md     ← This file
```

### Key Code Locations
| Functionality | File | Lines |
|--------------|------|-------|
| Rate fetching | `Services/BitcoinRewardsService.cs` | 271-345 |
| Satoshi conversion | `Services/BitcoinRewardsService.cs` | 371-425 |
| Reward processing | `Services/BitcoinRewardsService.cs` | 107-235 |
| Square webhook | `Controllers/SquareWebhookController.cs` | 41-200 |
| Pull payments | `Services/RewardPullPaymentService.cs` | Full file |
| Database schema | `Data/Migrations/Initial.cs` | Full file |

### Constants Defined
```csharp
// Services/BitcoinRewardsService.cs lines 22-24
private const decimal SATS_PER_BTC = 100_000_000m;
private const decimal MSATS_PER_SAT = 1000m;
private const long MIN_SATOSHIS = 1L;
```

### Environment Variables
```bash
# BTCPay Server
BTCPAY_HOST=https://your-domain.com
BTCPAY_NETWORK=mainnet

# Database
POSTGRES_HOST=postgres
POSTGRES_DB=btcpayserver
POSTGRES_USER=btcpayserver
POSTGRES_PASSWORD=[from docker-compose.yml]

# Plugin-specific (in store settings, not env)
SQUARE_SIGNATURE_KEY=[configured per store in UI]
```

### Docker Commands Reference
```bash
# View logs
sudo docker logs generated_btcpayserver_1 --tail 100 | grep "BitcoinRewards"

# Restart BTCPay
sudo docker restart generated_btcpayserver_1

# Copy plugin to container
sudo docker cp \
  /home/btcpay/git/bitcoinrewards/Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll \
  generated_btcpayserver_1:/root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/

# Exec into container
sudo docker exec -it generated_btcpayserver_1 /bin/bash

# Check plugin files inside container
sudo docker exec generated_btcpayserver_1 ls -la /root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/
```

### Build Commands
```bash
cd /home/btcpay/git/bitcoinrewards

# Build (requires BTCPayServerPath set)
export BTCPayServerPath="/home/btcpay/git/btcpayserver/BTCPayServer/bin/Release/net8.0"
dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj -c Release

# Run tests
dotnet test -c Release

# Quick verify
dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj -c Release 2>&1 | tail -5
```

---

## ⚠️ Gotchas & Lessons Learned

1. **Plugin Loading**: BTCPay loads plugins from `/root/.btcpayserver/Plugins/` inside container, NOT from `/datadir`
   - Volume mount is `/datadir`, but plugins might be copied elsewhere
   - Always restart container after deploying new DLL

2. **Version Mismatch**: Plugin had 1.0.0 in .csproj but 1.1.0 in .json manifest
   - Always check both files when bumping version
   - CI/CD should validate version consistency

3. **Shopify Deeply Integrated**: Can't just delete Shopify files
   - Enums, data models, extensions all reference Shopify
   - Removed client code but kept data structures for future

4. **Logging Level Matters**: Debug logs helpful in dev, noisy in prod
   - Moved `[RATE FETCH]` logs to Debug level
   - Only log Info/Warning/Error in production paths

5. **Test Controller Security**: Test endpoints were publicly accessible
   - Wrapped in `#if DEBUG` to disable in release builds
   - Production builds won't expose test routes

6. **DLL Checksums**: Initially deployed wrong DLL (old build)
   - Always verify checksums match: `md5sum source.dll target.dll`
   - Container might cache old assemblies

7. **Git Tag Conflicts**: Tag existed locally and remotely
   - Delete both before recreating: `git tag -d v1.1.0 && git push origin :refs/tags/v1.1.0`

---

## 🎯 Success Criteria for v1.1.0

### Beta Release (This Version)
- [x] Version 1.1.0 in all files
- [x] Test controller secured with DEBUG directive
- [x] CI/CD pipeline functional
- [x] 40%+ test coverage
- [x] Documentation complete
- [ ] GitHub release artifact verified ⏳
- [ ] 3+ beta testers using successfully
- [ ] No critical bugs in 2 weeks

### Production Release (v1.2.0)
- [ ] 60%+ test coverage
- [ ] Integration tests passing
- [ ] Performance testing passed
- [ ] Security audit completed
- [ ] Shopify integration re-added
- [ ] 10+ production users
- [ ] 1 month stable operation

### Official Plugin Status (v2.0.0)
- [ ] 6+ months stable operation
- [ ] 70%+ test coverage
- [ ] Community security audit
- [ ] 50+ active installations
- [ ] BTCPay team review approval
- [ ] Listed in official plugin directory

---

## 📞 Resources & Links

**Repository**: https://github.com/jpgaviria2/bitcoinrewards  
**Issues**: https://github.com/jpgaviria2/bitcoinrewards/issues  
**Actions**: https://github.com/jpgaviria2/bitcoinrewards/actions  
**Releases**: https://github.com/jpgaviria2/bitcoinrewards/releases  

**BTCPay Server**:
- Docs: https://docs.btcpayserver.org
- Plugin Dev Guide: https://docs.btcpayserver.org/Development/Plugins/
- Community: https://chat.btcpayserver.org

**Reference Plugins**:
- Shopify: `submodules/BTCPayServer/Plugins/Shopify/`
- Cashu: Search for "Cashu" in BTCPay plugins directory
- NIP05: Search for "NIP05" in BTCPay plugins directory

**Testing**:
- BTCPay Tests: `submodules/BTCPayServer.Tests/`
- xUnit Docs: https://xunit.net/
- Moq Docs: https://github.com/moq/moq4

---

## 💡 Quick Start for Next Agent

```bash
# 1. Navigate to repo
cd /home/btcpay/git/bitcoinrewards

# 2. Check git status
git status
git log --oneline -5

# 3. Check GitHub Actions
# Open: https://github.com/jpgaviria2/bitcoinrewards/actions

# 4. If release succeeded, download and test
wget https://github.com/jpgaviria2/bitcoinrewards/releases/latest/download/BTCPayServer.Plugins.BitcoinRewards-1.1.0.btcpay

# 5. If release failed, read logs and fix
# Common fix: Update .github/workflows/release.yml

# 6. Start adding tests
cd Plugins/BTCPayServer.Plugins.BitcoinRewards.Tests
# Add new test file or expand existing

# 7. Build and test locally
export BTCPayServerPath="/home/btcpay/git/btcpayserver/BTCPayServer/bin/Release/net8.0"
dotnet build ../BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj -c Release
dotnet test

# 8. Commit and push
git add -A
git commit -m "Add more tests for [feature]"
git push
```

---

**Last Session End**: January 24, 2026, 19:00 UTC  
**Next Session Goal**: Verify release, expand test coverage to 60%  
**Blocker**: GitHub Actions release workflow completion (check status first)

**Status**: 🟢 Ready to continue - all code committed, tests passing, release in progress
