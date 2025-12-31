# Changelog

All notable changes to the Bitcoin Rewards Plugin will be documented in this file.

## [1.1.0] - 2025-12-31

### Added
- Customizable HTML template editor for rewards display page
- Live preview for display templates
- Token replacement system for dynamic content ({AMOUNT_SATS}, {QR_CODE}, etc.)
- Sample template loader with modern responsive design
- Countdown timer with color-coded alerts (blue/yellow/red)
- Manual "Done" button to dismiss QR codes
- Pull payment claim status checking
- Configurable display timeout (10-300 seconds)
- Display auto-refresh settings (5-60 seconds)
- Display timeframe configuration (5-1440 minutes)

### Changed
- Display page now supports fully custom HTML layouts
- Improved settings UI organization
- Enhanced display page with better UX

### Removed
- External SignalR-based display system (replaced with native BTCPay display)
- All SignalR hubs, services, and related code
- External display app folder and documentation

### Fixed
- BTCPay rewards now work without buyer email
- Display page link restored in settings UI

## [1.0.0] - Initial Release

### Added
- Square POS integration for rewards
- BTCPay invoice rewards support
- Lightning pull payments for reward redemption
- Email notification system
- Configurable reward percentages per platform
- Minimum transaction amount and maximum reward caps
- Payout processor integration
- Native BTCPay display page with QR codes

