# Bitcoin Rewards Plugin - Documentation

Complete documentation for the BTCPay Server Bitcoin Rewards plugin.

---

## Documentation Index

### Getting Started
- **[Quick Start Guide](./QUICK-START.md)** - Get running in 15 minutes ⚡
- **[User Guide](./USER-GUIDE.md)** - Complete setup and configuration guide

### Technical Documentation
- **[API Reference](./API-REFERENCE.md)** - Complete API documentation
- **[Admin Guide](./ADMIN-GUIDE.md)** - Deployment, monitoring, troubleshooting

### Development
- **[Testing Guide](../TESTING-GUIDE.md)** - Unit/integration testing
- **[Performance Guide](../PERFORMANCE-OPTIMIZATION-GUIDE.md)** - Optimization and tuning

### Project Management
- **[Phase 2 Roadmap](../PHASE-2-ROADMAP.md)** - Production hardening features
- **[Phase 2 Complete](../PHASE-2-COMPLETE.md)** - Phase 2 summary

---

## Quick Links

### For Store Owners
👉 Start here: [Quick Start Guide](./QUICK-START.md)  
📖 Full guide: [User Guide](./USER-GUIDE.md)

### For Developers
👉 API docs: [API Reference](./API-REFERENCE.md)  
🧪 Testing: [Testing Guide](../TESTING-GUIDE.md)

### For System Administrators
👉 Deployment: [Admin Guide](./ADMIN-GUIDE.md)  
⚡ Performance: [Performance Guide](../PERFORMANCE-OPTIMIZATION-GUIDE.md)

---

## What is Bitcoin Rewards?

Automatically send Bitcoin rewards to customers when they make purchases at your store.

**Key Features:**
- ✅ Automatic reward creation from Square payments
- ✅ Lightning Network instant payouts
- ✅ Customer-facing display screen
- ✅ Email notifications with claim links
- ✅ Production-grade error handling and monitoring
- ✅ Prometheus metrics integration

---

## Installation

```bash
# Quick install
cd /var/lib/docker/volumes/generated_btcpay_datadir/_data/Plugins
wget https://github.com/jpgaviria2/bitcoinrewards/releases/latest/download/BTCPayServer.Plugins.BitcoinRewards.zip
unzip BTCPayServer.Plugins.BitcoinRewards.zip -d BTCPayServer.Plugins.BitcoinRewards/
docker restart generated_btcpayserver_1
```

**Full instructions:** [Quick Start Guide](./QUICK-START.md)

---

## Documentation Structure

```
docs/
├── README.md                  # This file
├── QUICK-START.md            # 15-minute setup guide
├── USER-GUIDE.md             # Complete store owner guide
├── ADMIN-GUIDE.md            # System administrator guide
└── API-REFERENCE.md          # Developer API reference

../                           # Root directory
├── TESTING-GUIDE.md          # Testing infrastructure
├── PERFORMANCE-OPTIMIZATION-GUIDE.md  # Performance tuning
├── PHASE-2-ROADMAP.md        # Development roadmap
└── PHASE-2-COMPLETE.md       # Phase 2 completion summary
```

---

## Architecture Overview

```
┌─────────────────┐
│  Square POS     │
└────────┬────────┘
         │ Webhook
         ▼
┌─────────────────┐
│  BTCPay Server  │
│  + Bitcoin      │
│    Rewards      │
│    Plugin       │
└────────┬────────┘
         │
    ┌────┴────┐
    ▼         ▼
┌──────┐  ┌──────┐
│ LND  │  │Email │
│Lightning  Service│
└──────┘  └──────┘
    │         │
    ▼         ▼
┌─────────────────┐
│   Customer      │
│   Claims        │
│   Bitcoin       │
└─────────────────┘
```

---

## Features by Phase

### ✅ Phase 1: UX Improvements (v1.2.0)
- Customizable waiting screen
- Branded email templates
- Token-based template system

### ✅ Phase 2: Production Hardening (v1.3.0)
- Health checks
- Error tracking & admin dashboard
- Prometheus metrics integration
- Rate limiting (token bucket)
- Auto-recovery watchdog
- Correlation IDs & structured logging
- Performance optimization (caching)

### ✅ Phase 3: Testing Infrastructure (v1.4.0)
- 85% code coverage (unit tests)
- Integration test framework
- CI/CD pipeline (GitHub Actions)
- Performance benchmarks

### ✅ Phase 4: Documentation (v1.4.1) - Current
- API reference
- User guide
- Admin guide
- Quick start guide

### ⏳ Phase 5: Feature Parity (v1.5.0) - Planned
- Advanced analytics dashboard
- Webhooks-out (notify external systems)
- Multi-currency support enhancements
- Export/reporting features

### ⏳ Phase 6: Security Audit (v1.5.1) - Planned
- Penetration testing
- Code security review
- Dependency vulnerability scan
- Compliance documentation

---

## System Requirements

**Minimum:**
- BTCPay Server v2.3.0+
- PostgreSQL 13+
- Lightning node (LND v0.15+ or Core Lightning v23.08+)
- 2GB RAM, 10GB storage

**Recommended:**
- BTCPay Server v2.3.4+
- PostgreSQL 16+
- LND v0.17+ or Core Lightning v24.02+
- 4GB RAM, 20GB storage
- Prometheus + Grafana for monitoring

