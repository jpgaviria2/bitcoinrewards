#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

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
        StoreRepository storeRepository)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _storeRepository = storeRepository;
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

    private async Task<string?> GetMintUrlForStore(string storeId)
    {
        try
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
            {
                _logger.LogWarning("Store {StoreId} not found", storeId);
                return null;
            }

            // Get Cashu payment method config
            if (_paymentMethodHandlers == null)
            {
                _logger.LogWarning("PaymentMethodHandlerDictionary not available");
                return null;
            }

            // Get CashuPmid from CashuPlugin
            var cashuPluginType = _cashuAssembly?.GetType("BTCPayServer.Plugins.Cashu.CashuPlugin");
            if (cashuPluginType == null)
            {
                _logger.LogWarning("CashuPlugin type not found");
                return null;
            }

            var cashuPmidField = cashuPluginType.GetField("CashuPmid", BindingFlags.Public | BindingFlags.Static);
            if (cashuPmidField == null)
            {
                _logger.LogWarning("CashuPmid field not found");
                return null;
            }

            var cashuPmid = cashuPmidField.GetValue(null);
            if (cashuPmid == null)
            {
                _logger.LogWarning("CashuPmid is null");
                return null;
            }

            // Get payment method config
            var getPaymentMethodConfigMethod = typeof(StoreData).GetMethod("GetPaymentMethodConfig", 
                new[] { cashuPmid.GetType(), _paymentMethodHandlers.GetType() });
            if (getPaymentMethodConfigMethod == null)
            {
                _logger.LogWarning("GetPaymentMethodConfig method not found");
                return null;
            }

            var config = getPaymentMethodConfigMethod.Invoke(store, new[] { cashuPmid, _paymentMethodHandlers });
            if (config == null)
            {
                _logger.LogWarning("Cashu payment method config not found for store {StoreId}", storeId);
                return null;
            }

            // Get TrustedMintsUrls property
            var trustedMintsProperty = config.GetType().GetProperty("TrustedMintsUrls");
            if (trustedMintsProperty == null)
            {
                _logger.LogWarning("TrustedMintsUrls property not found");
                return null;
            }

            var trustedMints = trustedMintsProperty.GetValue(config) as List<string>;
            if (trustedMints == null || trustedMints.Count == 0)
            {
                _logger.LogWarning("No trusted mints configured for store {StoreId}", storeId);
                return null;
            }

            var mintUrl = trustedMints.First();
            _logger.LogDebug("Found mint URL for store {StoreId}: {MintUrl}", storeId, mintUrl);
            return mintUrl;
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
            if (_cashuDbContextFactory == null)
            {
                _logger.LogWarning("CashuDbContextFactory not available");
                return 0;
            }

            // Create database context
            var createContextMethod = _cashuDbContextFactory.GetType().GetMethod("CreateContext");
            if (createContextMethod == null)
            {
                _logger.LogWarning("CreateContext method not found on CashuDbContextFactory");
                return 0;
            }

            var dbContext = createContextMethod.Invoke(_cashuDbContextFactory, null);
            if (dbContext == null)
            {
                _logger.LogWarning("Failed to create CashuDbContext");
                return 0;
            }

            try
            {
                // Get Proofs DbSet
                var proofsProperty = dbContext.GetType().GetProperty("Proofs");
                if (proofsProperty == null)
                {
                    _logger.LogWarning("Proofs property not found on CashuDbContext");
                    return 0;
                }

                var proofsDbSet = proofsProperty.GetValue(dbContext);
                if (proofsDbSet == null)
                {
                    return 0;
                }

                // Get active keysets for the mint
                var cashuUtilsType = _cashuAssembly?.GetType("BTCPayServer.Plugins.Cashu.CashuAbstractions.CashuUtils");
                if (cashuUtilsType == null)
                {
                    _logger.LogWarning("CashuUtils type not found");
                    return 0;
                }

                var getCashuHttpClientMethod = cashuUtilsType.GetMethod("GetCashuHttpClient", 
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (getCashuHttpClientMethod == null)
                {
                    _logger.LogWarning("GetCashuHttpClient method not found");
                    return 0;
                }

                var cashuHttpClient = getCashuHttpClientMethod.Invoke(null, new object[] { mintUrl });
                if (cashuHttpClient == null)
                {
                    _logger.LogWarning("Failed to create CashuHttpClient");
                    return 0;
                }

                var getKeysetsMethod = cashuHttpClient.GetType().GetMethod("GetKeysets");
                if (getKeysetsMethod == null)
                {
                    _logger.LogWarning("GetKeysets method not found");
                    return 0;
                }

                var keysetsTask = getKeysetsMethod.Invoke(cashuHttpClient, null) as Task;
                if (keysetsTask == null)
                {
                    _logger.LogWarning("GetKeysets did not return a Task");
                    return 0;
                }

                await keysetsTask;
                var keysetsResult = keysetsTask.GetType().GetProperty("Result")?.GetValue(keysetsTask);
                if (keysetsResult == null)
                {
                    _logger.LogWarning("GetKeysets result is null");
                    return 0;
                }

                // Get Keysets property from result
                var keysetsProperty = keysetsResult.GetType().GetProperty("Keysets");
                if (keysetsProperty == null)
                {
                    _logger.LogWarning("Keysets property not found");
                    return 0;
                }

                var keysets = keysetsProperty.GetValue(keysetsResult) as System.Collections.IEnumerable;
                if (keysets == null)
                {
                    return 0;
                }

                // Get keyset IDs
                var keysetIds = new List<string>();
                foreach (var keyset in keysets)
                {
                    var idProperty = keyset.GetType().GetProperty("Id");
                    if (idProperty != null)
                    {
                        var id = idProperty.GetValue(keyset);
                        if (id != null)
                        {
                            keysetIds.Add(id.ToString() ?? string.Empty);
                        }
                    }
                }

                // Query proofs from database
                var whereMethod = typeof(EntityFrameworkQueryableExtensions)
                    .GetMethods()
                    .FirstOrDefault(m => m.Name == "Where" && m.GetParameters().Length == 2);
                
                // Use reflection to build query: Proofs.Where(p => keysetIds.Contains(p.Id.ToString()) && p.StoreId == storeId && !FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)))
                // For simplicity, we'll query all proofs for the store and filter in memory
                var toListAsyncMethod = typeof(EntityFrameworkQueryableExtensions)
                    .GetMethods()
                    .FirstOrDefault(m => m.Name == "ToListAsync" && m.GetParameters().Length == 1);

                if (toListAsyncMethod == null)
                {
                    _logger.LogWarning("ToListAsync method not found");
                    return 0;
                }

                var toListAsyncGeneric = toListAsyncMethod.MakeGenericMethod(
                    proofsDbSet.GetType().GetGenericArguments()[0]);
                
                var proofsTask = toListAsyncGeneric.Invoke(null, new[] { proofsDbSet }) as Task;
                if (proofsTask == null)
                {
                    _logger.LogWarning("ToListAsync did not return a Task");
                    return 0;
                }

                await proofsTask;
                var proofsList = proofsTask.GetType().GetProperty("Result")?.GetValue(proofsTask) as System.Collections.IEnumerable;
                if (proofsList == null)
                {
                    return 0;
                }

                // Filter and sum proofs
                ulong totalBalance = 0;
                foreach (var proof in proofsList)
                {
                    var storeIdProperty = proof.GetType().GetProperty("StoreId");
                    var idProperty = proof.GetType().GetProperty("Id");
                    var amountProperty = proof.GetType().GetProperty("Amount");

                    if (storeIdProperty?.GetValue(proof)?.ToString() == storeId &&
                        idProperty?.GetValue(proof) != null)
                    {
                        var proofId = idProperty.GetValue(proof)?.ToString();
                        if (keysetIds.Contains(proofId ?? string.Empty))
                        {
                            var amount = amountProperty?.GetValue(proof);
                            if (amount != null && ulong.TryParse(amount.ToString(), out var amountValue))
                            {
                                totalBalance += amountValue;
                            }
                        }
                    }
                }

                _logger.LogInformation("Ecash balance for store {StoreId} on mint {MintUrl}: {Balance} sat", 
                    storeId, mintUrl, totalBalance);
                return (long)totalBalance;
            }
            finally
            {
                // Dispose context if it implements IDisposable
                if (dbContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                        }
                    }
                    catch (Exception ex)
                    {
            _logger.LogError(ex, "Error getting ecash balance for store {StoreId}", storeId);
            return 0;
        }
    }

    private async Task<long> GetLightningBalanceAsync(string storeId)
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

            var getNetworkMethod = networkProviderType.GetMethod("GetNetwork", new[] { typeof(string) });
            if (getNetworkMethod == null)
            {
                _logger.LogWarning("GetNetwork method not found");
                return 0;
            }

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
            if (_cashuDbContextFactory == null || _cashuWalletType == null)
            {
                return null;
            }

            // Create wallet to get keysets
            var walletConstructor = _cashuWalletType.GetConstructor(new[] { typeof(string), typeof(string), _cashuDbContextFactory.GetType() });
            if (walletConstructor == null)
            {
                _logger.LogWarning("CashuWallet constructor not found");
                return null;
            }

            var wallet = walletConstructor.Invoke(new object[] { mintUrl, "sat", _cashuDbContextFactory });
            if (wallet == null)
            {
                return null;
            }

            // Get keysets
            var getKeysetsMethod = _cashuWalletType.GetMethod("GetKeysets");
            if (getKeysetsMethod == null)
            {
                return null;
            }

            var keysetsTask = getKeysetsMethod.Invoke(wallet, null) as Task;
            if (keysetsTask == null)
            {
                return null;
            }

            await keysetsTask;
            var keysets = keysetsTask.GetType().GetProperty("Result")?.GetValue(keysetsTask) as System.Collections.IEnumerable;
            if (keysets == null)
            {
                return null;
            }

            var keysetIds = new List<string>();
            foreach (var keyset in keysets)
            {
                var idProperty = keyset.GetType().GetProperty("Id");
                if (idProperty?.GetValue(keyset) != null)
                {
                    keysetIds.Add(idProperty.GetValue(keyset)?.ToString() ?? string.Empty);
                }
            }

            // Get proofs from database
            var createContextMethod = _cashuDbContextFactory.GetType().GetMethod("CreateContext");
            if (createContextMethod == null)
            {
                return null;
            }

            var dbContext = createContextMethod.Invoke(_cashuDbContextFactory, null);
            if (dbContext == null)
            {
                return null;
            }

            try
            {
                var proofsProperty = dbContext.GetType().GetProperty("Proofs");
                if (proofsProperty == null)
                {
                    return null;
                }

                var proofsDbSet = proofsProperty.GetValue(dbContext);
                if (proofsDbSet == null)
                {
                    return null;
                }

                var toListAsyncMethod = typeof(EntityFrameworkQueryableExtensions)
                    .GetMethods()
                    .FirstOrDefault(m => m.Name == "ToListAsync" && m.GetParameters().Length == 1);
                if (toListAsyncMethod == null)
                {
                    return null;
                }

                var toListAsyncGeneric = toListAsyncMethod.MakeGenericMethod(
                    proofsDbSet.GetType().GetGenericArguments()[0]);
                var proofsTask = toListAsyncGeneric.Invoke(null, new[] { proofsDbSet }) as Task;
                if (proofsTask == null)
                {
                    return null;
                }

                await proofsTask;
                var proofsList = proofsTask.GetType().GetProperty("Result")?.GetValue(proofsTask) as System.Collections.IEnumerable;
                if (proofsList == null)
                {
                    return new List<object>();
                }

                // Filter proofs by store, mint, and amount
                var selectedProofs = new List<object>();
                ulong totalAmount = 0;

                foreach (var proof in proofsList)
                {
                    var storeIdProperty = proof.GetType().GetProperty("StoreId");
                    var idProperty = proof.GetType().GetProperty("Id");
                    var amountProperty = proof.GetType().GetProperty("Amount");

                    if (storeIdProperty?.GetValue(proof)?.ToString() == storeId &&
                        idProperty?.GetValue(proof) != null)
                    {
                        var proofId = idProperty.GetValue(proof)?.ToString();
                        if (keysetIds.Contains(proofId ?? string.Empty))
                        {
                            var amount = amountProperty?.GetValue(proof);
                            if (amount != null && ulong.TryParse(amount.ToString(), out var amountValue))
                            {
                                if (totalAmount < maxAmount)
                                {
                                    selectedProofs.Add(proof);
                                    totalAmount += amountValue;
                                    if (totalAmount >= maxAmount)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                return selectedProofs;
            }
            finally
            {
                if (dbContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
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
            if (_cashuWalletType == null || _cashuDbContextFactory == null)
            {
                _logger.LogWarning("CashuWallet type or DbContextFactory not available");
                return null;
            }

            _logger.LogInformation("Swapping proofs for {Amount} sat on mint {MintUrl}", amount, mintUrl);

            // Get stored proofs
            var proofs = await GetStoredProofsAsync(storeId, mintUrl, amount);
            if (proofs == null || proofs.Count == 0)
            {
                _logger.LogWarning("No proofs available for swapping");
                return null;
            }

            // Create wallet instance
            var walletConstructor = _cashuWalletType.GetConstructor(new[] { typeof(string), typeof(string), _cashuDbContextFactory.GetType() });
            if (walletConstructor == null)
            {
                return null;
            }

            var wallet = walletConstructor.Invoke(new object[] { mintUrl, "sat", _cashuDbContextFactory });
            if (wallet == null)
            {
                return null;
            }

            // Convert stored proofs to DotNut Proof objects
            var proofList = new List<object>();
            foreach (var storedProof in proofs)
            {
                var toDotNutProofMethod = storedProof.GetType().GetMethod("ToDotNutProof");
                if (toDotNutProofMethod != null)
                {
                    var dotNutProof = toDotNutProofMethod.Invoke(storedProof, null);
                    if (dotNutProof != null)
                    {
                        proofList.Add(dotNutProof);
                    }
                }
            }

            if (proofList.Count == 0)
            {
                _logger.LogWarning("No valid proofs to swap");
                return null;
            }

            // Get active keyset and keys
            var getActiveKeysetMethod = _cashuWalletType.GetMethod("GetActiveKeyset");
            if (getActiveKeysetMethod == null)
            {
                return null;
            }

            var keysetTask = getActiveKeysetMethod.Invoke(wallet, null) as Task;
            if (keysetTask == null)
            {
                return null;
            }

            await keysetTask;
            var activeKeyset = keysetTask.GetType().GetProperty("Result")?.GetValue(keysetTask);
            if (activeKeyset == null)
            {
                return null;
            }

            var keysetIdProperty = activeKeyset.GetType().GetProperty("Id");
            var keysetId = keysetIdProperty?.GetValue(activeKeyset);
            if (keysetId == null)
            {
                return null;
            }

            var getKeysMethod = _cashuWalletType.GetMethod("GetKeys", new[] { keysetId.GetType(), typeof(bool) });
            if (getKeysMethod == null)
            {
                getKeysMethod = _cashuWalletType.GetMethod("GetKeys", new[] { keysetId.GetType() });
            }

            if (getKeysMethod == null)
            {
                return null;
            }

            var keysTask = getKeysMethod.Invoke(wallet, new[] { keysetId, false }) as Task;
            if (keysTask == null)
            {
                keysTask = getKeysMethod.Invoke(wallet, new[] { keysetId }) as Task;
            }

            if (keysTask == null)
            {
                return null;
            }

            await keysTask;
            var keys = keysTask.GetType().GetProperty("Result")?.GetValue(keysTask);
            if (keys == null)
            {
                return null;
            }

            // Split amount to proof amounts
            var cashuUtilsType = _cashuAssembly?.GetType("BTCPayServer.Plugins.Cashu.CashuAbstractions.CashuUtils");
            if (cashuUtilsType == null)
            {
                return null;
            }

            var splitToProofsAmountsMethod = cashuUtilsType.GetMethod("SplitToProofsAmounts",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ulong), keys.GetType() }, null);
            if (splitToProofsAmountsMethod == null)
            {
                return null;
            }

            var outputAmounts = splitToProofsAmountsMethod.Invoke(null, new object[] { amount, keys }) as List<ulong>;
            if (outputAmounts == null || outputAmounts.Count == 0)
            {
                return null;
            }

            // Call Swap method
            var swapMethod = _cashuWalletType.GetMethod("Swap",
                new[] { typeof(List<>).MakeGenericType(proofList[0].GetType()), typeof(List<ulong>), keysetId.GetType(), keys.GetType() });
            if (swapMethod == null)
            {
                // Try without keysetId and keys parameters
                swapMethod = _cashuWalletType.GetMethod("Swap",
                    new[] { typeof(List<>).MakeGenericType(proofList[0].GetType()), typeof(List<ulong>) });
            }

            if (swapMethod == null)
            {
                _logger.LogWarning("Swap method not found on CashuWallet");
                return null;
            }

            // Create typed list
            var proofType = proofList[0].GetType();
            var listType = typeof(List<>).MakeGenericType(proofType);
            var typedProofList = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");
            foreach (var proof in proofList)
            {
                addMethod?.Invoke(typedProofList, new[] { proof });
            }

            object? swapResult;
            if (swapMethod.GetParameters().Length == 4)
            {
                swapResult = swapMethod.Invoke(wallet, new[] { typedProofList, outputAmounts, keysetId, keys });
            }
            else
            {
                swapResult = swapMethod.Invoke(wallet, new[] { typedProofList, outputAmounts });
            }

            if (swapResult == null)
            {
                return null;
            }

            var swapTask = swapResult as Task;
            if (swapTask != null)
            {
                await swapTask;
                swapResult = swapTask.GetType().GetProperty("Result")?.GetValue(swapTask);
            }

            if (swapResult == null)
            {
                return null;
            }

            // Get ResultProofs from SwapResult
            var resultProofsProperty = swapResult.GetType().GetProperty("ResultProofs");
            if (resultProofsProperty == null)
            {
                return null;
            }

            var resultProofs = resultProofsProperty.GetValue(swapResult) as Array;
            if (resultProofs == null || resultProofs.Length == 0)
            {
                _logger.LogWarning("Swap returned no proofs");
                return null;
            }

            // Convert proofs to list
            var resultProofList = new List<object>();
            foreach (var proof in resultProofs)
            {
                resultProofList.Add(proof);
            }

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

            if (_cashuWalletType == null || _cashuDbContextFactory == null)
            {
                _logger.LogWarning("CashuWallet type or DbContextFactory not available");
                return null;
            }

            // Get Lightning client (reuse logic from GetLightningBalanceAsync)
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
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
            var getNetworkMethod = networkProviderType?.GetMethod("GetNetwork", new[] { typeof(string) });
            var network = getNetworkMethod?.Invoke(networkProvider, new object[] { "BTC" });
            if (network == null)
            {
                return null;
            }

            // Get Lightning client (simplified - reuse GetLightningBalanceAsync logic)
            var lightningClient = await GetLightningClientForStore(storeId);
            if (lightningClient == null)
            {
                _logger.LogWarning("Lightning client not available for store {StoreId}", storeId);
                return null;
            }

            // Create wallet with Lightning client
            var walletConstructor = _cashuWalletType.GetConstructor(
                new[] { lightningClient.GetType(), typeof(string), typeof(string), _cashuDbContextFactory.GetType() });
            if (walletConstructor == null)
            {
                return null;
            }

            var wallet = walletConstructor.Invoke(new object[] { lightningClient, mintUrl, "sat", _cashuDbContextFactory });
            if (wallet == null)
            {
                return null;
            }

            // Create mint quote (NUT-04)
            var cashuHttpClientType = _cashuAssembly?.GetType("BTCPayServer.Plugins.Cashu.CashuAbstractions.CashuUtils");
            if (cashuHttpClientType == null)
            {
                return null;
            }

            var getCashuHttpClientMethod = cashuHttpClientType.GetMethod("GetCashuHttpClient",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (getCashuHttpClientMethod == null)
            {
                return null;
            }

            var cashuHttpClient = getCashuHttpClientMethod.Invoke(null, new object[] { mintUrl });
            if (cashuHttpClient == null)
            {
                return null;
            }

            // Create mint quote
            var createMintQuoteMethod = cashuHttpClient.GetType().GetMethod("CreateMintQuote");
            if (createMintQuoteMethod == null)
            {
                return null;
            }

            // Get generic method
            var mintQuoteRequestType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "PostMintQuoteBolt11Request");
            var mintQuoteResponseType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "PostMintQuoteBolt11Response");

            if (mintQuoteRequestType == null || mintQuoteResponseType == null)
            {
                _logger.LogWarning("Mint quote types not found");
                return null;
            }

            var genericCreateMintQuote = createMintQuoteMethod.MakeGenericMethod(mintQuoteResponseType, mintQuoteRequestType);
            
            // Create request
            var requestType = mintQuoteRequestType;
            var request = Activator.CreateInstance(requestType);
            if (request == null)
            {
                return null;
            }
            var amountProperty = requestType.GetProperty("Amount");
            var unitProperty = requestType.GetProperty("Unit");
            amountProperty?.SetValue(request, (ulong)amountSatoshis);
            unitProperty?.SetValue(request, "sat");

            // Create mint quote
            var mintQuoteTask = genericCreateMintQuote.Invoke(cashuHttpClient, new object[] { "bolt11", request }) as Task;
            if (mintQuoteTask == null)
            {
                return null;
            }

            await mintQuoteTask;
            var mintQuote = mintQuoteTask.GetType().GetProperty("Result")?.GetValue(mintQuoteTask);
            if (mintQuote == null)
            {
                return null;
            }

            // Get invoice from quote
            var requestProperty = mintQuote.GetType().GetProperty("Request");
            var invoiceBolt11 = requestProperty?.GetValue(mintQuote)?.ToString();
            if (string.IsNullOrEmpty(invoiceBolt11))
            {
                return null;
            }

            // Pay invoice
            var payMethod = lightningClient.GetType().GetMethod("Pay");
            if (payMethod == null)
            {
                return null;
            }

            var cancellationTokenType = typeof(CancellationToken);
            var defaultCancellationToken = cancellationTokenType.GetProperty("None", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? default(CancellationToken);
            var payTask = payMethod.Invoke(lightningClient, new object[] { invoiceBolt11, defaultCancellationToken }) as Task;
            if (payTask == null)
            {
                return null;
            }

            await payTask;
            var payResult = payTask.GetType().GetProperty("Result")?.GetValue(payTask);
            if (payResult == null)
            {
                _logger.LogWarning("Lightning payment failed");
                return null;
            }

            // Check payment result
            var resultProperty = payResult.GetType().GetProperty("Result");
            var paymentResult = resultProperty?.GetValue(payResult);
            if (paymentResult?.ToString() != "OK")
            {
                _logger.LogWarning("Lightning payment result: {Result}", paymentResult);
                return null;
            }

            // Get quote ID and check status
            var quoteProperty = mintQuote.GetType().GetProperty("Quote");
            var quoteId = quoteProperty?.GetValue(mintQuote)?.ToString();
            if (string.IsNullOrEmpty(quoteId))
            {
                return null;
            }

            // Check mint quote status - try CheckMintQuote first, fall back to CheckMeltQuote
            var checkMintQuoteMethod = cashuHttpClient.GetType().GetMethod("CheckMintQuote");
            if (checkMintQuoteMethod == null)
            {
                // Fall back to CheckMeltQuote if CheckMintQuote doesn't exist
                checkMintQuoteMethod = cashuHttpClient.GetType().GetMethod("CheckMeltQuote");
            }

            if (checkMintQuoteMethod == null)
            {
                _logger.LogWarning("CheckMintQuote/CheckMeltQuote method not found");
                return null;
            }

            var genericCheckQuote = checkMintQuoteMethod.MakeGenericMethod(mintQuoteResponseType);
            
            // Poll for quote completion
            for (int i = 0; i < 30; i++) // Poll up to 30 times (30 seconds)
            {
                await Task.Delay(1000);
                var checkTask = genericCheckQuote.Invoke(cashuHttpClient, new object[] { "bolt11", quoteId, default(CancellationToken) }) as Task;
                if (checkTask == null)
                {
                    continue;
                }

                await checkTask;
                var checkResult = checkTask.GetType().GetProperty("Result")?.GetValue(checkTask);
                if (checkResult == null)
                {
                    continue;
                }

                var paidProperty = checkResult.GetType().GetProperty("Paid");
                var paid = paidProperty?.GetValue(checkResult) as bool?;
                if (paid == true)
                {
                    // Get proofs from quote
                    var proofsProperty = checkResult.GetType().GetProperty("Proofs");
                    var proofs = proofsProperty?.GetValue(checkResult) as Array;
                    if (proofs != null && proofs.Length > 0)
                    {
                        var proofList = new List<object>();
                        foreach (var proof in proofs)
                        {
                            if (proof != null)
                            {
                                proofList.Add(proof);
                            }
                        }

                        // Store proofs in database
                        if (_cashuService != null && proofList.Count > 0)
                        {
                            var firstProof = proofList[0];
                            var addProofsMethod = _cashuService.GetType().GetMethod("AddProofsToDb",
                                new[] { typeof(IEnumerable<>).MakeGenericType(firstProof.GetType()), typeof(string), typeof(string) });
                            if (addProofsMethod != null)
                            {
                                var addTask = addProofsMethod.Invoke(_cashuService, new object[] { proofs, storeId, mintUrl }) as Task;
                                if (addTask != null)
                                {
                                    await addTask;
                                }
                            }
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
            var getNetworkMethod = networkProviderType.GetMethod("GetNetwork", new[] { typeof(string) });
            if (getNetworkMethod == null || networkProvider == null)
            {
                return null;
            }
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
                return Task.FromResult<string?>(null);
            }

            // Get CashuToken type
            var cashuTokenType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "CashuToken");
            if (cashuTokenType == null)
            {
                _logger.LogWarning("CashuToken type not found");
                return Task.FromResult<string?>(null);
            }

            // Create token
            var token = Activator.CreateInstance(cashuTokenType);
            if (token == null)
            {
                return Task.FromResult<string?>(null);
            }

            // Set Tokens property
            var tokensProperty = cashuTokenType.GetProperty("Tokens");
            if (tokensProperty == null)
            {
                return Task.FromResult<string?>(null);
            }

            // Create Token object
            var tokenItemType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "Token" && t.Namespace?.Contains("Cashu") == true);
            if (tokenItemType == null)
            {
                return Task.FromResult<string?>(null);
            }

            var tokenItem = Activator.CreateInstance(tokenItemType);
            if (tokenItem == null)
            {
                return Task.FromResult<string?>(null);
            }

            var mintProperty = tokenItemType.GetProperty("Mint");
            var proofsProperty = tokenItemType.GetProperty("Proofs");
            mintProperty?.SetValue(tokenItem, mintUrl);
            
            // Convert proofs to list
            var proofType = proofs[0].GetType();
            var listType = typeof(List<>).MakeGenericType(proofType);
            var proofList = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");
            foreach (var proof in proofs)
            {
                addMethod?.Invoke(proofList, new[] { proof });
            }
            proofsProperty?.SetValue(tokenItem, proofList);

            // Set tokens array
            var arrayType = tokenItemType.MakeArrayType();
            var tokensArray = Array.CreateInstance(tokenItemType, 1);
            tokensArray.SetValue(tokenItem, 0);
            tokensProperty.SetValue(token, tokensArray);

            // Set Unit and Memo
            var unitProperty = cashuTokenType.GetProperty("Unit");
            var memoProperty = cashuTokenType.GetProperty("Memo");
            unitProperty?.SetValue(token, unit);
            memoProperty?.SetValue(token, "Bitcoin Rewards Token");

            // Encode token
            var encodeMethod = cashuTokenType.GetMethod("Encode");
            if (encodeMethod == null)
            {
                // Try CashuTokenHelper.Decode/Encode
                var cashuTokenHelperType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "CashuTokenHelper");
                if (cashuTokenHelperType != null)
                {
                    var helperEncodeMethod = cashuTokenHelperType.GetMethod("Encode",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { cashuTokenType }, null);
                    if (helperEncodeMethod != null)
                    {
                        var encoded = helperEncodeMethod.Invoke(null, new object[] { token });
                        return Task.FromResult<string?>(encoded?.ToString());
                    }
                }
                return Task.FromResult<string?>(null);
            }

            var encodedToken = encodeMethod.Invoke(token, null);
            return Task.FromResult<string?>(encodedToken?.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating token from proofs");
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
                    _logger.LogWarning("Swap operation failed, falling back to Lightning minting");
                }
            }

            // 3b. Check Lightning balance
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
}

