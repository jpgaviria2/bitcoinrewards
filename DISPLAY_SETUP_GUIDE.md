# Physical Store Display Setup Guide

Complete guide for setting up the Bitcoin Rewards display system for physical stores where customer email/phone information is not collected.

## Overview

When customers pay at a physical store using Square without providing contact information, this system displays a QR code on a nearby screen that customers can scan with their Lightning wallet to claim their Bitcoin reward immediately.

## Architecture

```
[Customer] → [Square POS] → [Square API] → [BTCPay Plugin] → [SignalR] → [Display Device]
                                                ↓
                                        [Pull Payment Created]
                                                ↓
                                         [LNURL QR Code]
```

## Prerequisites

### Required Components

1. **BTCPay Server** (v2.3.0+) with Bitcoin Rewards plugin installed
2. **Square Account** with API credentials and webhook configured
3. **Display Device** - Tablet, iPad, or small monitor
4. **Network Connection** - Stable connection between display and BTCPay Server
5. **Payout Processor** - Lightning or on-chain Bitcoin configured in BTCPay

### Display Device Requirements

**Minimum:**
- 7" screen (10"+ recommended)
- Modern web browser (Chrome, Safari, Edge, Firefox)
- WiFi or Ethernet connection
- Touch screen (optional, for configuration only)

**Recommended Devices:**
- iPad (9th gen or newer) - $329+
- Samsung Galaxy Tab A8 - $229+
- Amazon Fire HD 10 - $149+
- Raspberry Pi + 10" touchscreen - $150+

## Step-by-Step Setup

### Part 1: BTCPay Server Configuration

#### 1.1 Install Bitcoin Rewards Plugin

If not already installed:

1. Navigate to **Server Settings → Plugins**
2. Find "Bitcoin Rewards" in available plugins
3. Click "Install" and restart BTCPay Server

#### 1.2 Configure Square Integration

1. Go to **Stores → [Your Store] → Plugins → Bitcoin Rewards**
2. In the "Platform Integration" section:
   - ✅ Check "Enable Square"
   - Enter Square Application ID
   - Enter Square Access Token
   - Enter Square Location ID
   - Select Environment (Production/Sandbox)
   - Enter Square Webhook Signature Key
3. Set "Shopify / Square Reward Percentage" (e.g., 5%)
4. Set minimum transaction amount if desired
5. Save settings

#### 1.3 Configure Payout Processor

1. Go to **Stores → [Your Store] → Payout Processors**
2. Add a payout processor:
   - **Lightning Address Payout Processor** (recommended)
   - Or **Automated Lightning Payout Processor**
   - Or **On-chain Bitcoin** (slower, higher fees)
3. Configure the processor settings
4. Enable and save

#### 1.4 Enable Display Mode

1. Return to **Bitcoin Rewards** settings
2. Scroll to "Physical Store Display Settings"
3. Configure:
   - ✅ **Enable Display Mode** (always broadcasts)
     - OR -
   - ✅ **Fallback to Display When No Email/Phone** (recommended - only when needed)
   
4. Set **Display Duration** (default: 60 seconds)
5. Save settings

#### 1.5 Note Your Configuration

You'll need these values:
- **BTCPay Server URL**: `https://your-btcpay-server.com`
- **Store ID**: Found in store settings URL or settings page
- **SignalR Hub URL**: `https://your-btcpay-server.com/plugins/bitcoin-rewards/hubs/display`

### Part 2: Display Device Setup

#### 2.1 Prepare the Device

**For iPad/iOS:**
1. Update to latest iOS
2. Open Settings → Display & Brightness
3. Set Auto-Lock to "Never"
4. Enable "Raise to Wake" = Off
5. Settings → Battery → Low Power Mode = Off

**For Android Tablet:**
1. Update to latest Android
2. Settings → Display → Sleep = Never
3. Install Chrome browser (if not default)
4. Consider installing a kiosk app (optional):
   - Fully Kiosk Browser
   - Kiosk Browser Lockdown

**For Raspberry Pi:**
1. Install Raspberry Pi OS Lite
2. Install Chromium browser
3. Configure auto-start in kiosk mode
4. Disable screen blanking

#### 2.2 Install Display Application

**Option A: Download from GitHub (Recommended)**

```bash
# On your display device or computer:
git clone https://github.com/yourusername/btcpay-rewards-display.git
cd btcpay-rewards-display
```

**Option B: Copy from Plugin Folder**

Copy the entire `display-app/` folder from the plugin repository to your display device or web server.

**Option C: Host on Web Server**

Upload `display-app/` contents to any web server:
- GitHub Pages
- Netlify
- Your own Apache/Nginx server
- BTCPay Server itself (in a public directory)

#### 2.3 Configure Display Application

**Method 1: URL Parameters (Easiest)**

Create a bookmark or startup URL:
```
file:///path/to/display-app/index.html?server=https://btcpay.example.com&store=YOUR_STORE_ID
```

Or if hosted on web server:
```
https://your-display-app-host.com/index.html?server=https://btcpay.example.com&store=YOUR_STORE_ID
```

**Method 2: Manual Configuration**