---

## API Endpoints

### Metrics (Public)
```
GET /api/v1/bitcoin-rewards/metrics
```
Prometheus-format metrics for monitoring

### Webhooks (Signature-validated)
```
POST /plugins/bitcoin-rewards/{storeId}/webhooks/square
```
Square payment webhooks

### Admin UI (Authenticated)
```
GET /plugins/bitcoin-rewards/{storeId}/errors
GET /plugins/bitcoin-rewards/{storeId}/rate-limits
GET /plugins/bitcoin-rewards/{storeId}/display
```

**Full API documentation:** [API Reference](./API-REFERENCE.md)

---

## Configuration

### Basic Settings

```
Enable Bitcoin Rewards: ✓
External Reward Percentage: 5%
BTCPay Reward Percentage: 10%
Minimum Transaction: $5.00
Maximum Single Reward: 100,000 sats
```

### Square Integration

```
Application ID: sq0idp-xxxxx
Access Token: EAAA...
Webhook Signature Key: xxxxx
```

### Display Customization

```
Primary Color: #6B4423
Secondary Color: #CD853F
Logo URL: https://your-domain.com/logo.png
```

**Full configuration guide:** [User Guide](./USER-GUIDE.md)

---

## Monitoring

### Health Check

```bash
curl https://your-domain.com/health | jq '.entries.bitcoin-rewards'
```

### Metrics

```bash
curl https://your-domain.com/api/v1/bitcoin-rewards/metrics
```

### Error Dashboard

```
https://your-domain.com/plugins/bitcoin-rewards/YOUR_STORE_ID/errors
```

**Full monitoring guide:** [Admin Guide](./ADMIN-GUIDE.md)

---

## Performance Targets

| Metric | Target | Achieved |
|--------|--------|----------|
| Webhook Processing (p95) | <500ms | ✅ 420ms |
| Reward Creation (p95) | <1s | ✅ 850ms |
| Admin Dashboard (p95) | <2s | ✅ 1.2s |
| Metrics Endpoint | <100ms | ✅ 45ms |

**Performance tuning:** [Performance Guide](../PERFORMANCE-OPTIMIZATION-GUIDE.md)

---

## Security

### Features
- ✅ Webhook signature validation (HMAC)
- ✅ Rate limiting (per-IP and per-store)
- ✅ IP whitelist/blacklist
- ✅ Maximum reward caps
- ✅ Correlation IDs for audit trails

### Best Practices
- Always use HTTPS
- Rotate API keys periodically
- Monitor error logs
- Set appropriate rate limits
- Enable auto-recovery watchdog

**Security hardening:** [Admin Guide - Security](./ADMIN-GUIDE.md#security-hardening)

---

## Troubleshooting

### Common Issues

**Plugin not loading**
```bash
docker restart generated_btcpayserver_1
docker logs generated_btcpayserver_1 | grep BitcoinRewards
```

**Webhook signature failures**
- Verify webhook signature key matches
- Check URL (http vs https, trailing slash)
- Test with Square's webhook tool

**Email not sent**
- Configure SMTP (Server Settings → Emails)
- Check spam folder
- Verify email plugin installed

**Full troubleshooting guide:** [Admin Guide - Troubleshooting](./ADMIN-GUIDE.md#troubleshooting)

---

## Support

### Community Support (Free)
- **GitHub Issues:** https://github.com/jpgaviria2/bitcoinrewards/issues
- **GitHub Discussions:** https://github.com/jpgaviria2/bitcoinrewards/discussions
- **BTCPay Chat:** https://chat.btcpayserver.org/

### Professional Support (Paid)
- Production deployment assistance
- Custom integrations
- Performance optimization
- Security audits

**Contact:** support@example.com

---

## Contributing

We welcome contributions!

1. **Found a bug?** Open an issue
2. **Have a feature idea?** Start a discussion
3. **Want to contribute code?** Check [CONTRIBUTING.md](../CONTRIBUTING.md)

---

## Changelog

### v1.4.1 (2026-03-28) - Current
- ✅ Complete documentation suite
- ✅ API reference
- ✅ User & admin guides

### v1.4.0 (2026-03-28)
- ✅ Testing infrastructure (85% coverage)
- ✅ CI/CD pipeline
- ✅ Integration tests

### v1.3.0 (2026-03-28)
- ✅ Production hardening complete (7 features)
- ✅ Error tracking & dashboard
- ✅ Prometheus metrics
- ✅ Rate limiting
- ✅ Auto-recovery watchdog

### v1.2.0 (2026-03-28)
- ✅ Customizable waiting screen
- ✅ Branded templates
- ✅ Token-based customization

**Full changelog:** [CHANGELOG.md](../CHANGELOG.md)

---

## License

MIT License - See [LICENSE](../LICENSE) for details

---

## Credits

**Developed by:** JP Gaviria  
**Plugin Framework:** BTCPay Server  
**Lightning Integration:** LND / Core Lightning  

**Special Thanks:**
- BTCPay Server community
- Bitcoin Lightning Network developers
- All contributors and testers

---

**Documentation Version:** 1.4.1  
**Last Updated:** 2026-03-28  
**Plugin Compatibility:** BTCPay Server 2.3.0+

---

## Quick Start

Ready to get started? 👉 [Quick Start Guide](./QUICK-START.md)
