# JavaScript Resources for Bitcoin Rewards Plugin

This directory contains JavaScript resources for generating animated QR codes for exported Cashu tokens.

## Building the Bundle

To build the JavaScript bundle, you need to:

1. Install Node.js and npm (if not already installed)
2. Install dependencies:
   ```bash
   npm install
   ```
3. Build the bundle:
   ```bash
   npx webpack
   ```

This will generate `bundle.js` which is used by the ExportedToken view to display scannable QR codes.

## Dependencies

- `@ngraveio/bc-ur`: For UR (Uniform Resources) encoding of large tokens
- `qrcode`: For QR code generation
- `buffer` and `process`: Polyfills for Node.js compatibility in the browser

## Notes

- The bundle uses animated QR codes for large tokens (split into multiple fragments)
- The QR code is generated client-side and is CSP-compatible (no external CDN)
- The bundle is embedded as a resource in the plugin assembly

