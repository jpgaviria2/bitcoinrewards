# Bitcoin Rewards Plugin - Compliance with BTCPay Server Plugin Documentation

This document verifies that the Bitcoin Rewards plugin follows the official BTCPay Server plugin development guidelines as documented at: https://docs.btcpayserver.org/Development/Plugins/

## ✅ Compliance Checklist

### Setup of a new plugin
- [x] Plugin repository has BTCPay Server as submodule
- [x] Plugin project references BTCPay Server project
- [x] Plugin identifier, name, and description are set in `.csproj`
- [x] Plugin uses `BaseBTCPayServerPlugin` as base class

### Plugin Structure
- [x] Plugin class extends `BaseBTCPayServerPlugin`
- [x] `Identifier` property matches plugin name: `BTCPayServer.Plugins.BitcoinRewards`
- [x] `Name` and `Description` properties are set
- [x] Plugin dependencies are declared (BTCPay Server >= 2.0.0)

### UI Extension Points
- [x] Using `services.AddUIExtension("header-nav", "BitcoinRewardsNavExtension")`
- [x] View file located in `Views/Shared/BitcoinRewardsNavExtension.cshtml`
- [x] Extension point name matches documented pattern: `header-nav`

### Views
- [x] Views are in `Views/Shared/` for extension points
- [x] Views are in `Views/UIBitcoinRewards/` for controller views
- [x] Views use proper Razor syntax and BTCPay Server helpers

### Database
- [x] Database entities defined (`BitcoinRewardRecord`)
- [x] Using `ApplicationDbContextFactory` (acceptable for plugins that reference store data)
- [x] Database extension methods defined for entity registration

**Note:** According to the documentation, plugins *can* have their own database context and schema using `BaseDbContextFactory<MyPluginDbContext>`. However, for plugins that need to reference existing BTCPay Server entities (like `StoreData`), using `ApplicationDbContextFactory` with the public schema is a valid approach, as seen in other plugins.

### Services Registration
- [x] Services registered in `Execute(IServiceCollection services)` method
- [x] Using `TryAddScoped` to avoid conflicts
- [x] Services properly scoped (Scoped for DbContext-dependent services)

### Authorization and Permissions
- [x] Controllers use `[Authorize]` attributes
- [x] Using standard BTCPay Server policies: `Policies.CanModifyStoreSettings`, `Policies.CanViewStoreSettings`
- [x] Using `AuthenticationSchemes.Cookie`

### Assets (if needed)
- [ ] Currently no embedded resources/assets
- [x] If assets are added, they would be embedded using `<EmbeddedResource Include="Resources\**" />`

## Implementation Details

### Current Implementation

1. **Plugin Class**: `BitcoinRewardsPlugin.cs`
   - Extends `BaseBTCPayServerPlugin` ✅
   - Implements required properties ✅
   - Registers UI extensions ✅
   - Registers services ✅

2. **UI Extension**: 
   - View: `Views/Shared/BitcoinRewardsNavExtension.cshtml` ✅
   - Registered via `services.AddUIExtension("header-nav", "BitcoinRewardsNavExtension")` ✅
   - Uses proper BTCPay Server view helpers ✅

3. **Database**:
   - Entity: `BitcoinRewardRecord` ✅
   - Repository: `BitcoinRewardsRepository` ✅
   - Uses `ApplicationDbContextFactory` (valid for store-related data) ✅

4. **Controllers**:
   - `UIBitcoinRewardsController` ✅
   - Proper authorization attributes ✅
   - Routes follow plugin convention: `/plugins/bitcoin-rewards/{storeId}/...` ✅

## Notes

### Database Schema Decision
While the documentation mentions plugins can have their own database schema, using `ApplicationDbContextFactory` with the public schema is appropriate when:
- Plugin needs to reference existing BTCPay Server entities (e.g., `StoreData`)
- Plugin data is closely tied to stores/users
- Simpler data access is required

Our implementation uses this approach, which is consistent with how other plugins handle store-related data.

### UI Extension Registration
The documentation shows both patterns:
- `services.AddSingleton<IUIExtension>(new UIExtension(...))` (older pattern)
- `services.AddUIExtension(...)` (newer pattern)

We use the newer `AddUIExtension` method, which is the recommended approach as seen in current plugins like Shopify and Subscriptions.

## Conclusion

The Bitcoin Rewards plugin follows the BTCPay Server plugin development guidelines as documented. All major requirements are met, and the implementation choices (such as using `ApplicationDbContextFactory`) are appropriate for the plugin's requirements.

## References

- [BTCPay Server Plugin Documentation](https://docs.btcpayserver.org/Development/Plugins/)
- Plugin identifier: `BTCPayServer.Plugins.BitcoinRewards`
- Version: `1.0.0`