1. Open `index.html` in browser
2. Enter BTCPay Server URL
3. Enter Store ID
4. Click "Connect"
5. Settings are saved in localStorage

#### 2.4 Set Up Kiosk Mode

**Chrome/Edge:**

Create a desktop shortcut with:
```bash
# Windows
"C:\Program Files\Google\Chrome\Application\chrome.exe" --kiosk --app="file:///C:/path/to/index.html?server=https://btcpay.example.com&store=ABC123"

# macOS
open -a "Google Chrome" --args --kiosk --app="file:///path/to/index.html?server=https://btcpay.example.com&store=ABC123"

# Linux
chromium-browser --kiosk --app="file:///path/to/index.html?server=https://btcpay.example.com&store=ABC123"
```

**Safari (iOS):**
1. Open display app in Safari
2. Tap Share button
3. "Add to Home Screen"
4. Open from Home Screen (fullscreen mode)
5. Enable Guided Access for kiosk mode:
   - Settings → Accessibility → Guided Access
   - Enable and set passcode
   - Triple-click home button to start

**Firefox:**
- Press F11 for fullscreen
- Use an addon like "R-kiosk" for locked kiosk mode

#### 2.5 Configure Auto-Start (Optional)

**Windows:**
1. Create Chrome kiosk shortcut (above)
2. Move shortcut to: `C:\Users\[User]\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup`

**macOS:**
1. System Preferences → Users & Groups → Login Items
2. Add Chrome with kiosk arguments

**Raspberry Pi:**
Edit `/etc/xdg/lxsession/LXDE-pi/autostart`:
```bash
@chromium-browser --kiosk --app=file:///home/pi/display-app/index.html?server=https://btcpay.example.com&store=ABC123
@xset s off
@xset -dpms
@xset s noblank
```

### Part 3: Physical Installation

#### 3.1 Choose Location

**Ideal Placement:**
- Near Square POS terminal (within customer view)
- Easily accessible for customer scanning
- Protected from accidental damage
- Good lighting (not direct sunlight on screen)
- Within WiFi/network range

