# Physical Store Display Testing Guide

This guide walks through testing the complete flow for the physical store rewards display feature.

## Prerequisites

Before testing, ensure you have:

1. BTCPay Server instance running with the Bitcoin Rewards plugin installed
2. Square account with test/sandbox credentials configured
3. A payout processor configured in BTCPay (Lightning, On-chain, etc.)
4. Two browser windows/devices:
   - One for the display app
   - One for simulating Square webhooks or using the Square Dashboard

## Testing Steps

### 1. Enable Display Mode in Plugin Settings

1. Navigate to BTCPay Server → Stores → [Your Store] → Plugins → Bitcoin Rewards
2. Scroll to "Physical Store Display Settings"
3. Enable either:
   - ✅ "Enable Display Mode" (always broadcasts)
   - ✅ "Fallback to Display When No Email/Phone" (recommended)
4. Set "Display Duration" to 60 seconds (or your preference)
5. Note the Store ID displayed in the "Display Device Setup" section
6. Save settings

### 2. Set Up Display Device

**Option A: Same Machine (Testing)**
1. Open a new browser window
2. Navigate to `display-app/index.html` 
3. Or use URL parameters:
   ```
   file:///path/to/display-app/index.html?server=https://your-btcpay-server.com&store=YOUR_STORE_ID
   ```
4. If using manual config, enter:
   - Server URL: Your BTCPay Server URL
   - Store ID: Your Store ID
5. Click "Connect"
6. You should see "Waiting for rewards..." with a green "Connected" indicator

**Option B: Separate Device (Production-like)**
1. Host display app files on a web server or open directly
2. Follow same configuration steps
3. Consider going fullscreen (F11)

### 3. Verify SignalR Connection

**Check Browser Console (F12):**
```
SignalR connected
Joined store: YOUR_STORE_ID
```

If you see errors:
- Verify BTCPay Server URL is correct and accessible
- Check that SignalR hub is registered (plugin loaded correctly)
- Ensure no firewall blocking WebSocket connections

### 4. Simulate Square Payment Without Email

**Method A: Using BTCPay Test Reward (Easiest)**

1. In BTCPay, go to Bitcoin Rewards → Create Test Reward
2. Set Platform to "Square"
3. Set amount (e.g., $10.00)
4. **Leave Email and Phone EMPTY**
5. Submit

The display should immediately show:
- 🎉 Bitcoin Reward! header
- QR code with claim link
- Reward amount in sats and BTC
- 60-second countdown timer

**Method B: Square Webhook Simulation**

Use a tool like Postman or curl to send a webhook:

```bash
curl -X POST "https://your-btcpay-server.com/plugins/bitcoin-rewards/YOUR_STORE_ID/webhooks/square" \
  -H "Content-Type: application/json" \
  -H "X-Square-Signature: YOUR_SIGNATURE" \
  -d '{
    "type": "payment.updated",
    "data": {
      "object": {
        "payment": {
          "id": "TEST_PAYMENT_123",
          "status": "COMPLETED",
          "amount_money": {
            "amount": 1000,
            "currency": "USD"
          }
        }
      }
    }
  }'
```

**Note:** You'll need to compute the correct HMAC signature using your Square webhook signature key.

**Method C: Real Square Transaction (Production Testing)**

1. Make a purchase using Square POS
2. Complete payment
3. **DO NOT** enter customer email or phone
4. Square sends webhook to BTCPay
5. Watch display device

### 5. Verify Display Behavior

**On the Display Screen:**

✅ **Screen switches** from "Waiting" to "Display"  
✅ **QR code displays** large and centered  
✅ **Amount shows** correct satoshis and BTC  
✅ **Timer counts down** from 60 to 0  
✅ **After timeout** returns to "Waiting" screen  

**In Browser Console:**
```
Received reward: {
  ClaimLink: "https://...",
  RewardSatoshis: 12345,
  ...
}
```

### 6. Test QR Code Scanning

1. While QR code is displayed, use a Lightning wallet that supports LNURL-withdraw:
   - Phoenix
   - Breez
   - Zeus
   - Blue Wallet
   - Muun

2. Scan the QR code

3. Wallet should:
   - Recognize LNURL-withdraw
   - Show reward amount
   - Prompt to claim

4. Confirm claim in wallet

5. Verify in BTCPay:
   - Go to Bitcoin Rewards → History
   - Find the reward record
   - Status should update to "Redeemed"

### 7. Test Multiple Rapid Payments

**Purpose:** Verify "latest only" behavior

1. Create 3 test rewards quickly (5-10 seconds apart)
2. All should have no email/phone
3. Display should:
   - Show first reward
   - Immediately replace with second reward
   - Immediately replace with third reward
   - Show only the most recent QR code

### 8. Test Reconnection

**Purpose:** Verify auto-reconnect on network interruption

1. With display connected, disconnect network/WiFi
2. Connection indicator should turn red
3. Status changes to "Disconnected"
4. Reconnect network
5. Within 30 seconds, should auto-reconnect
6. Status returns to "Connected"
7. Create test reward to verify still receiving

