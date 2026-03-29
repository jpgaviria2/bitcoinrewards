#!/bin/bash
# Security Audit Script for Bitcoin Rewards Plugin
# Run this before each release

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
REPORT_FILE="$PROJECT_ROOT/security-audit-report-$(date +%Y%m%d).txt"

echo "==================================="
echo "Bitcoin Rewards Security Audit"
echo "==================================="
echo ""
echo "Date: $(date)"
echo "Report: $REPORT_FILE"
echo ""

# Check 1: Dependency Vulnerability Scan
echo "[1/6] Scanning dependencies for known vulnerabilities..."
cd "$PROJECT_ROOT"
dotnet list package --vulnerable --include-transitive > /tmp/vuln-check.txt 2>&1 || true

if grep -q "has the following vulnerable packages" /tmp/vuln-check.txt; then
    echo "⚠️  WARNING: Vulnerable dependencies found!"
    grep -A 20 "vulnerable packages" /tmp/vuln-check.txt
    echo ""
else
    echo "✅ No known vulnerabilities in dependencies"
    echo ""
fi

# Check 2: Hardcoded Secrets Scan
echo "[2/6] Scanning for hardcoded secrets..."
SECRET_PATTERNS=(
    "password\s*=\s*['\"][^'\"]{8,}"
    "api[_-]?key\s*=\s*['\"][^'\"]{20,}"
    "secret\s*=\s*['\"][^'\"]{16,}"
    "token\s*=\s*['\"][^'\"]{32,}"
    "-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----"
)

SECRETS_FOUND=0
for pattern in "${SECRET_PATTERNS[@]}"; do
    if grep -rEi "$pattern" \
        --include="*.cs" \
        --include="*.json" \
        --include="*.yml" \
        --include="*.yaml" \
        --exclude-dir="bin" \
        --exclude-dir="obj" \
        --exclude-dir="node_modules" \
        "$PROJECT_ROOT/Plugins" > /dev/null 2>&1; then
        echo "⚠️  WARNING: Potential secret found matching pattern: $pattern"
        SECRETS_FOUND=1
    fi
done

if [ $SECRETS_FOUND -eq 0 ]; then
    echo "✅ No hardcoded secrets detected"
fi
echo ""

# Check 3: SQL Injection Risk
echo "[3/6] Checking for potential SQL injection risks..."
if grep -rE "(FromSqlRaw|FromSql|ExecuteSqlCommand|ExecuteSqlRaw)" \
    --include="*.cs" \
    "$PROJECT_ROOT/Plugins" | grep -v "// Safe:" > /dev/null 2>&1; then
    echo "⚠️  WARNING: Raw SQL found - verify parameterization"
    grep -rE "(FromSqlRaw|FromSql)" --include="*.cs" "$PROJECT_ROOT/Plugins" | head -5
else
    echo "✅ No raw SQL usage detected (uses EF Core properly)"
fi
echo ""

# Check 4: XSS Prevention
echo "[4/6] Checking for potential XSS risks..."
if grep -rE "@Html\.Raw\(|innerHTML\s*=" \
    --include="*.cshtml" \
    --include="*.js" \
    "$PROJECT_ROOT/Plugins" > /dev/null 2>&1; then
    echo "⚠️  WARNING: Potential XSS risk found"
    grep -rE "@Html\.Raw\(|innerHTML\s*=" \
        --include="*.cshtml" \
        "$PROJECT_ROOT/Plugins" | head -5
else
    echo "✅ No obvious XSS risks detected"
fi
echo ""

# Check 5: Authentication Check
echo "[5/6] Checking authentication on controllers..."
UNAUTHED_ENDPOINTS=$(grep -rE "^\s*\[HttpGet|^\s*\[HttpPost" \
    --include="*Controller.cs" \
    "$PROJECT_ROOT/Plugins" -A 1 | \
    grep -v "Authorize" | \
    grep -E "HttpGet|HttpPost" | \
    wc -l)

if [ "$UNAUTHED_ENDPOINTS" -gt 5 ]; then
    echo "⚠️  WARNING: $UNAUTHED_ENDPOINTS endpoints without [Authorize] attribute"
    echo "    (Some may be intentionally public, verify manually)"
else
    echo "✅ Most endpoints have authorization"
fi
echo ""

# Check 6: HTTPS Enforcement
echo "[6/6] Checking for HTTPS enforcement..."
if grep -rE "http://" \
    --include="*.cs" \
    --include="*.json" \
    "$PROJECT_ROOT/Plugins" | \
    grep -v "localhost" | \
    grep -v "127.0.0.1" | \
    grep -v "// HTTP OK:" > /dev/null 2>&1; then
    echo "⚠️  WARNING: HTTP URLs found (should use HTTPS in production)"
else
    echo "✅ No hardcoded HTTP URLs found"
fi
echo ""

# Summary
echo "==================================="
echo "Security Audit Complete"
echo "==================================="
echo ""
echo "Summary:"
cat /tmp/vuln-check.txt >> "$REPORT_FILE" 2>/dev/null || echo "No vulnerabilities" >> "$REPORT_FILE"
echo ""
echo "Full report saved to: $REPORT_FILE"
echo ""
echo "Next steps:"
echo "1. Review warnings above"
echo "2. Address high/critical findings"
echo "3. Update SECURITY.md with findings"
echo "4. Re-run before release"
echo ""

# Exit with error if critical issues found
if grep -q "Critical" /tmp/vuln-check.txt 2>/dev/null; then
    echo "❌ CRITICAL VULNERABILITIES FOUND - DO NOT RELEASE"
    exit 1
fi

echo "✅ Security audit passed"
exit 0
