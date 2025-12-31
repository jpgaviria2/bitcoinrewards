# Bitcoin Rewards Display Application

A standalone web application that displays Bitcoin reward QR codes for physical store customers to scan and claim their rewards immediately.

## Overview

This display application works in conjunction with the BTCPay Server Bitcoin Rewards plugin. When customers make purchases at a physical store using Square (without providing an email), the plugin broadcasts the reward to this display device via SignalR, allowing customers to scan a QR code and claim their reward on the spot.

## Features

- **Real-time Updates**: Receives rewards instantly via SignalR WebSocket connection
- **Clean UI**: Full-screen, tablet-optimized interface
- **QR Code Display**: Large, scannable LNURL-withdraw QR codes
- **Auto-Clear**: Automatically returns to waiting state after configurable duration
- **Persistent Configuration**: Stores settings in localStorage
- **Auto-Reconnect**: Handles network interruptions gracefully
- **Dark Mode**: Supports system dark mode preference

## Quick Start

### Option 1: Direct Browser Access (Easiest)

1. **Host the files** on any web server (or open `index.html` directly in a browser)

2. **Open in browser** with URL parameters:
   ```
   index.html?server=https://your-btcpay-server.com&store=YOUR_STORE_ID
   ```

3. **Go fullscreen** (F11 on most browsers)

### Option 2: Manual Configuration

1. Open `index.html` in a web browser
2. Enter your BTCPay Server URL (e.g., `https://btcpay.example.com`)
3. Enter your Store ID from BTCPay Server
4. Click "Connect"
5. Go fullscreen

## Deployment Options

### Local File Hosting

Simply open the `index.html` file in any modern web browser. No server required!

### Web Server Hosting

Upload all files to any web server (Apache, Nginx, or even a simple HTTP server):

```bash
# Python 3
python -m http.server 8000

# Node.js
npx http-server

# PHP
php -S localhost:8000
```

Then navigate to: `http://localhost:8000?server=YOUR_SERVER&store=YOUR_STORE`

### Cloud Hosting

Deploy to any static hosting service:
- GitHub Pages
- Netlify
- Vercel
- AWS S3 + CloudFront
- Azure Static Web Apps

## Browser Setup for Kiosk Mode

### Chrome/Edge (Recommended)

**Windows:**
```bash
chrome.exe --kiosk "file:///C:/path/to/index.html?server=https://btcpay.example.com&store=ABC123"
```

**macOS:**
```bash
open -a "Google Chrome" --args --kiosk "file:///path/to/index.html?server=https://btcpay.example.com&store=ABC123"
```

**Linux:**
```bash
chromium-browser --kiosk "file:///path/to/index.html?server=https://btcpay.example.com&store=ABC123"
```

### iPad/iOS

1. Add to Home Screen (Safari → Share → Add to Home Screen)
2. Open from Home Screen (launches in fullscreen mode)
3. Use Guided Access (Settings → Accessibility → Guided Access) to lock the device to this app

### Android Tablets

1. Use Chrome's "Add to Home Screen" feature
2. Enable "Kiosk Mode" via device settings or use a kiosk app like:
   - Fully Kiosk Browser
   - Kiosk Browser Lockdown
   - SureLock

## Configuration

### URL Parameters

- `server` (required): Your BTCPay Server URL
- `store` (required): Your Store ID from BTCPay

Example:
```
index.html?server=https://btcpay.example.com&store=ABC123
```

### Display Duration

The display duration is controlled by the BTCPay plugin settings (default: 60 seconds). Configure this in:
BTCPay Server → Store Settings → Bitcoin Rewards → Physical Store Display Settings

## How It Works

1. **Customer Pays**: Customer completes payment at physical store (Square POS)
2. **Webhook Received**: BTCPay plugin receives Square webhook (no email provided)
3. **Pull Payment Created**: Plugin creates a pull payment with LNURL
4. **Broadcast**: Plugin sends reward via SignalR to all connected displays
5. **Display Shows**: Display device shows QR code for 60 seconds
6. **Customer Scans**: Customer scans with Lightning wallet to claim
7. **Auto-Clear**: Display returns to waiting state

## Network Requirements

- **Outbound HTTPS**: Must reach BTCPay Server (port 443)
- **WebSocket Support**: SignalR uses WebSocket protocol
- **No Firewall Blocking**: Ensure websockets are allowed

### Firewall Considerations

If your BTCPay Server is behind a firewall or reverse proxy, ensure:
- WebSocket connections are permitted
- SignalR endpoint is accessible: `/plugins/bitcoin-rewards/hubs/display`
- CORS is configured if hosting display app on different domain

## Troubleshooting

### Display Shows "Disconnected"

- Check BTCPay Server URL is correct and accessible
- Verify Store ID is correct
- Check browser console for errors (F12)
- Ensure websocket connections are not blocked by firewall

### QR Code Not Generating

- Check browser console for errors
- Ensure QRCode.js library is loaded (CDN or local)
- Verify claim link is valid

### Rewards Not Appearing

- Verify plugin is enabled in BTCPay Server
- Check "Enable Display Mode" or "Fallback to Display When No Email" is enabled
- Ensure Square webhooks are working
- Check BTCPay Server logs for errors

### Connection Keeps Dropping

- Check network stability
- Verify BTCPay Server is consistently reachable
- Consider using wired ethernet instead of WiFi

## Hardware Recommendations

### Tablets
- iPad (9th gen or newer) - Excellent battery life, great display
- Samsung Galaxy Tab - Good value, large screen options
- Amazon Fire HD - Budget-friendly option

### Small Monitors
- 10-15" portable monitors with Raspberry Pi
- Old tablets/iPads repurposed
- Small touchscreen displays

### Requirements
- Modern web browser (Chrome, Safari, Edge, Firefox)
- Stable network connection
- Minimum 7" screen (10"+ recommended for better QR scanning)

## Security Notes

- Display only shows publicly-shareable claim links
- No sensitive data is transmitted or stored
- Claim links expire after 30 days (BTCPay default)
- Anyone can scan the QR code (first-come, first-served)
- Consider physical security of display device

## Development

### Files Structure
```
display-app/
├── index.html      # Main HTML structure
├── style.css       # Styling and responsive design
├── config.js       # Configuration management
├── app.js          # Main application logic
└── README.md       # This file
```

### Dependencies (loaded from CDN)
- **SignalR Client**: Microsoft's SignalR JavaScript client
- **QRCode.js**: QR code generation library

### Local Development
```bash
# Simple HTTP server
python -m http.server 8000

# Then visit:
# http://localhost:8000?server=https://your-btcpay&store=yourstore
```

## Browser Compatibility

- ✅ Chrome/Edge 90+
- ✅ Safari 14+
- ✅ Firefox 88+
- ✅ iOS Safari 14+
- ✅ Chrome for Android 90+

## Support

For issues related to:
- **Display App**: Check browser console, verify configuration
- **BTCPay Plugin**: Check BTCPay Server logs, plugin settings
- **Square Integration**: Verify Square webhook configuration

## License

This application is part of the BTCPay Server Bitcoin Rewards plugin project.

## Contributing

Issues and pull requests welcome!

