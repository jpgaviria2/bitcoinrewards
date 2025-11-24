# Bitcoin Rewards Plugin

A BTCPay Server plugin for managing Bitcoin rewards.

## Development

If you are a developer maintaining this plugin, clone this repository with `--recurse-submodules`:

```bash
git clone --recurse-submodules https://github.com/yourusername/btcpayserver-plugin-bitcoinrewards
```

Then create the `appsettings.dev.json` file in `submodules\btcpayserver\BTCPayServer`, with the following content:

```json
{
  "DEBUG_PLUGINS": "../../../Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Debug/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll"
}
```

This will ensure that BTCPay Server loads the plugin when it starts.

Start the development dependencies via docker-compose:

```bash
docker-compose up -d dev
```

Finally:
1. Set up BTCPay Server as the startup project in [Rider](https://www.jetbrains.com/rider/) or Visual Studio.
2. Make sure to select the `Bitcoin-HTTPS` launch settings.

If you want to reset the environment you can run:

```bash
docker-compose down -v
docker-compose up -d dev
```

Note: Running or compiling the BTCPay Server project will not automatically recompile the plugin project. Therefore, if you make any changes to the project, do not forget to build it before running BTCPay Server in debug mode.

We recommend using Rider for plugin development, as it supports hot reload with plugins. You can edit .cshtml files, save, and refresh the page to see the changes.

Visual Studio does not support this feature.

## Building

See [BUILD_INSTRUCTIONS.md](BUILD_INSTRUCTIONS.md) for detailed build instructions.