**Avoid:**
- Behind counter (customers can't reach)
- Too far from POS (customers forget)
- Direct sunlight (washes out screen)
- High theft risk areas

#### 3.2 Mount the Display

**Tablet Stands:**
- Desktop tablet stand ($15-50)
- Wall-mounted tablet holder ($20-80)
- Lockable security enclosure ($100-300)

**Small Monitor:**
- VESA mount on wall or counter
- Articulating arm for flexibility
- Cable management for clean look

**Security Considerations:**
- Use Kensington lock cable
- Lockable enclosure for high-traffic areas
- Adhesive mount with security screws
- Insurance coverage

#### 3.3 Power Management

**Options:**
- AC power (recommended for always-on displays)
- USB power bank (for portable setup)
- PoE (Power over Ethernet) if supported

**Battery Devices (iPad/Tablet):**
- Keep plugged in during business hours
- Enable "Optimized Battery Charging" (iOS)
- Replace after 2-3 years if battery degrades

#### 3.4 Network Connection

**Wired (Recommended):**
- USB-to-Ethernet adapter
- More reliable than WiFi
- No interference issues

**Wireless:**
- Use 5GHz WiFi if available
- Place near WiFi access point
- Use WiFi extender if needed
- Monitor signal strength

### Part 4: Testing

Follow the [Display Testing Guide](DISPLAY_TESTING_GUIDE.md) to verify:

1. Display connects to BTCPay Server
2. Test reward shows on display
3. QR code is scannable
4. Timer counts down correctly
5. Auto-clear works
6. Reconnection works after network interruption

### Part 5: Staff Training

#### 5.1 Train Your Staff

**Key Points to Communicate:**
- When customers pay without providing email, reward QR appears on display
- Customers should scan QR with Lightning wallet to claim
- QR code disappears after 60 seconds
- No action needed from staff (automatic)

**Troubleshooting Staff Should Know:**
- If display shows "Disconnected", notify manager
- Don't turn off or unplug display during business hours
- If screen goes black, try tapping to wake (but should be configured to stay on)

#### 5.2 Customer-Facing Materials

Create signage near display:

**Example Sign:**
```
🎉 Bitcoin Reward!

1. Complete your purchase at the register
2. Watch this screen for your reward QR code
3. Scan with your Lightning wallet
4. Claim your Bitcoin instantly!

Supported wallets: Phoenix, Breez, Zeus, Blue Wallet, Muun, and others
```

### Part 6: Maintenance

#### 6.1 Daily Checks

- [ ] Display is on and showing "Connected"
- [ ] Screen is clean and readable
- [ ] Device is charged/plugged in
- [ ] No physical damage

#### 6.2 Weekly Checks

- [ ] Test with a sample transaction
- [ ] Verify QR code displays correctly
- [ ] Check BTCPay Server logs for errors
- [ ] Clean screen and device

#### 6.3 Monthly Checks

- [ ] Update browser if needed
- [ ] Check for plugin updates
- [ ] Review reward history in BTCPay
- [ ] Verify customer feedback

#### 6.4 Troubleshooting

See [Display Testing Guide](DISPLAY_TESTING_GUIDE.md) for detailed troubleshooting steps.

**Quick Fixes:**
- **Display disconnected**: Refresh page, check network
- **QR not showing**: Verify display mode enabled in settings
- **Screen went black**: Check device sleep settings
- **Customer can't scan**: Increase screen brightness, clean screen

## Advanced Configuration

### Multiple Store Locations

Each location needs:
1. Separate Store ID in BTCPay Server
2. Display device configured with that Store ID
3. Individual Square webhook endpoints

Or use single store with location tagging in Square metadata.

### CORS Configuration

If hosting display app on different domain than BTCPay:

Add to BTCPay Server configuration:
```json
{
  "CORS": {
    "AllowedOrigins": ["https://your-display-app-domain.com"]
  }
}
```

### Custom Branding

Edit `display-app/style.css`:
```css
:root {
    --primary-color: #your-color;
    --secondary-color: #your-color;
}
```

Add logo to `index.html`:
```html
<img src="your-logo.png" alt="Logo" class="logo">
```

### Analytics

Track usage with Google Analytics or similar:

Add to `index.html`:
```html
<!-- Google Analytics -->
<script async src="https://www.googletagmanager.com/gtag/js?id=YOUR-GA-ID"></script>
<script>
  window.dataLayer = window.dataLayer || [];
  function gtag(){dataLayer.push(arguments);}
  gtag('js', new Date());
  gtag('config', 'YOUR-GA-ID');
</script>
```

Track events in `app.js`:
```javascript
// When reward is displayed
gtag('event', 'reward_displayed', {
    'amount_sats': reward.RewardSatoshis
});
```

## Security Best Practices

1. **Network Security:**
   - Use HTTPS for BTCPay Server
   - Secure WiFi with WPA3
   - Consider VPN for remote displays

2. **Physical Security:**
   - Lock device in kiosk mode
   - Use lockable enclosure
   - Place in monitored area
   - Insurance coverage

3. **Access Control:**
   - Limit admin access to device
   - Use strong passcodes
   - Regular security updates

4. **Data Privacy:**
   - No customer data stored on display
   - Only claim links transmitted
   - Comply with local privacy laws

## Cost Breakdown

**Example Budget (Per Location):**

| Item | Cost |
|------|------|
| iPad (10th gen) | $329 |
| Tablet stand/mount | $50 |
| USB-C cable & charger | $20 |
| Setup time (2 hours) | $100 |
| **Total** | **~$500** |

**Budget Option:**
| Item | Cost |
|------|------|
| Amazon Fire HD 10 | $149 |
| Basic stand | $15 |
| Setup time | $50 |
| **Total** | **~$214** |

## ROI Considerations

**Benefits:**
- Increased customer satisfaction
- Bitcoin adoption education
- Competitive advantage
- Marketing opportunity
- No email collection needed

**Costs:**
- Hardware (one-time)
- Setup time (one-time)
- Minimal maintenance
- Network/hosting (if applicable)

**Break-even Analysis:**
- If reward increases repeat customers by even 5%
- Hardware cost typically recovered in 1-3 months
- Ongoing costs near zero

## Frequently Asked Questions

**Q: What if customer doesn't have a Lightning wallet?**  
A: They can install one in ~2 minutes (Phoenix, Breez are easiest). This is also a great education opportunity.

**Q: What if multiple customers pay at once?**  
A: Display shows the most recent reward. Earlier rewards can still be claimed by email if provided, or via BTCPay history.

**Q: How long is the reward valid?**  
A: Pull payments expire after 30 days by default (configurable in BTCPay).

**Q: Can customer claim after the 60 seconds?**  
A: Yes! If they note the transaction ID, they can claim later via BTCPay Server.

**Q: What if display device fails?**  
A: Have "Fallback to Display When No Email" enabled so rewards still work via email when provided. Replace/repair display device.

**Q: Does this work with other payment processors?**  
A: Currently designed for Square. Could be extended to other processors with similar webhooks.

**Q: Can I use my own domain for the display app?**  
A: Yes! Host the files anywhere and configure CORS in BTCPay if needed.

**Q: Is this secure?**  
A: Yes. Only publicly-shareable claim links are displayed. No sensitive data transmitted.

## Support Resources

- **Plugin Documentation**: [Main README](README.md)
- **Testing Guide**: [DISPLAY_TESTING_GUIDE.md](DISPLAY_TESTING_GUIDE.md)
- **Display App README**: [display-app/README.md](display-app/README.md)
- **BTCPay Server Docs**: https://docs.btcpayserver.org
- **GitHub Issues**: [Report bugs or request features]

## Conclusion

Once set up, this system provides a seamless experience for customers to claim Bitcoin rewards without providing email addresses. The display device operates autonomously, requiring minimal maintenance.

**Next Steps:**
1. Complete Part 1-4 of this guide
2. Run all tests from testing guide
3. Train staff (Part 5)
4. Deploy to production
5. Monitor and maintain (Part 6)

Good luck with your physical store Bitcoin rewards system! 🚀

