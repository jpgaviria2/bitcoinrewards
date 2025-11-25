#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Adapter to interact with BTCPay Server's Cashu plugin using reflection.
/// Uses reflection to avoid compile-time dependencies on the Cashu plugin.
/// </summary>
public class CashuServiceAdapter : ICashuService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CashuServiceAdapter> _logger;
    private readonly bool _cashuServiceAvailable;
    private readonly object? _cashuService;

    public CashuServiceAdapter(
        IServiceProvider serviceProvider,
        ILogger<CashuServiceAdapter> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        try
        {
            // Try to find Cashu plugin assembly and service
            var cashuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "BTCPayServer.Plugins.Cashu" || 
                                    a.FullName?.Contains("Cashu") == true);
            
            if (cashuAssembly != null)
            {
                // Look for CashuWallet service - common name pattern in Cashu plugins
                // Try multiple possible service type names
                var serviceTypeNames = new[]
                {
                    "BTCPayServer.Plugins.Cashu.Services.CashuService",
                    "BTCPayServer.Plugins.Cashu.CashuAbstractions.CashuWallet",
                    "BTCPayServer.Plugins.Cashu.Services.ICashuService",
                    "BTCPayServer.Plugins.Cashu.Services.CashuWalletService"
                };
                
                foreach (var typeName in serviceTypeNames)
                {
                    var serviceType = cashuAssembly.GetType(typeName);
                    if (serviceType != null)
                    {
                        _cashuService = _serviceProvider.GetService(serviceType);
                        if (_cashuService != null)
                        {
                            _cashuServiceAvailable = true;
                            _logger.LogInformation("Cashu plugin service found: {ServiceType}", typeName);
                            break;
                        }
                    }
                }
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
            _logger.LogError(ex, "Error initializing CashuServiceAdapter. Cashu plugin service could not be resolved.");
        }
    }

    public async Task<string?> MintTokenAsync(long amountSatoshis, string storeId)
    {
        if (!_cashuServiceAvailable || _cashuService == null)
        {
            _logger.LogWarning("Cashu plugin not available - cannot mint token for {AmountSatoshis} sats in store {StoreId}", 
                amountSatoshis, storeId);
            return null;
        }

        try
        {
            // Convert satoshis to BTC (Cashu typically works with BTC amounts)
            var btcAmount = amountSatoshis / 100_000_000m;
            
            // Try to find a minting method on the Cashu service
            // Common method names: Mint, MintToken, MintAsync, CreateToken, etc.
            var mintMethods = new[]
            {
                ("MintAsync", new[] { typeof(decimal), typeof(string) }),
                ("MintTokenAsync", new[] { typeof(decimal), typeof(string) }),
                ("Mint", new[] { typeof(decimal), typeof(string) }),
                ("MintToken", new[] { typeof(decimal), typeof(string) }),
                ("CreateTokenAsync", new[] { typeof(long), typeof(string) }),
                ("MintAsync", new[] { typeof(long), typeof(string) })
            };

            MethodInfo? mintMethod = null;
            object[] parameters = Array.Empty<object>();
            
            foreach (var (methodName, paramTypes) in mintMethods)
            {
                mintMethod = _cashuService.GetType().GetMethod(methodName, paramTypes);
                if (mintMethod != null)
                {
                    // Prepare parameters based on method signature
                    if (paramTypes[0] == typeof(decimal))
                    {
                        parameters = new object[] { btcAmount, storeId };
                    }
                    else if (paramTypes[0] == typeof(long))
                    {
                        parameters = new object[] { amountSatoshis, storeId };
                    }
                    break;
                }
            }

            if (mintMethod == null)
            {
                _logger.LogError("Minting method not found on Cashu service. Available methods: {Methods}",
                    string.Join(", ", _cashuService.GetType().GetMethods().Select(m => m.Name)));
                return null;
            }

            // Invoke the minting method
            var result = mintMethod.Invoke(_cashuService, parameters);
            
            // Handle async methods
            if (result is Task<string?> stringTask)
            {
                return await stringTask;
            }
            else if (result is Task<object?> objectTask)
            {
                var taskResult = await objectTask;
                return taskResult?.ToString();
            }
            else if (result is string token)
            {
                return token;
            }
            else if (result != null)
            {
                // Try to get result from Task<T>
                var resultType = result.GetType();
                if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    await ((Task)result);
                    var resultProperty = resultType.GetProperty("Result");
                    var taskResult = resultProperty?.GetValue(result);
                    return taskResult?.ToString();
                }
            }

            _logger.LogWarning("Mint method returned unexpected type: {ResultType}", result?.GetType().Name);
            return null;
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

