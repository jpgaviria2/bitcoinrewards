# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.5.x   | :white_check_mark: |
| 1.4.x   | :white_check_mark: |
| 1.3.x   | :white_check_mark: |
| < 1.3.0 | :x:                |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

### Reporting Process

1. **Email:** security@example.com (PGP key available)
2. **Subject:** `[SECURITY] Bitcoin Rewards Plugin - Brief Description`
3. **Include:**
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if available)

### Response Timeline

- **Initial response:** Within 24 hours
- **Status update:** Within 7 days
- **Fix timeline:** 30-90 days depending on severity

### Disclosure Policy

- We follow responsible disclosure
- Security advisories published after fix is deployed
- Credit given to reporters (if desired)

---

## Security Features

### Authentication & Authorization

✅ **BTCPay Server Integration**
- Cookie-based authentication
- API key support
- Role-based access control (RBAC)
- Store-level permissions

✅ **Webhook Signature Validation**
- HMAC-SHA256 signatures (incoming webhooks)
- Constant-time comparison (timing attack prevention)
- Multiple URL variant checking
- Secret per store

### Rate Limiting

✅ **Token Bucket Algorithm**
- Per-IP limits: 60 req/min (webhooks), 120 req/min (API)
- Per-store limits: 100 req/min
- Burst capacity for traffic spikes
- IP whitelist/blacklist

✅ **Request Size Limits**
- Webhook payload: 1 MB max
- Request timeout: 30s default
- Connection limits: 100 concurrent

### Data Protection

✅ **PII Handling**
- Email/phone masked in logs
- Customer data hashed (SHA256) in analytics
- Optional PII exclusion in exports
- GDPR-compliant data retention

✅ **Input Validation**
- Transaction amount validation (min/max)
- Reward cap enforcement (security feature)
- SQL injection prevention (parameterized queries)
- XSS prevention (output encoding)

### Network Security

✅ **HTTPS Required**
- All webhooks must use HTTPS
- TLS 1.2+ only
- Certificate validation

✅ **CORS Policy**
- Restrictive CORS headers
- Origin validation
- Credential requirements

### Error Handling

✅ **Security-Safe Errors**
- No stack traces in production responses
- Generic error messages to clients
- Detailed logging server-side only
- Correlation IDs for debugging

---

## Security Best Practices

### For Store Owners

1. **API Keys:**
   - Rotate every 90 days
   - Use separate keys per integration
   - Never commit to version control
   - Store in secure vault (LastPass, 1Password)

2. **Webhook Secrets:**
   - Generate strong secrets (32+ characters)
   - Rotate if compromised
   - Use different secrets per store

3. **Reward Caps:**
   - Set maximum single reward (prevent fraud)
   - Monitor for anomalies
   - Alert on large rewards

4. **Rate Limiting:**
   - Enable for production
   - Whitelist trusted IPs
   - Blacklist bad actors

5. **Monitoring:**
   - Review error dashboard daily
   - Check metrics for anomalies
   - Set up alerting (Prometheus)

### For Developers

1. **Dependencies:**
   - Run `dotnet list package --vulnerable` regularly
   - Update to patched versions
   - Review third-party licenses

2. **Code Review:**
   - All PRs require review
   - Security-focused review for sensitive code
   - Automated security scans (GitHub CodeQL)

3. **Testing:**
   - Security test cases in unit tests
   - Penetration testing for major releases
   - Fuzzing for input validation

4. **Secrets Management:**
   - Never hardcode secrets
   - Use environment variables
   - Encrypt at rest

---

## Known Security Considerations

### 1. Webhook Signature URL Variants

**Issue:** Square webhooks may send different URL formats (http/https, trailing slash, port)

**Mitigation:** Plugin checks multiple URL variants during signature validation

**Risk Level:** Low (defense in depth)

### 2. Lightning Node Access

**Issue:** Plugin requires Lightning node RPC access (LND/Core Lightning)

**Mitigation:**
- Use dedicated macaroon with limited permissions
- Network isolation (Docker/firewall)
- Connection encryption

**Risk Level:** Medium (standard Lightning deployment)

### 3. Database Credentials

**Issue:** PostgreSQL connection string contains password

**Mitigation:**
- Use environment variables
- Restrict database user permissions (no DROP/CREATE on system tables)
- Connection pooling with limits

**Risk Level:** Low (standard practice)

### 4. PII in Logs

**Issue:** Customer emails/phones logged for debugging

**Mitigation:**
- PII masked in all log messages
- Log retention policy (30 days)
- Secure log storage

**Risk Level:** Low (GDPR-compliant)

---

## Security Audit History

### v1.5.1 - Phase 6 Security Audit (2026-03-28)

**Performed:** Internal security review

**Scope:**
- Code review (manual)
- Dependency scan (automated)
- Penetration testing (limited)
- Configuration review

**Findings:**
- ✅ No critical vulnerabilities
- ✅ No high-risk issues
- ⚠️ 2 medium-risk items (addressed)
- ℹ️ 3 informational items (documented)

**Actions Taken:**
1. Updated dependencies (no vulnerabilities)
2. Enhanced input validation
3. Added security documentation
4. Implemented security headers

**Report:** See `security-audit-report-v1.5.1.md`

---

## Compliance

### GDPR (EU)

✅ **Right to Access:** Export functionality provides customer data  
✅ **Right to Erasure:** Manual deletion via admin UI (future: automated)  
✅ **Data Minimization:** Only store necessary transaction data  
✅ **Purpose Limitation:** Data used only for rewards processing  
✅ **Security Measures:** Encryption, access controls, logging  

**Note:** Store owners are data controllers. Plugin is data processor.

### PCI DSS

✅ **No Card Data:** Plugin never handles credit card information  
✅ **Network Segmentation:** Runs in isolated Docker container  
✅ **Access Control:** BTCPay Server authentication required  
✅ **Logging:** All actions logged with correlation IDs  

**Note:** Square handles PCI compliance for payment processing.

---

## Security Headers

Plugin ensures these headers are present (via BTCPay Server):

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
X-XSS-Protection: 1; mode=block
Strict-Transport-Security: max-age=31536000; includeSubDomains
Content-Security-Policy: default-src 'self'
```

---

## Vulnerability Disclosure Timeline

### Example: Hypothetical Vulnerability

**Day 0:** Vulnerability reported via email  
**Day 1:** Confirmed and severity assessed (High)  
**Day 7:** Patch developed and tested  
**Day 14:** Fix deployed to production  
**Day 30:** Public disclosure (CVE assigned)  
**Day 90:** Detailed writeup published  

---

## Security Contacts

**Primary:** security@example.com  
**Secondary:** GitHub Security Advisories  
**PGP Key:** Available at keybase.io/bitcoinrewards  

---

## Attribution

Security researchers who have helped improve this plugin:

- *Your name here* - Report vulnerability responsibly and get credited!

---

## References

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [BTCPay Server Security](https://docs.btcpayserver.org/Security/)
- [CWE Top 25](https://cwe.mitre.org/top25/)

---

**Last Updated:** 2026-03-28  
**Version:** 1.5.1  
**Next Review:** 2026-06-28 (Quarterly)
