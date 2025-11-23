# Bitcoin Rewards Plugin for BTCPay Server

A standalone plugin that enables Bitcoin-backed rewards for customers when they make purchases through Shopify.

## Features

- **Automatic Rewards**: Automatically calculate and send Bitcoin rewards based on order amounts
- **Shopify Integration**: Receive order webhooks from Shopify and process rewards
- **Email Notifications**: Send email notifications to customers with their reward details
- **Configurable**: Set reward percentage, minimum order amounts, and maximum reward limits
- **Secure**: Webhook signature verification for Shopify

## Installation

### Option 1: Install from Plugin Builder (Recommended)

1. Navigate to your BTCPay Server instance
2. Go to **Server Settings** > **Plugins**
3. Search for "Bitcoin Rewards" or browse available plugins
4. Click **Install** to download and install the plugin
5. The plugin will be automatically enabled

### Option 2: Manual Installation

1. Download the latest `.btcpay` file from the [Plugin Builder](https://plugin-builder.btcpayserver.org/)
2. Copy the `.btcpay` file to your BTCPay Server plugins directory:
   - **Docker**: `/btcpay/plugins/`
   - **Manual install**: `{BTCPayServerDirectory}/Plugins/`
3. Restart BTCPay Server
4. The plugin will appear in your store settings

## Building from Source

### Prerequisites

- .NET 8.0 SDK or later
- BTCPay Server source code (for DLL references) or installed BTCPay Server
- Git

### Local Development Build

1. Clone this repository:
   ```bash
   git clone https://github.com/yourusername/btcpayserver-plugin-bitcoinrewards.git
   cd btcpayserver-plugin-bitcoinrewards
   ```

2. Set the BTCPay Server path (one of the following):
   - Set environment variable: `BTCPayServerPath=C:\path\to\btcpayserver\BTCPayServer\bin\Release\net8.0`
   - Or update the `BTCPayServerLibPath` in the `.csproj` file

3. Build the plugin:
   ```bash
   cd Plugins/BTCPayServer.Plugins.BitcoinRewards
   dotnet build -c Release
   ```

4. The `.btcpay` file will be generated in `bin/Release/net8.0/`

### Using Plugin Builder

The plugin can be built automatically using the [BTCPay Server Plugin Builder](https://plugin-builder.btcpayserver.org/):

1. Register your plugin in the Plugin Builder
2. Provide the repository URL: `https://github.com/yourusername/btcpayserver-plugin-bitcoinrewards`
3. Specify the plugin directory: `BTCPayServer.Plugins.BitcoinRewards`
4. The Plugin Builder will build and publish the plugin automatically

## Configuration

### Store Settings

1. Navigate to your store settings
2. Click on **"Bitcoin Rewards"** in the navigation menu
3. Configure the following settings:
   - **Enable Bitcoin Rewards**: Toggle to enable/disable the plugin
   - **Reward Percentage**: Percentage of order amount to give as reward (e.g., 0.01 = 1%)
   - **Minimum Order Amount**: Minimum order amount required to receive a reward
   - **Maximum Reward Amount**: Maximum reward amount in BTC
   - **Shopify Enabled**: Enable Shopify integration
   - **Webhook Secret**: Secret key for verifying webhook signatures
   - **Email Settings**: Configure email from address, subject, and body template

### Shopify Webhook Setup

1. In your Shopify admin, go to **Settings** > **Notifications** > **Webhooks**
2. Create a new webhook with:
   - **Event**: Order creation
   - **URL**: `https://your-btcpay-server.com/plugins/bitcoinrewards/webhooks/shopify/{storeId}`
   - **Format**: JSON
   - **API version**: Latest
3. Copy the webhook secret and paste it into the plugin settings

## How It Works

1. When an order is created in Shopify, a webhook is sent to BTCPay Server
2. The plugin verifies the webhook signature for security
3. The order data is parsed to extract:
   - Order ID and amount
   - Customer email or phone number
   - Customer name
4. A reward amount is calculated based on the configured percentage
5. Bitcoin is sent to the customer (or a Bitcoin address is generated)
6. An email notification is sent to the customer with reward details

## Email Template Variables

The email body template supports the following placeholders:
- `{RewardAmount}` - The Bitcoin reward amount
- `{BitcoinAddress}` - The Bitcoin address where the reward was sent
- `{OrderId}` - The order ID from Shopify

## Security

- Webhook signatures are verified using HMAC SHA256
- Store-specific webhook secrets ensure only authorized webhooks are processed
- Customer data is handled securely and only used for reward distribution

## Development

### Project Structure

```
btcpayserver-plugin-bitcoinrewards/
├── Plugins/
│   ├── BTCPayServer.Plugins.BitcoinRewards/
│   │   ├── BitcoinRewardsPlugin.cs          # Main plugin class
│   │   ├── BitcoinRewardsService.cs         # Business logic service
│   │   ├── BitcoinRewardsExtensions.cs     # Extension methods for settings
│   │   ├── BTCPayServer.Plugins.BitcoinRewards.csproj
│   │   ├── Controllers/
│   │   │   ├── UIBitcoinRewardsController.cs  # UI controller for settings
│   │   │   └── WebhookController.cs           # Webhook endpoints
│   │   ├── Models/
│   │   │   ├── BitcoinRewardsSettings.cs    # Settings model
│   │   │   ├── OrderData.cs                 # Order data model
│   │   │   └── RewardRecord.cs              # Reward record model
│   │   ├── Services/
│   │   ├── Repositories/
│   │   └── Views/
│   │       ├── UIBitcoinRewards/
│   │       │   ├── EditSettings.cshtml      # Settings view
│   │       │   └── ViewRewards.cshtml       # Rewards history view
│   │       └── BitcoinRewards/
│   │           └── NavExtension.cshtml      # Navigation menu extension
│   └── BTCPayServer.Plugins.BitcoinRewards.Tests/  # Test project
├── submodules/                          # BTCPay Server submodule
├── README.md
└── Directory.Build.*                    # Build configuration files
```

### Building for Plugin Builder

The plugin is configured to work with the BTCPay Server Plugin Builder:

- **Plugin Directory**: `BTCPayServer.Plugins.BitcoinRewards`
- **Assembly Name**: `BTCPayServer.Plugins.BitcoinRewards`
- **Output**: `.btcpay` file (automatically renamed from `.dll` in Release builds)

### Dependencies

The plugin references BTCPay Server DLLs (not bundled):
- `BTCPayServer.Abstractions.dll`
- `BTCPayServer.Data.dll`
- `BTCPayServer.Services.dll`
- `BTCPayServer.HostedServices.dll`
- `BTCPayServer.Logging.dll`

These are provided by the BTCPay Server installation at runtime.

## TODO / Future Enhancements

- [ ] Implement actual Bitcoin sending using BTCPay Server's wallet services
- [ ] Add support for customer Bitcoin address management
- [ ] Implement proper currency conversion (fiat to BTC)
- [ ] Add support for Lightning Network rewards
- [ ] Add reward history and analytics
- [ ] Add support for reward expiration
- [ ] Add support for reward tiers based on order amount
- [ ] Add support for referral rewards

## Compatibility

- **BTCPay Server**: 2.0.0 or later
- **.NET**: 8.0
- **Target Framework**: net8.0

## Build Notes

### Known Build Warnings/Errors

- **gpg/cat errors**: During the build process, you may see errors like `gpg: command not found` and `cat: write error: Broken pipe`. These are harmless and occur when the BTCPayServer PluginPacker tries to sign the package with GPG. The build will complete successfully without GPG signing. These errors can be safely ignored.

- **Nullable reference warnings**: The build may show nullable reference type warnings (CS8618, CS8603, CS8601). These are code quality warnings and don't prevent the build from succeeding. They indicate places where null checks could be improved, but the code is functionally correct.

## License

MIT License - Same as BTCPay Server

## Support

For issues and questions, please open an issue on the [GitHub repository](https://github.com/yourusername/btcpayserver-plugin-bitcoinrewards).

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

