#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Services.Stores;
using DotNut;
using DotNut.Api;
using DotNut.ApiModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Adapter to interact with BTCPay Server's Cashu plugin using reflection.
/// Uses reflection to avoid compile-time dependencies on the Cashu plugin.
/// </summary>
public class CashuServiceAdapter : ICashuService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CashuServiceAdapter> _logger;
    private readonly StoreRepository _storeRepository;
    private readonly ProofStorageService _proofStorageService;
    private readonly WalletConfigurationService _walletConfigurationService;
    private readonly Data.BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private bool _cashuServiceAvailable;
    private object? _cashuService;
    private object? _cashuDbContextFactory;
    private object? _lightningClientFactoryService;
    private object? _paymentMethodHandlers;
    private Type? _cashuWalletType;
    private Assembly? _cashuAssembly;

    public CashuServiceAdapter(
        IServiceProvider serviceProvider,
        ILogger<CashuServiceAdapter> logger,
        StoreRepository storeRepository,
        ProofStorageService proofStorageService,
        WalletConfigurationService walletConfigurationService,
        Data.BitcoinRewardsPluginDbContextFactory dbContextFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _storeRepository = storeRepository;
        _proofStorageService = proofStorageService;
        _walletConfigurationService = walletConfigurationService;
        _dbContextFactory = dbContextFactory;
        TryDiscoverCashuService();
    }

    private void TryDiscoverCashuService()
    {
        // Don't re-discover if we already found it
        if (_cashuServiceAvailable && _cashuService != null)
            return;

        try
        {
            // Try to find Cashu plugin assembly and service
            _cashuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "BTCPayServer.Plugins.Cashu" || 
                                    a.FullName?.Contains("Cashu") == true);
            
            if (_cashuAssembly != null)
            {
                _logger.LogInformation("Cashu plugin assembly found: {AssemblyName}", _cashuAssembly.FullName);
                
                var allTypes = _cashuAssembly.GetTypes();
                _logger.LogDebug("Cashu assembly contains {Count} types", allTypes.Length);
                
                // Discover CashuDbContextFactory
                var dbContextFactoryType = _cashuAssembly.GetType("BTCPayServer.Plugins.Cashu.Data.CashuDbContextFactory");
                if (dbContextFactoryType != null)
                {
                    _cashuDbContextFactory = _serviceProvider.GetService(dbContextFactoryType);
                    if (_cashuDbContextFactory != null)
                    {
                        _logger.LogInformation("CashuDbContextFactory found and resolved");
                    }
                }
                
                // Discover LightningClientFactoryService
                var lightningFactoryType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "LightningClientFactoryService" && 
                                        t.Namespace?.Contains("BTCPayServer") == true);
                if (lightningFactoryType != null)
                {
                    _lightningClientFactoryService = _serviceProvider.GetService(lightningFactoryType);
                    if (_lightningClientFactoryService != null)
                    {
                        _logger.LogInformation("LightningClientFactoryService found and resolved");
                    }
                }
                
                // Discover PaymentMethodHandlerDictionary
                var paymentHandlersType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "PaymentMethodHandlerDictionary" && 
                                        t.Namespace?.Contains("BTCPayServer") == true);
                if (paymentHandlersType != null)
                {
                    _paymentMethodHandlers = _serviceProvider.GetService(paymentHandlersType);
                    if (_paymentMethodHandlers != null)
                    {
                        _logger.LogInformation("PaymentMethodHandlerDictionary found and resolved");
                    }
                }
                
                // Discover CashuWallet type (not a service, but we'll instantiate it)
                _cashuWalletType = _cashuAssembly.GetType("BTCPayServer.Plugins.Cashu.CashuAbstractions.CashuWallet");
                if (_cashuWalletType != null)
                {
                    _logger.LogInformation("CashuWallet type found: {TypeName}", _cashuWalletType.FullName);
                }
                
                // Look for CashuPaymentService (for payment operations)
                var paymentServiceType = _cashuAssembly.GetType("BTCPayServer.Plugins.Cashu.PaymentHandlers.CashuPaymentService");
                if (paymentServiceType != null)
                {
                    _cashuService = _serviceProvider.GetService(paymentServiceType);
                            if (_cashuService != null)
                            {
                                _cashuServiceAvailable = true;
                        _logger.LogInformation("CashuPaymentService found and resolved");
                        
                        // Log available methods
                        var methods = paymentServiceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Select(m => m.Name)
                            .ToList();
                        _logger.LogDebug("CashuPaymentService available methods: {Methods}", string.Join(", ", methods));
                    }
                }
                
                // If we found essential services, mark as available
                if (_cashuDbContextFactory != null && _cashuWalletType != null)
                {
                    _cashuServiceAvailable = true;
                    _logger.LogInformation("Cashu plugin services discovered successfully");
                }
                            }
                            else
                            {
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name?.Contains("Cashu") == true || a.FullName?.Contains("Cashu") == true)
                    .Select(a => a.GetName().Name)
                    .ToList();
                _logger.LogDebug("Cashu assembly not found. Assemblies with 'Cashu' in name: {Assemblies}", 
                    string.Join(", ", loadedAssemblies));
            }
            
            if (!_cashuServiceAvailable)
            {
                _logger.LogWarning("Cashu plugin service not available - token minting will be disabled. Install the BTCPay Server Cashu plugin to enable ecash token generation.");
                        }
                    }
                    catch (Exception ex)
                    {
            _cashuServiceAvailable = false;
            _cashuService = null;
            _logger.LogError(ex, "Error discovering Cashu service. Cashu plugin service could not be resolved.");
        }
    }

    /// <summary>
    /// Creates a CashuWallet instance using reflection (matching Cashu plugin).
    /// Falls back to InternalCashuWallet if CashuWallet is not available.
    /// </summary>
    private object? CreateCashuWallet(string mintUrl, string unit = "sat", ILightningClient? lightningClient = null)
    {
        // Try to use CashuWallet from Cashu plugin if available
        if (_cashuWalletType != null && _cashuDbContextFactory != null)
        {
            try
            {
                // CashuWallet has two constructors:
                // 1. CashuWallet(string mint, string unit = "sat", CashuDbContextFactory? cashuDbContextFactory = null)
                // 2. CashuWallet(ILightningClient lightningClient, string mint, string unit = "sat", CashuDbContextFactory? cashuDbContextFactory = null)
                
                if (lightningClient != null)
                {
                    var constructor = _cashuWalletType.GetConstructor(new[] 
                    { 
                        typeof(ILightningClient), 
                        typeof(string), 
                        typeof(string), 
                        _cashuDbContextFactory.GetType() 
                    });
                    if (constructor != null)
                    {
                        return constructor.Invoke(new object[] { lightningClient, mintUrl, unit, _cashuDbContextFactory });
                    }
                }
                else
                {
                    var constructor = _cashuWalletType.GetConstructor(new[] 
                    { 
                        typeof(string), 
                        typeof(string), 
                        _cashuDbContextFactory.GetType() 
                    });
                    if (constructor != null)
                    {
                        return constructor.Invoke(new object[] { mintUrl, unit, _cashuDbContextFactory });
                    }
                    
                    // Try with nullable parameter
                    var constructorNullable = _cashuWalletType.GetConstructor(new[] 
                    { 
                        typeof(string), 
                        typeof(string), 
                        typeof(object) 
                    });
                    if (constructorNullable != null)
                    {
                        return constructorNullable.Invoke(new object[] { mintUrl, unit, _cashuDbContextFactory });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create CashuWallet via reflection, falling back to InternalCashuWallet");
            }
        }
        
        // Fallback to InternalCashuWallet
        _logger.LogDebug("Using InternalCashuWallet as fallback");
        var walletLogger = _serviceProvider.GetService(typeof(ILogger<InternalCashuWallet>)) as ILogger<InternalCashuWallet>;
        if (lightningClient != null)
        {
            return new InternalCashuWallet(lightningClient, mintUrl, unit, walletLogger);
        }
        return new InternalCashuWallet(mintUrl, unit, walletLogger);
    }

    private async Task<string?> GetMintUrlForStore(string storeId)
    {
        try
        {
            // Method 1: Try to get from our own wallet configuration (primary source)
            var mintUrl = await _walletConfigurationService.GetMintUrlAsync(storeId);
            if (!string.IsNullOrEmpty(mintUrl))
            {
                _logger.LogInformation("Found mint URL from Bitcoin Rewards wallet configuration for store {StoreId}: {MintUrl}", storeId, mintUrl);
                return mintUrl;
            }

            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
            {
                _logger.LogWarning("Store {StoreId} not found", storeId);
                return null;
            }

            // Method 2: Try to get from Cashu payment method config (fallback)
            if (_paymentMethodHandlers != null)
            {
                // Get CashuPmid from CashuPlugin (it's internal, so use NonPublic)
                var cashuPluginType = _cashuAssembly?.GetType("BTCPayServer.Plugins.Cashu.CashuPlugin");
                if (cashuPluginType != null)
                {
                    var cashuPmidField = cashuPluginType.GetField("CashuPmid", 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (cashuPmidField != null)
                    {
                        var cashuPmid = cashuPmidField.GetValue(null);
                        if (cashuPmid != null)
                        {
                            // Get payment method config
                            var getPaymentMethodConfigMethod = typeof(StoreData).GetMethod("GetPaymentMethodConfig", 
                                new[] { cashuPmid.GetType(), _paymentMethodHandlers.GetType() });
                            if (getPaymentMethodConfigMethod != null)
                            {
                                var config = getPaymentMethodConfigMethod.Invoke(store, new[] { cashuPmid, _paymentMethodHandlers });
                                if (config != null)
                                {
                                    // Get TrustedMintsUrls property
                                    var trustedMintsProperty = config.GetType().GetProperty("TrustedMintsUrls");
                                    if (trustedMintsProperty != null)
                                    {
                                        var trustedMints = trustedMintsProperty.GetValue(config) as List<string>;
                                        if (trustedMints != null && trustedMints.Count > 0)
                                        {
                                            var foundMintUrl = trustedMints.First();
                                            _logger.LogInformation("Found mint URL from payment method config for store {StoreId}: {MintUrl}", storeId, foundMintUrl);
                                            return foundMintUrl;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Method 3: Try to get from Cashu plugin database (fallback)
            if (_cashuDbContextFactory != null)
            {
                try
                {
                    var createContextMethod = _cashuDbContextFactory.GetType().GetMethod("CreateContext");
                    if (createContextMethod != null)
                    {
                        var dbContext = createContextMethod.Invoke(_cashuDbContextFactory, null);
                        if (dbContext != null)
                        {
                            try
                            {
                                // Get Mints DbSet
                                var mintsProperty = dbContext.GetType().GetProperty("Mints");
                                if (mintsProperty != null)
                                {
                                    var mintsDbSet = mintsProperty.GetValue(dbContext);
                                    if (mintsDbSet != null)
                                    {
                                        // Get Proofs DbSet to find which mints have proofs for this store
                                        var proofsProperty = dbContext.GetType().GetProperty("Proofs");
                                        if (proofsProperty != null)
                                        {
                                            var proofsDbSet = proofsProperty.GetValue(dbContext);
                                            if (proofsDbSet != null)
                                            {
                                                // Query for mints that have proofs for this store
                                                var toListAsyncMethod = typeof(EntityFrameworkQueryableExtensions)
                                                    .GetMethods()
                                                    .FirstOrDefault(m => m.Name == "ToListAsync" && m.GetParameters().Length == 1);
                                                
                                                if (toListAsyncMethod != null)
                                                {
                                                    // Get mint URLs from proofs that belong to this store
                                                    var proofType = proofsDbSet.GetType().GetGenericArguments()[0];
                                                    var toListAsyncGeneric = toListAsyncMethod.MakeGenericMethod(proofType);
                                                    var proofsTask = toListAsyncGeneric.Invoke(null, new[] { proofsDbSet }) as Task;
                                                    
                                                    if (proofsTask != null)
                                                    {
                                                        await proofsTask;
                                                        var proofsList = proofsTask.GetType().GetProperty("Result")?.GetValue(proofsTask) as System.Collections.IEnumerable;
                                                        
                                                        if (proofsList != null)
                                                        {
                                                            // Get unique mint URLs from proofs that have this storeId
                                                            var mintUrls = new HashSet<string>();
                                                            foreach (var proof in proofsList)
                                                            {
                                                                var storeIdProperty = proof.GetType().GetProperty("StoreId");
                                                                if (storeIdProperty?.GetValue(proof)?.ToString() == storeId)
                                                                {
                                                                    // Get the mint URL from the mint associated with this proof
                                                                    // We need to find which mint this proof belongs to via keyset
                                                                    // For now, let's get all mints and check which ones have proofs
                                                                }
                                                            }
                                                        }
                                                    }
                                                    
                                                    // Alternative: Get all mints and return the first one
                                                    var mintType = mintsDbSet.GetType().GetGenericArguments()[0];
                                                    var mintsToListAsync = toListAsyncMethod.MakeGenericMethod(mintType);
                                                    var mintsTask = mintsToListAsync.Invoke(null, new[] { mintsDbSet }) as Task;
                                                    
                                                    if (mintsTask != null)
                                                    {
                                                        await mintsTask;
                                                        var mintsList = mintsTask.GetType().GetProperty("Result")?.GetValue(mintsTask) as System.Collections.IEnumerable;
                                                        
                                                        if (mintsList != null)
                                                        {
                                                            // Get the first mint URL
                                                            foreach (var mint in mintsList)
                                                            {
                                                                var urlProperty = mint.GetType().GetProperty("Url");
                                                                var foundMintUrl = urlProperty?.GetValue(mint)?.ToString();
                                                                if (!string.IsNullOrEmpty(foundMintUrl))
                                                                {
                                                                    _logger.LogInformation("Found mint URL from database for store {StoreId}: {MintUrl}", storeId, foundMintUrl);
                                                                    return foundMintUrl;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                if (dbContext is IDisposable disposable)
                                {
                                    disposable.Dispose();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error getting mint URL from database for store {StoreId}", storeId);
                }
            }

            _logger.LogWarning("No mint URL found for store {StoreId}. Please configure Cashu payment method with trusted mints in store settings.", storeId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mint URL for store {StoreId}", storeId);
            return null;
        }
    }

    private async Task<long> GetEcashBalanceAsync(string storeId, string mintUrl)
    {
        try
        {
            // Simple approach - just sum proofs (like Cashu plugin does)
            var allProofs = await _proofStorageService.GetProofsAsync(storeId, mintUrl);
            var balance = allProofs?.Aggregate(0UL, (sum, p) => sum + p.Amount) ?? 0;
            
            _logger.LogDebug("Ecash balance for store {StoreId} on mint {MintUrl}: {Balance} sat", 
                storeId, mintUrl, balance);
            
            return (long)balance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ecash balance for store {StoreId}", storeId);
            return 0;
        }
    }

    public async Task<long> GetLightningBalanceAsync(string storeId)
    {
        try
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
            {
                _logger.LogWarning("Store {StoreId} not found", storeId);
                return 0;
            }

            if (_lightningClientFactoryService == null || _paymentMethodHandlers == null)
            {
                _logger.LogWarning("Lightning services not available");
                return 0;
            }

            // Get network (BTC)
            var networkProviderType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "BTCPayNetworkProvider" && 
                                    t.Namespace?.Contains("BTCPayServer") == true);
            if (networkProviderType == null)
            {
                _logger.LogWarning("BTCPayNetworkProvider not found");
                return 0;
            }

            var networkProvider = _serviceProvider.GetService(networkProviderType);
            if (networkProvider == null)
            {
                _logger.LogWarning("BTCPayNetworkProvider service not available");
                return 0;
            }

            // Get all GetNetwork methods and find the one that takes a single string parameter
            var getNetworkMethods = networkProviderType.GetMethods()
                .Where(m => m.Name == "GetNetwork" && m.GetParameters().Length == 1 && 
                           m.GetParameters()[0].ParameterType == typeof(string))
                .ToList();
            
            if (getNetworkMethods.Count == 0)
            {
                _logger.LogWarning("GetNetwork method not found");
                return 0;
            }

            // Use the first matching method (should be the one that takes string)
            var getNetworkMethod = getNetworkMethods[0];
            var network = getNetworkMethod.Invoke(networkProvider, new object[] { "BTC" });
            if (network == null)
            {
                _logger.LogWarning("BTC network not found");
                return 0;
            }

            // Get Lightning payment method ID
            var paymentTypesType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "PaymentTypes" && 
                                    t.Namespace?.Contains("BTCPayServer") == true);
            if (paymentTypesType == null)
            {
                _logger.LogWarning("PaymentTypes not found");
                return 0;
            }

            var lnProperty = paymentTypesType.GetProperty("LN", BindingFlags.Public | BindingFlags.Static);
            if (lnProperty == null)
            {
                _logger.LogWarning("PaymentTypes.LN not found");
                return 0;
            }

            var lnPaymentType = lnProperty.GetValue(null);
            if (lnPaymentType == null)
            {
                _logger.LogWarning("LN payment type is null");
                return 0;
            }

            var getPaymentMethodIdMethod = lnPaymentType.GetType().GetMethod("GetPaymentMethodId", 
                new[] { typeof(string) });
            if (getPaymentMethodIdMethod == null)
            {
                _logger.LogWarning("GetPaymentMethodId method not found");
                return 0;
            }

            var cryptoCodeProperty = network.GetType().GetProperty("CryptoCode");
            if (cryptoCodeProperty == null)
            {
                _logger.LogWarning("CryptoCode property not found");
                return 0;
            }

            var cryptoCode = cryptoCodeProperty.GetValue(network)?.ToString() ?? "BTC";
            if (getPaymentMethodIdMethod == null)
            {
                _logger.LogWarning("GetPaymentMethodId method not found");
                return 0;
            }
            var lightningPmi = getPaymentMethodIdMethod.Invoke(lnPaymentType, new object[] { cryptoCode });

            // Get Lightning config
            if (lightningPmi == null)
            {
                _logger.LogWarning("Lightning payment method ID is null");
                return 0;
            }
            var getPaymentMethodConfigMethod = typeof(StoreData).GetMethod("GetPaymentMethodConfig", 
                new[] { lightningPmi.GetType(), _paymentMethodHandlers.GetType() });
            if (getPaymentMethodConfigMethod == null)
            {
                _logger.LogWarning("GetPaymentMethodConfig method not found for Lightning");
                return 0;
            }

            var lightningConfig = getPaymentMethodConfigMethod.Invoke(store, new[] { lightningPmi, _paymentMethodHandlers });
            if (lightningConfig == null)
            {
                _logger.LogDebug("Lightning not configured for store {StoreId}", storeId);
                return 0;
            }

            // Create Lightning client
            var createLightningClientMethod = lightningConfig.GetType().GetMethod("CreateLightningClient",
                new[] { network.GetType(), typeof(object), _lightningClientFactoryService.GetType() });
            if (createLightningClientMethod == null)
            {
                _logger.LogWarning("CreateLightningClient method not found");
                return 0;
            }

            // Get LightningNetworkOptions
            var lightningNetworkOptionsType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "LightningNetworkOptions" && 
                                    t.Namespace?.Contains("BTCPayServer") == true);
            var lightningNetworkOptions = lightningNetworkOptionsType != null 
                ? _serviceProvider.GetService(lightningNetworkOptionsType) 
                : null;

            var lightningClient = createLightningClientMethod.Invoke(lightningConfig, 
                new[] { network, lightningNetworkOptions, _lightningClientFactoryService });
            if (lightningClient == null)
            {
                _logger.LogWarning("Failed to create Lightning client");
                return 0;
            }

            // Get balance
            var getBalanceMethod = lightningClient.GetType().GetMethod("GetBalance");
            if (getBalanceMethod == null)
            {
                _logger.LogWarning("GetBalance method not found on Lightning client");
                return 0;
            }

            var balanceTask = getBalanceMethod.Invoke(lightningClient, null) as Task;
            if (balanceTask == null)
            {
                _logger.LogWarning("GetBalance did not return a Task");
                return 0;
            }

            await balanceTask;
            var balanceResult = balanceTask.GetType().GetProperty("Result")?.GetValue(balanceTask);
            if (balanceResult == null)
            {
                _logger.LogWarning("GetBalance result is null");
                return 0;
            }

            // Get Available property
            var availableProperty = balanceResult.GetType().GetProperty("Available");
            if (availableProperty == null)
            {
                _logger.LogWarning("Available property not found on balance");
                return 0;
            }

            var available = availableProperty.GetValue(balanceResult);
            if (available == null)
            {
                return 0;
            }

            // Convert to satoshis
            var toUnitMethod = available.GetType().GetMethod("ToUnit");
            if (toUnitMethod == null)
            {
                _logger.LogWarning("ToUnit method not found");
                return 0;
            }

            var satoshiUnitEnum = Enum.Parse(toUnitMethod.GetParameters()[0].ParameterType, "Satoshi");
            var satoshis = toUnitMethod.Invoke(available, new[] { satoshiUnitEnum });
            if (satoshis == null || !long.TryParse(satoshis.ToString(), out var satoshiValue))
            {
                return 0;
            }

            _logger.LogInformation("Lightning balance for store {StoreId}: {Balance} sat", storeId, satoshiValue);
            return satoshiValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Lightning balance for store {StoreId}", storeId);
            return 0;
        }
    }

    private async Task<List<object>?> GetStoredProofsAsync(string storeId, string mintUrl, ulong maxAmount)
    {
        try
        {
            // Use our own proof storage service (primary source)
            var proofs = await _proofStorageService.GetProofsAsync(storeId, mintUrl, maxAmount);
            
            // Convert Proof objects to List<object> for compatibility with existing code
            return proofs.Cast<object>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stored proofs for store {StoreId}", storeId);
            return null;
        }
    }

    private async Task<string?> SwapProofsAsync(ulong amount, string mintUrl, string storeId)
    {
        try
        {
            _logger.LogInformation("Swapping proofs for {Amount} sat on mint {MintUrl}", amount, mintUrl);

            // Get stored proofs from database
            var storedProofs = await GetStoredProofsAsync(storeId, mintUrl, amount);
            if (storedProofs == null || storedProofs.Count == 0)
            {
                _logger.LogWarning("No proofs available for swapping");
                return null;
            }

            // Create internal wallet (logger is optional)
            var walletLogger = _serviceProvider.GetService(typeof(ILogger<InternalCashuWallet>)) as ILogger<InternalCashuWallet>;
            var wallet = new InternalCashuWallet(mintUrl, "sat", walletLogger);

            // Get active keyset and keys
            var activeKeyset = await wallet.GetActiveKeyset();
            var keysetId = new KeysetId(activeKeyset.Id.ToString());
            var keys = await wallet.GetKeys(keysetId);
            if (keys == null)
            {
                _logger.LogWarning("Could not get keys for keyset");
                return null;
            }

            // Convert stored proofs to DotNut Proof objects (they're already Proof objects from ProofStorageService)
            var proofList = storedProofs.Cast<Proof>().ToList();

            if (proofList.Count == 0)
            {
                _logger.LogWarning("No valid proofs to swap");
                return null;
            }

            // Check if proofs match the active keyset
            var activeKeysetIdStr = activeKeyset.Id.ToString();
            var matchingProofs = proofList.Where(p => p.Id.ToString() == activeKeysetIdStr).ToList();
            
            if (matchingProofs.Count == 0)
            {
                _logger.LogWarning("No proofs match the active keyset {KeysetId}. Proof keyset IDs: {ProofIds}", 
                    activeKeysetIdStr, string.Join(", ", proofList.Select(p => p.Id.ToString()).Distinct()));
                
                // For small amounts, try creating token directly from proofs without swap
                // This works if the proofs are already in the correct format
                if (amount <= 1000) // Small amounts (1-1000 sat)
                {
                    _logger.LogInformation("Attempting to create token directly from proofs without swap (small amount)");
                    var totalProofAmount = proofList.Aggregate(0UL, (sum, p) => sum + p.Amount);
                    if (totalProofAmount >= amount)
                    {
                        // Select proofs that sum to the requested amount
                        var selectedProofs = new List<Proof>();
                        ulong selectedAmount = 0;
                        foreach (var proof in proofList.OrderByDescending(p => p.Amount))
                        {
                            if (selectedAmount >= amount) break;
                            selectedProofs.Add(proof);
                            selectedAmount += proof.Amount;
                        }
                        
                        if (selectedAmount >= amount)
                        {
                            _logger.LogInformation("Creating token directly from {Count} proofs totaling {Amount} sat", 
                                selectedProofs.Count, selectedAmount);
                            var directProofList = selectedProofs.Cast<object>().ToList();
                            return await CreateTokenFromProofs(directProofList, mintUrl, "sat");
                        }
                    }
                }
                
                return null;
            }

            // Use matching proofs for swap
            proofList = matchingProofs;

            // Split amount to proof amounts
            var outputAmounts = InternalCashuWallet.SplitToProofsAmounts(amount, keys);

            // Call Swap - the mint will validate if proofs are spent
            var swapResult = await wallet.Swap(proofList, outputAmounts, keysetId, keys);
            if (!swapResult.Success || swapResult.ResultProofs == null || swapResult.ResultProofs.Length == 0)
            {
                var errorMsg = swapResult.Error?.Message ?? "Unknown error";
                _logger.LogWarning("Swap failed: {Error}", errorMsg);
                
                // Don't try fallback - swap is required for security and token rotation
                // If swap fails, the proofs might be invalid or the mint is having issues
                return null;
            }

            // Convert proofs to list of objects for CreateTokenFromProofs
            var resultProofList = swapResult.ResultProofs.Cast<object>().ToList();

            // Create token from proofs
            return await CreateTokenFromProofs(resultProofList, mintUrl, "sat");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error swapping proofs for {Amount} sat", amount);
            return null;
        }
    }

    private async Task<List<object>?> MintFromLightningAsync(long amountSatoshis, string storeId, string mintUrl)
    {
        try
        {
            _logger.LogInformation("Minting {Amount} sat from Lightning for store {StoreId} on mint {MintUrl}", 
                amountSatoshis, storeId, mintUrl);

            // Get Lightning client
            var lightningClientObj = await GetLightningClientForStore(storeId);
            if (lightningClientObj == null)
            {
                _logger.LogWarning("Lightning client not available for store {StoreId}", storeId);
                return null;
            }

            // Cast to ILightningClient
            var lightningClient = lightningClientObj as ILightningClient;
            if (lightningClient == null)
            {
                _logger.LogWarning("Lightning client is not ILightningClient");
                return null;
            }

            // Create wallet with Lightning client (logger is optional)
            var walletLogger = _serviceProvider.GetService(typeof(ILogger<InternalCashuWallet>)) as ILogger<InternalCashuWallet>;
            var wallet = new InternalCashuWallet(lightningClient, mintUrl, "sat", walletLogger);

            // Create mint quote (NUT-04)
            var mintQuote = await wallet.CreateMintQuote((ulong)amountSatoshis, "sat");
            if (mintQuote == null || string.IsNullOrEmpty(mintQuote.Request))
            {
                _logger.LogWarning("Failed to create mint quote");
                return null;
            }

            // Pay invoice
            var payResult = await lightningClient.Pay(mintQuote.Request, CancellationToken.None);
            if (payResult.Result != PayResult.Ok)
            {
                _logger.LogWarning("Lightning payment failed: {Result}", payResult.Result);
                return null;
            }

            // Poll for quote completion
            var quoteId = mintQuote.Quote;
            if (string.IsNullOrEmpty(quoteId))
            {
                _logger.LogWarning("Mint quote ID is empty");
                return null;
            }

            for (int i = 0; i < 30; i++) // Poll up to 30 times (30 seconds)
            {
                await Task.Delay(1000);
                var checkResult = await wallet.CheckMintQuote(quoteId, CancellationToken.None);
                
                // Check if quote is paid using reflection (PostMintQuoteBolt11Response structure may vary)
                var paidProperty = checkResult.GetType().GetProperty("Paid");
                var paid = paidProperty?.GetValue(checkResult) as bool?;
                
                if (paid == true)
                {
                    // Get proofs from quote
                    var proofsProperty = checkResult.GetType().GetProperty("Proofs");
                    var proofs = proofsProperty?.GetValue(checkResult) as Array;
                    
                    if (proofs != null && proofs.Length > 0)
                    {
                        // Convert proofs to list of objects for return
                        var proofList = new List<object>();
                        var proofsToStore = new List<Proof>();
                        foreach (var proof in proofs)
                        {
                            if (proof != null)
                            {
                                proofList.Add(proof);
                                if (proof is Proof proofObj)
                                {
                                    proofsToStore.Add(proofObj);
                                }
                            }
                        }

                        // Store proofs in database using our proof storage service
                        if (proofsToStore.Count > 0)
                        {
                            await _proofStorageService.AddProofsAsync(proofsToStore, storeId, mintUrl);
                        }

                        _logger.LogInformation("Successfully minted {Count} proofs from Lightning", proofList.Count);
                        return proofList;
                    }
                    break;
                }
            }

            _logger.LogWarning("Mint quote not paid after polling");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error minting from Lightning for {Amount} sat", amountSatoshis);
            return null;
        }
    }

    private Task<object?> GetLightningClientForStore(string storeId)
    {
        // Simplified version - reuse logic from GetLightningBalanceAsync
        // This is a helper to avoid code duplication
        return Task.FromResult<object?>(GetLightningClientForStoreSync(storeId));
    }

    private object? GetLightningClientForStoreSync(string storeId)
    {
        try
        {
            var store = _storeRepository.FindStore(storeId).GetAwaiter().GetResult();
            if (store == null || _lightningClientFactoryService == null || _paymentMethodHandlers == null)
            {
                return null;
            }

            // Get network
            var networkProviderType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "BTCPayNetworkProvider");
            if (networkProviderType == null)
            {
                return null;
            }
            var networkProvider = _serviceProvider.GetService(networkProviderType);
            // Get all GetNetwork methods and find the one that takes a single string parameter
            var getNetworkMethods = networkProviderType.GetMethods()
                .Where(m => m.Name == "GetNetwork" && m.GetParameters().Length == 1 && 
                           m.GetParameters()[0].ParameterType == typeof(string))
                .ToList();
            
            if (getNetworkMethods.Count == 0 || networkProvider == null)
            {
                return null;
            }

            // Use the first matching method
            var getNetworkMethod = getNetworkMethods[0];
            var network = getNetworkMethod.Invoke(networkProvider, new object[] { "BTC" });
            if (network == null)
            {
                return null;
            }

            // Get Lightning PMI
            var paymentTypesType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "PaymentTypes");
            if (paymentTypesType == null)
            {
                return null;
            }
            var lnProperty = paymentTypesType.GetProperty("LN", BindingFlags.Public | BindingFlags.Static);
            var lnPaymentType = lnProperty?.GetValue(null);
            if (lnPaymentType == null)
            {
                return null;
            }
            var getPaymentMethodIdMethod = lnPaymentType.GetType().GetMethod("GetPaymentMethodId", new[] { typeof(string) });
            if (getPaymentMethodIdMethod == null)
            {
                return null;
            }
            var cryptoCodeProperty = network.GetType().GetProperty("CryptoCode");
            var cryptoCode = cryptoCodeProperty?.GetValue(network)?.ToString() ?? "BTC";
            var lightningPmi = getPaymentMethodIdMethod.Invoke(lnPaymentType, new object[] { cryptoCode });
            if (lightningPmi == null)
            {
                return null;
            }

            // Get config
            var getPaymentMethodConfigMethod = typeof(StoreData).GetMethod("GetPaymentMethodConfig",
                new[] { lightningPmi.GetType(), _paymentMethodHandlers.GetType() });
            if (getPaymentMethodConfigMethod == null)
            {
                return null;
            }
            var lightningConfig = getPaymentMethodConfigMethod.Invoke(store, new[] { lightningPmi, _paymentMethodHandlers });
            if (lightningConfig == null)
            {
                return null;
            }

            // Create client
            var createLightningClientMethod = lightningConfig.GetType().GetMethod("CreateLightningClient",
                new[] { network.GetType(), typeof(object), _lightningClientFactoryService.GetType() });
            if (createLightningClientMethod == null)
            {
                return null;
            }
            var lightningNetworkOptionsType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "LightningNetworkOptions");
            var lightningNetworkOptions = lightningNetworkOptionsType != null
                ? _serviceProvider.GetService(lightningNetworkOptionsType)
                : null;

            return createLightningClientMethod.Invoke(lightningConfig,
                new[] { network, lightningNetworkOptions, _lightningClientFactoryService });
        }
        catch
        {
            return null;
        }
    }

    private Task<string?> CreateTokenFromProofs(List<object> proofs, string mintUrl, string unit)
    {
        try
        {
            if (proofs == null || proofs.Count == 0)
            {
                _logger.LogWarning("CreateTokenFromProofs: No proofs provided");
                return Task.FromResult<string?>(null);
            }

            _logger.LogDebug("CreateTokenFromProofs: Creating token from {Count} proofs, mint: {MintUrl}, unit: {Unit}", 
                proofs.Count, mintUrl, unit);

            // Convert proofs to DotNut Proof objects
            var dotNutProofs = new List<Proof>();
            foreach (var proof in proofs)
            {
                if (proof is Proof dotNutProof)
                {
                    dotNutProofs.Add(dotNutProof);
                }
                else
                {
                    _logger.LogWarning("Proof is not a DotNut.Proof type: {Type}. Skipping.", proof.GetType().FullName);
                }
            }

            if (dotNutProofs.Count == 0)
            {
                _logger.LogError("No valid DotNut.Proof objects found in proofs list");
                return Task.FromResult<string?>(null);
            }

            _logger.LogDebug("Converted {Count} proofs to DotNut.Proof objects", dotNutProofs.Count);

            // Create CashuToken directly using DotNut (same as Cashu plugin)
            var createdToken = new CashuToken()
            {
                Tokens =
                [
                    new CashuToken.Token
                    {
                        Mint = mintUrl,
                        Proofs = dotNutProofs,
                    }
                ],
                Memo = "Bitcoin Rewards Token",
                Unit = unit
            };

            // Encode token
            var serializedToken = createdToken.Encode();
            
            if (string.IsNullOrEmpty(serializedToken))
            {
                _logger.LogError("Encode() returned null or empty string");
                return Task.FromResult<string?>(null);
            }

            _logger.LogDebug("Successfully created and encoded token (length: {Length})", serializedToken.Length);
            return Task.FromResult<string?>(serializedToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating token from proofs: {Error}", ex.Message);
            return Task.FromResult<string?>(null);
        }
    }

    public async Task<string?> MintTokenAsync(long amountSatoshis, string storeId)
    {
        // Try to discover service lazily if it wasn't found during construction
        if (!_cashuServiceAvailable)
        {
            _logger.LogDebug("Cashu service not found during construction, attempting lazy discovery");
            TryDiscoverCashuService();
            
            if (!_cashuServiceAvailable)
            {
                _logger.LogWarning("Cashu plugin not available - cannot mint token for {AmountSatoshis} sats in store {StoreId}. Please ensure the Cashu plugin is installed and enabled.", 
                    amountSatoshis, storeId);
                return null;
            }
        }

        try
        {
            _logger.LogInformation("Minting token for {AmountSatoshis} sat in store {StoreId}", amountSatoshis, storeId);

            // 1. Get mint URL from store config
            var mintUrl = await GetMintUrlForStore(storeId);
            if (string.IsNullOrEmpty(mintUrl))
            {
                _logger.LogWarning("No mint URL configured for store {StoreId}", storeId);
                return null;
            }

            // 2. Check ecash balance
            var ecashBalance = await GetEcashBalanceAsync(storeId, mintUrl);
            _logger.LogInformation("Ecash balance for store {StoreId}: {Balance} sat (needed: {Needed} sat)", 
                storeId, ecashBalance, amountSatoshis);

            if (ecashBalance >= amountSatoshis)
            {
                // 3a. Use Swap (NUT-03) to create token from existing proofs
                _logger.LogInformation("Sufficient ecash balance, using Swap (NUT-03) to create token");
                var token = await SwapProofsAsync((ulong)amountSatoshis, mintUrl, storeId);
                if (token != null)
                {
                    _logger.LogInformation("Token created successfully using Swap");
                    return token;
                }
                else
                {
                    _logger.LogError("Swap operation failed despite having sufficient ecash balance ({Balance} sat >= {Needed} sat). Cannot create token.", 
                        ecashBalance, amountSatoshis);
                    // Don't fall back to Lightning if we have sufficient ecash - the issue is with swap/token creation
                    return null;
                }
            }

            // 3b. Check Lightning balance (only if we don't have sufficient ecash)
            var lightningBalance = await GetLightningBalanceAsync(storeId);
            var neededAmount = amountSatoshis - ecashBalance;
            _logger.LogInformation("Lightning balance for store {StoreId}: {Balance} sat (needed: {Needed} sat)", 
                storeId, lightningBalance, neededAmount);

            if (lightningBalance < neededAmount)
            {
                _logger.LogError("Insufficient balance - Ecash: {Ecash} sat, Lightning: {Lightning} sat, Needed: {Needed} sat", 
                    ecashBalance, lightningBalance, neededAmount);
                return null;
            }

            // 4. Mint from Lightning for missing amount
            _logger.LogInformation("Minting {Amount} sat from Lightning (NUT-04)", neededAmount);
            var newProofs = await MintFromLightningAsync(neededAmount, storeId, mintUrl);
            if (newProofs == null || newProofs.Count == 0)
            {
                _logger.LogError("Failed to mint proofs from Lightning");
                return null;
            }

            // 5. Combine existing proofs + new proofs and create token
            _logger.LogInformation("Combining existing proofs with newly minted proofs");
            
            // Get existing proofs for the remaining amount
            var existingProofs = await GetStoredProofsAsync(storeId, mintUrl, (ulong)ecashBalance);
            var allProofs = new List<object>();
            
            if (existingProofs != null && existingProofs.Count > 0)
            {
                // Convert stored proofs to DotNut proofs
                foreach (var storedProof in existingProofs)
                {
                    var toDotNutProofMethod = storedProof.GetType().GetMethod("ToDotNutProof");
                    if (toDotNutProofMethod != null)
                    {
                        var dotNutProof = toDotNutProofMethod.Invoke(storedProof, null);
                        if (dotNutProof != null)
                        {
                            allProofs.Add(dotNutProof);
                        }
                    }
                }
            }

            // Add newly minted proofs
            allProofs.AddRange(newProofs);

            // Create token from all proofs
            var finalToken = await CreateTokenFromProofs(allProofs, mintUrl, "sat");
            if (finalToken != null)
            {
                _logger.LogInformation("Token created successfully from combined proofs");
            }
            else
            {
                _logger.LogError("Failed to create token from combined proofs");
            }

            return finalToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error minting Cashu token for {AmountSatoshis} sats in store {StoreId}", 
                amountSatoshis, storeId);
            return null;
        }
    }

    public async Task<bool> ReclaimTokenAsync(string ecashToken, string storeId)
    {
        if (!_cashuServiceAvailable || _cashuService == null)
        {
            _logger.LogWarning("Cashu plugin not available - cannot reclaim token in store {StoreId}", storeId);
            return false;
        }

        try
        {
            // Try to find a reclaim/revoke method
            var reclaimMethods = new[]
            {
                ("ReclaimTokenAsync", new[] { typeof(string), typeof(string) }),
                ("ReclaimAsync", new[] { typeof(string), typeof(string) }),
                ("RevokeTokenAsync", new[] { typeof(string), typeof(string) }),
                ("ReclaimToken", new[] { typeof(string), typeof(string) })
            };

            MethodInfo? reclaimMethod = null;
            
            foreach (var (methodName, paramTypes) in reclaimMethods)
            {
                reclaimMethod = _cashuService.GetType().GetMethod(methodName, paramTypes);
                if (reclaimMethod != null)
                {
                    break;
                }
            }

            if (reclaimMethod == null)
            {
                _logger.LogWarning("Reclaim method not found on Cashu service");
                return false;
            }

            var result = reclaimMethod.Invoke(_cashuService, new object[] { ecashToken, storeId });
            
            if (result is Task<bool> boolTask)
            {
                return await boolTask;
            }
            else if (result is Task task)
            {
                await task;
                return true; // Assume success if no return value
            }
            else if (result is bool success)
            {
                return success;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reclaiming Cashu token in store {StoreId}", storeId);
            return false;
        }
    }

    public async Task<bool> ValidateTokenAsync(string ecashToken)
    {
        if (!_cashuServiceAvailable || _cashuService == null)
        {
            _logger.LogWarning("Cashu plugin not available - cannot validate token");
            return false;
        }

        try
        {
            // Try to find a validation method
            var validateMethods = new[]
            {
                ("ValidateTokenAsync", new[] { typeof(string) }),
                ("ValidateAsync", new[] { typeof(string) }),
                ("IsValidAsync", new[] { typeof(string) }),
                ("ValidateToken", new[] { typeof(string) })
            };

            MethodInfo? validateMethod = null;
            
            foreach (var (methodName, paramTypes) in validateMethods)
            {
                validateMethod = _cashuService.GetType().GetMethod(methodName, paramTypes);
                if (validateMethod != null)
                {
                    break;
                }
            }

            if (validateMethod == null)
            {
                _logger.LogWarning("Validation method not found on Cashu service");
                return false;
            }

            var result = validateMethod.Invoke(_cashuService, new object[] { ecashToken });
            
            if (result is Task<bool> boolTask)
            {
                return await boolTask;
            }
            else if (result is bool isValid)
            {
                return isValid;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Cashu token");
            return false;
        }
    }

    public async Task<(bool Success, string? ErrorMessage, ulong? Amount)> ReceiveTokenAsync(string token, string storeId)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Token cannot be empty", null);
        }

        try
        {
            // Try to decode the token using CashuUtils or CashuTokenHelper
            object? decodedToken = null;
            bool tokenDecoded = false;

            // Method 1: Try CashuUtils.TryDecodeToken (from Cashu plugin)
            if (_cashuAssembly != null)
            {
                try
                {
                    var cashuUtilsType = _cashuAssembly.GetType("BTCPayServer.Plugins.Cashu.CashuAbstractions.CashuUtils");
                    if (cashuUtilsType != null)
                    {
                        var tryDecodeTokenMethod = cashuUtilsType.GetMethod("TryDecodeToken",
                            new[] { typeof(string), typeof(object).MakeByRefType() });

                        if (tryDecodeTokenMethod != null)
                        {
                            object? tokenPlaceholder = null;
                            var parameters = new object?[] { token, tokenPlaceholder };
                            var result = tryDecodeTokenMethod.Invoke(null, parameters);

                            if (result is bool isValid && isValid)
                            {
                                decodedToken = parameters[1];
                                tokenDecoded = true;
                                _logger.LogDebug("Successfully decoded token using CashuUtils.TryDecodeToken");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not decode token using CashuUtils, trying alternative method");
                }
            }

            // Method 2: Try CashuTokenHelper.Decode (from DotNut)
            if (!tokenDecoded)
            {
                try
                {
                    var cashuTokenHelperType = typeof(Proof).Assembly.GetTypes()
                        .FirstOrDefault(t => t.Name == "CashuTokenHelper");

                    if (cashuTokenHelperType != null)
                    {
                        var decodeMethod = cashuTokenHelperType.GetMethod("Decode",
                            new[] { typeof(string), typeof(string).MakeByRefType() });

                        if (decodeMethod != null)
                        {
                            string? memo = null;
                            var parameters = new object?[] { token, memo };
                            decodedToken = decodeMethod.Invoke(null, parameters);
                            if (decodedToken != null)
                            {
                                tokenDecoded = true;
                                _logger.LogDebug("Successfully decoded token using CashuTokenHelper.Decode");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not decode token using CashuTokenHelper");
                }
            }

            if (!tokenDecoded || decodedToken == null)
            {
                return (false, "Invalid Cashu token format", null);
            }

            // Extract proofs and mint URL from the decoded token
            var tokenType = decodedToken.GetType();
            var tokensProperty = tokenType.GetProperty("Tokens");
            if (tokensProperty == null)
            {
                return (false, "Token does not contain Tokens property", null);
            }

            var tokens = tokensProperty.GetValue(decodedToken) as System.Collections.IEnumerable;
            if (tokens == null)
            {
                return (false, "Token does not contain any tokens", null);
            }

            var allProofs = new List<Proof>();
            string? mintUrl = null;
            string? unit = null;

            foreach (var tokenItem in tokens)
            {
                var tokenItemType = tokenItem.GetType();
                
                // Get mint URL
                var mintProperty = tokenItemType.GetProperty("Mint");
                if (mintProperty != null)
                {
                    mintUrl = mintProperty.GetValue(tokenItem)?.ToString();
                }

                // Get unit
                var unitProperty = tokenType.GetProperty("Unit");
                if (unitProperty != null)
                {
                    unit = unitProperty.GetValue(decodedToken)?.ToString();
                }

                // Get proofs
                var proofsProperty = tokenItemType.GetProperty("Proofs");
                if (proofsProperty != null)
                {
                    var proofs = proofsProperty.GetValue(tokenItem) as System.Collections.IEnumerable;
                    if (proofs != null)
                    {
                        foreach (var proof in proofs)
                        {
                            if (proof is Proof dotNutProof)
                            {
                                allProofs.Add(dotNutProof);
                            }
                        }
                    }
                }
            }

            if (allProofs.Count == 0)
            {
                return (false, "Token does not contain any proofs", null);
            }

            if (string.IsNullOrWhiteSpace(mintUrl))
            {
                return (false, "Token does not contain a mint URL", null);
            }

            // Calculate total amount
            var totalAmount = allProofs.Aggregate(0UL, (sum, p) => sum + p.Amount);

            // Use CashuWallet.Receive() to swap proofs (matching Cashu plugin pattern)
            // Receive() handles fee calculation and swap logic internally
            _logger.LogInformation("Receiving {Count} proofs using CashuWallet.Receive()", allProofs.Count);
            
            // Try to use CashuWallet from Cashu plugin if available, otherwise use InternalCashuWallet
            object? wallet = CreateCashuWallet(mintUrl, unit ?? "sat");
            
            if (wallet == null)
            {
                _logger.LogError("Failed to create wallet for receiving token");
                return (false, "Failed to create wallet", null);
            }

            // Use Receive() method if available (CashuWallet), otherwise fall back to Swap (InternalCashuWallet)
            List<Proof> freshProofs;
            var walletType = wallet.GetType();
            var receiveMethod = walletType.GetMethod("Receive", new[] { typeof(List<Proof>), typeof(ulong) });
            
            if (receiveMethod != null)
            {
                // Use CashuWallet.Receive() - it handles swap internally with fee calculation
                _logger.LogDebug("Using CashuWallet.Receive() method");
                var receiveTask = receiveMethod.Invoke(wallet, new object[] { allProofs, 0UL }) as Task<object>;
                if (receiveTask != null)
                {
                    await receiveTask;
                    var receiveResult = receiveTask.GetType().GetProperty("Result")?.GetValue(receiveTask);
                    if (receiveResult != null)
                    {
                        var successProperty = receiveResult.GetType().GetProperty("Success");
                        var resultProofsProperty = receiveResult.GetType().GetProperty("ResultProofs");
                        var errorProperty = receiveResult.GetType().GetProperty("Error");
                        
                        if (successProperty != null && resultProofsProperty != null)
                        {
                            var success = (bool)(successProperty.GetValue(receiveResult) ?? false);
                            var resultProofs = resultProofsProperty.GetValue(receiveResult) as Proof[];
                            
                            if (!success || resultProofs == null || resultProofs.Length == 0)
                            {
                                var error = errorProperty?.GetValue(receiveResult);
                                var errorMsg = error?.GetType().GetProperty("Message")?.GetValue(error)?.ToString() ?? "Unknown error";
                                _logger.LogError("Failed to receive proofs: {Error}", errorMsg);
                                return (false, $"Failed to receive proofs: {errorMsg}", null);
                            }
                            
                            freshProofs = resultProofs.ToList();
                            _logger.LogInformation("Successfully received {Count} fresh proofs using CashuWallet.Receive()", freshProofs.Count);
                        }
                        else
                        {
                            _logger.LogError("Receive result structure unexpected");
                            return (false, "Failed to parse receive result", null);
                        }
                    }
                    else
                    {
                        _logger.LogError("Receive task returned null result");
                        return (false, "Receive operation failed", null);
                    }
                }
                else
                {
                    _logger.LogError("Receive method did not return a Task");
                    return (false, "Receive method signature unexpected", null);
                }
            }
            else
            {
                // Fallback to InternalCashuWallet.Swap() pattern
                _logger.LogDebug("Using InternalCashuWallet.Swap() as fallback");
                if (wallet is InternalCashuWallet internalWallet)
                {
                    var activeKeyset = await internalWallet.GetActiveKeyset();
                    var keysetId = new KeysetId(activeKeyset.Id.ToString());
                    var keys = await internalWallet.GetKeys(keysetId);
                    if (keys == null)
                    {
                        _logger.LogError("Could not get keys for keyset when receiving token");
                        return (false, "Could not get keys from mint", null);
                    }

                    var amounts = allProofs.Select(p => p.Amount).ToList();
                    var swapResult = await internalWallet.Swap(allProofs, amounts, keysetId, keys);
                    
                    if (!swapResult.Success || swapResult.ResultProofs == null || swapResult.ResultProofs.Length == 0)
                    {
                        var errorMsg = swapResult.Error?.Message ?? "Unknown error";
                        _logger.LogError("Failed to swap received proofs: {Error}", errorMsg);
                        return (false, $"Failed to swap proofs: {errorMsg}", null);
                    }

                    freshProofs = swapResult.ResultProofs.ToList();
                    _logger.LogInformation("Successfully swapped {Count} proofs, received {FreshCount} fresh proofs", 
                        allProofs.Count, freshProofs.Count);
                }
                else
                {
                    _logger.LogError("Wallet type not recognized for fallback");
                    return (false, "Wallet type not supported", null);
                }
            }

            // Store fresh proofs using ProofStorageService
            await _proofStorageService.AddProofsAsync(freshProofs, storeId, mintUrl);

            _logger.LogInformation("Successfully received Cashu token with {Count} fresh proofs totaling {Amount} sat for store {StoreId}",
                freshProofs.Count, totalAmount, storeId);

            return (true, null, totalAmount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving Cashu token for store {StoreId}", storeId);
            return (false, $"Error receiving token: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string? Token, string? ErrorMessage, ulong Amount)> ExportTokenAsync(string storeId, string mintUrl)
    {
        try
        {
            _logger.LogInformation("Exporting token for store {StoreId} on mint {MintUrl}", storeId, mintUrl);

            // Match Cashu plugin's ExportMintBalance approach
            // 1. Get keysets from mint
            var walletLogger = _serviceProvider.GetService(typeof(ILogger<InternalCashuWallet>)) as ILogger<InternalCashuWallet>;
            var wallet = new InternalCashuWallet(mintUrl, "sat", walletLogger);
            
            List<GetKeysetsResponse.KeysetItemResponse> keysets;
            try
            {
                keysets = await wallet.GetKeysets();
                if (keysets == null || keysets.Count == 0)
                {
                    _logger.LogError("No keysets found for mint {MintUrl}", mintUrl);
                    return (false, null, "Couldn't get keysets!", 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting keysets from mint {MintUrl}", mintUrl);
                return (false, null, "Couldn't get keysets!", 0);
            }

            // 2. Get StoredProof entities directly from database and filter by keyset IDs (matching Cashu plugin line 244-249)
            // This matches the Cashu plugin's exact approach - query StoredProof entities, then convert to Proof
            await using var db = _dbContextFactory.CreateContext();
            var selectedStoredProofs = db.Proofs.Where(p =>
                p.StoreId == storeId
                && p.MintUrl == mintUrl
                && keysets.Select(k => k.Id).Contains(p.Id)
                // Exclude proofs in FailedTransactions (matching Cashu plugin line 248)
                && !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p))
            ).ToList();

            if (selectedStoredProofs.Count == 0)
            {
                _logger.LogWarning("No proofs match active keysets for store {StoreId}", storeId);
                return (false, null, "No proofs available for active keysets", 0);
            }

            // 3. Convert StoredProof entities to DotNut Proof objects using ToDotNutProof() (matching Cashu plugin line 258)
            var dotNutProofs = selectedStoredProofs.Select(p => p.ToDotNutProof()).ToList();

            var tokenAmount = selectedStoredProofs.Select(p => p.Amount).Sum();
            _logger.LogInformation("Exporting {Count} proofs totaling {Amount} sat", dotNutProofs.Count, tokenAmount);

            // 4. Create token directly from proofs (NO swap - matching Cashu plugin line 251-263)
            var tokenItem = new CashuToken.Token
            {
                Mint = mintUrl,
                Proofs = dotNutProofs
            };

            // Validate token item before creating CashuToken
            if (string.IsNullOrWhiteSpace(tokenItem.Mint))
            {
                _logger.LogError("Token item has null or empty Mint");
                return (false, null, "Invalid mint URL", 0);
            }

            if (tokenItem.Proofs == null || tokenItem.Proofs.Count == 0)
            {
                _logger.LogError("Token item has no proofs");
                return (false, null, "No proofs in token", 0);
            }

            var createdToken = new CashuToken()
            {
                Tokens = [tokenItem],
                Memo = "Bitcoin Rewards Token",
                Unit = "sat"
            };
            
            // Validate token before encoding
            if (createdToken.Tokens == null || createdToken.Tokens.Count == 0)
            {
                _logger.LogError("Created token has no tokens");
                return (false, null, "Failed to create token structure", 0);
            }

            if (createdToken.Tokens[0].Proofs == null || createdToken.Tokens[0].Proofs.Count == 0)
            {
                _logger.LogError("Created token has no proofs");
                return (false, null, "Failed to create token with proofs", 0);
            }

            // Log token structure for debugging
            _logger.LogDebug("Token structure: Unit={Unit}, Memo={Memo}, TokensCount={TokensCount}, ProofsCount={ProofsCount}, Mint={Mint}",
                createdToken.Unit ?? "null", createdToken.Memo ?? "null", createdToken.Tokens.Count, 
                createdToken.Tokens[0].Proofs.Count, createdToken.Tokens[0].Mint ?? "null");

            string serializedToken;
            try
            {
                serializedToken = createdToken.Encode();
            }
            catch (NullReferenceException ex)
            {
                _logger.LogError(ex, "NullReferenceException when encoding token. Token structure: Unit={Unit}, Memo={Memo}, TokensCount={TokensCount}, ProofsCount={ProofsCount}, Mint={Mint}",
                    createdToken.Unit ?? "null", createdToken.Memo ?? "null", createdToken.Tokens?.Count ?? 0, 
                    createdToken.Tokens?[0].Proofs?.Count ?? 0, createdToken.Tokens?[0].Mint ?? "null");
                
                // Log first proof details for debugging
                if (createdToken.Tokens?[0].Proofs?.Count > 0)
                {
                    var firstProof = createdToken.Tokens[0].Proofs[0];
                    _logger.LogError("First proof: Id={Id}, Amount={Amount}, Secret={Secret}, C={C}, DLEQ={DLEQ}",
                        firstProof.Id?.ToString() ?? "null", firstProof.Amount, 
                        firstProof.Secret != null ? "not null" : "null",
                        firstProof.C != null ? "not null" : "null",
                        firstProof.DLEQ != null ? "not null" : "null");
                }
                
                return (false, null, $"Failed to encode token: {ex.Message}", 0);
            }
            
            if (string.IsNullOrEmpty(serializedToken))
            {
                _logger.LogError("Failed to encode token");
                return (false, null, "Failed to create token", 0);
            }

            // 5. Remove proofs and store ExportedToken in a transaction (matching Cashu plugin line 268-291)
            var proofsToRemove = await db.Proofs
                .Where(p => p.StoreId == storeId 
                    && p.MintUrl == mintUrl
                    && keysets.Select(k => k.Id).Contains(p.Id))
                .ToListAsync();
            
            var exportedTokenEntity = new Data.Models.ExportedToken
            {
                SerializedToken = serializedToken,
                Amount = tokenAmount,
                Unit = "sat",
                Mint = mintUrl,
                StoreId = storeId,
                IsUsed = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    db.Proofs.RemoveRange(proofsToRemove);
                    db.ExportedTokens.Add(exportedTokenEntity);
                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            _logger.LogInformation("Successfully exported token for {Amount} sat and removed proofs from database", tokenAmount);

            return (true, serializedToken, null, tokenAmount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting token for store {StoreId}", storeId);
            return (false, null, ex.Message, 0);
        }
    }
}