### 9. Test with Email Fallback

**Purpose:** Verify display works even when email is present if enabled

1. Set "Enable Display Mode" to **ON** (not just fallback)
2. Create test reward **WITH** an email address
3. Verify:
   - Email is sent (check email)
   - Display ALSO shows the QR code
   - Both delivery methods work simultaneously

### 10. Test Display Duration Configuration

1. In plugin settings, change Display Duration to 30 seconds
2. Save settings
3. Create test reward without email
4. Verify timer counts down from 30 instead of 60
5. Screen clears after 30 seconds

## Expected Results Summary

| Test Case | Expected Result |
|-----------|----------------|
| Display connects | Green "Connected" indicator, console shows "SignalR connected" |
| Test reward (no email) | Display shows QR within 2 seconds |
| QR code displays | Large, scannable, with correct claim link |
| Amount displayed | Matches reward calculation (transaction × percentage) |
| Timer countdown | Counts from configured duration to 0 |
| Auto-clear | Returns to waiting screen after timeout |
| Wallet scan | Wallet recognizes LNURL-withdraw |
| Claim reward | Reward status updates in BTCPay history |
| Multiple rapid | Shows only latest reward |
| Network disconnect | Auto-reconnects within 30 seconds |
| Display mode ON | Shows QR even with email present |

## Troubleshooting Common Issues

### Display Shows "Disconnected"

**Problem:** Cannot connect to SignalR hub

**Solutions:**
- Verify BTCPay Server URL is correct
- Check Store ID matches
- Ensure plugin is loaded (restart BTCPay if needed)
- Check browser console for errors
- Verify no firewall blocking port 443 or WebSockets

### Reward Not Broadcasting to Display

**Problem:** Test reward created but display doesn't show it

**Check:**
1. Plugin settings: Is "Enable Display Mode" or "Fallback to Display When No Email" enabled?
2. Browser console: Any errors?
3. BTCPay logs: Check for broadcasting errors
   ```
   docker logs btcpayserver_btcpayserver | grep -i "reward"
   ```
4. Reward record: Check email/phone fields - are they empty?

### QR Code Not Generating

**Problem:** Display screen shows but QR code is missing

**Solutions:**
- Check browser console for QRCode.js errors
- Verify internet connection (for CDN libraries)
- Check claim link is valid URL
- Try different browser

### Claim Link Invalid

**Problem:** Wallet scans QR but shows error

**Check:**
1. Payout processor: Is one configured and enabled?
2. Pull payment: Was it created successfully?
3. Claim link format: Should be `https://your-server/pull-payments/{id}`
4. BTCPay logs: Check pull payment creation

### Timer Doesn't Count Down

**Problem:** Timer shows 60 but never decrements

**Solutions:**
- Check JavaScript console for errors
- Verify timer SVG elements are rendering
- Try refreshing display page
- Check CSS is loaded properly

## Testing Checklist

Before going to production, verify:

- [ ] Display connects successfully
- [ ] Test reward (no email) shows on display
- [ ] Real Square payment (no email) shows on display
- [ ] QR code is scannable with Lightning wallet
- [ ] Reward can be claimed successfully
- [ ] Timer counts down correctly
- [ ] Display clears after timeout
- [ ] Display auto-reconnects after network interruption
- [ ] Multiple rapid rewards show latest only
- [ ] Plugin logs show successful broadcast
- [ ] Display works in kiosk/fullscreen mode
- [ ] Display is positioned near POS terminal
- [ ] Staff trained on how it works

## Production Deployment Tips

1. **Use dedicated device**: Tablet or small monitor mounted near POS
2. **Wired connection**: Ethernet more reliable than WiFi
3. **Fullscreen mode**: Use browser kiosk mode
4. **Auto-start**: Configure device to auto-load display on boot
5. **Physical security**: Mount tablet securely, use kiosk mode to prevent tampering
6. **Clear signage**: Add sign near display explaining how to scan
7. **Staff training**: Ensure cashiers understand and can explain to customers
8. **Monitor connectivity**: Check display connection daily
9. **Battery/power**: Keep device charged or plugged in
10. **Backup plan**: Have email collection as backup if display fails

## Performance Benchmarks

Expected latencies:
- **Square webhook → BTCPay**: < 1 second
- **Pull payment creation**: < 500ms
- **SignalR broadcast**: < 100ms
- **QR code generation**: < 200ms
- **Total (payment → display)**: < 2 seconds

If experiencing delays:
- Check BTCPay Server performance
- Monitor network latency
- Verify no resource constraints (CPU, memory)

## Next Steps

After successful testing:

1. Document your specific setup in internal wiki
2. Create customer-facing materials (signage, instructions)
3. Train staff on troubleshooting common issues
4. Set up monitoring/alerting for display connection
5. Plan for scaling to multiple locations if needed

## Support

If issues persist after testing:

1. Check BTCPay Server logs: `docker logs btcpayserver_btcpayserver`
2. Check browser console on display device
3. Review plugin configuration
4. Verify network connectivity
5. Test with different browsers/devices
6. Consult plugin documentation and GitHub issues

