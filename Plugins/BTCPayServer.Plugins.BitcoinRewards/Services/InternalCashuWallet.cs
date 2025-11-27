#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using DotNut;
using DotNut.Api;
using DotNut.ApiModels;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Internal Cashu wallet implementation using DotNut directly.
/// This avoids dependency on Cashu plugin's internal services via reflection.
/// </summary>
public class InternalCashuWallet
{
    private readonly CashuHttpClient _cashuHttpClient;
    private readonly ILightningClient? _lightningClient;
    private readonly string _mintUrl;
    private readonly string _unit;
    private readonly ILogger<InternalCashuWallet>? _logger;
    private Keyset? _keys;
    private List<GetKeysetsResponse.KeysetItemResponse>? _keysets;

    public bool HasLightningClient => _lightningClient is not null;

    public InternalCashuWallet(string mintUrl, string unit = "sat", ILogger<InternalCashuWallet>? logger = null)
    {
        _mintUrl = mintUrl;
        _unit = unit;
        _logger = logger;
        _cashuHttpClient = GetCashuHttpClient(mintUrl);
    }

    public InternalCashuWallet(ILightningClient lightningClient, string mintUrl, string unit = "sat", ILogger<InternalCashuWallet>? logger = null)
    {
        _lightningClient = lightningClient;
        _mintUrl = mintUrl;
        _unit = unit;
        _logger = logger;
        _cashuHttpClient = GetCashuHttpClient(mintUrl);
    }

    private static CashuHttpClient GetCashuHttpClient(string mintUrl)
    {
        // Add trailing / so mint like https://mint.minibits.cash/Bitcoin works correctly
        var mintUri = new Uri(mintUrl + "/");
        var client = new HttpClient { BaseAddress = mintUri };
        // Some operations, like Melt can take a long time. But 5 minutes should be more than ok.
        client.Timeout = TimeSpan.FromMinutes(5);
        return new CashuHttpClient(client);
    }

    /// <summary>
    /// Get active keyset for current unit
    /// </summary>
    public async Task<GetKeysetsResponse.KeysetItemResponse> GetActiveKeyset()
    {
        if (_keysets == null)
        {
            await GetKeysets();
        }
        if (_keysets == null)
        {
            throw new InvalidOperationException("Couldn't get keysets!");
        }
        var filteredKeysets = _keysets.Where(
            keyset => keyset.Active && keyset.Id.ToString().StartsWith("00") && keyset.Unit == _unit);
        var activeKeyset = filteredKeysets.OrderBy(keyset => keyset.InputFee).FirstOrDefault();
        if (activeKeyset == null)
        {
            throw new InvalidOperationException("Could not find active keyset for this unit!");
        }
        return activeKeyset;
    }

    /// <summary>
    /// Get all keysets from mint
    /// </summary>
    public async Task<List<GetKeysetsResponse.KeysetItemResponse>> GetKeysets()
    {
        var getKeysetResponse = await _cashuHttpClient.GetKeysets();
        var keysets = getKeysetResponse.Keysets.Where(k => k.Id.ToString().Length == 16).ToList();
        _keysets = keysets;
        return _keysets;
    }

    /// <summary>
    /// Get keys for a keyset
    /// </summary>
    public async Task<Keyset?> GetKeys(KeysetId? keysetId = null, bool forceRefresh = false)
    {
        if (keysetId == null)
        {
            var localKeyset = await GetActiveKeyset();
            keysetId = new KeysetId(localKeyset.Id.ToString());
        }

        var keys = await _cashuHttpClient.GetKeys(keysetId);
        if (keys.Keysets == null || keys.Keysets.Length == 0)
        {
            throw new InvalidOperationException("Couldn't fetch keys for provided keyset");
        }

        _keys = keys.Keysets.FirstOrDefault()?.Keys;
        return _keys;
    }

    /// <summary>
    /// Swap proofs to receive new proofs (NUT-03)
    /// </summary>
    public async Task<SwapResult> Swap(List<Proof> proofsToSwap, List<ulong> amounts, KeysetId? keysetId = null, Keyset? keys = null)
    {
        keysetId ??= (await GetActiveKeyset()).Id;
        keys ??= await GetKeys(keysetId);

        if (keys == null)
        {
            throw new InvalidOperationException("Could not get keys for keyset");
        }

        var outputs = CreateOutputs(amounts, keysetId, keys);

        var request = new PostSwapRequest()
        {
            Inputs = proofsToSwap.ToArray(),
            Outputs = outputs.BlindedMessages
        };

        try
        {
            var response = await _cashuHttpClient.Swap(request);
            return new SwapResult
            {
                ProvidedOutputs = outputs,
                ResultProofs = CreateProofs(response.Signatures, outputs.BlindingFactors, outputs.Secrets, keys),
            };
        }
        catch (Exception e)
        {
            return new SwapResult
            {
                ProvidedOutputs = outputs,
                Error = e,
            };
        }
    }

    /// <summary>
    /// Create mint quote from Lightning (NUT-04)
    /// </summary>
    public async Task<PostMintQuoteBolt11Response> CreateMintQuote(ulong amount, string unit = "sat")
    {
        if (_lightningClient == null)
        {
            throw new InvalidOperationException("Lightning client is not configured");
        }

        var request = new PostMintQuoteBolt11Request { Amount = amount, Unit = unit };
        return await _cashuHttpClient.CreateMintQuote<PostMintQuoteBolt11Response, PostMintQuoteBolt11Request>(
            "bolt11", request);
    }

    /// <summary>
    /// Check mint quote status
    /// </summary>
    public async Task<PostMintQuoteBolt11Response> CheckMintQuote(string quoteId, CancellationToken cancellationToken = default)
    {
        return await _cashuHttpClient.CheckMintQuote<PostMintQuoteBolt11Response>("bolt11", quoteId, cancellationToken);
    }

    /// <summary>
    /// Create outputs for swap/mint operations
    /// </summary>
    private OutputData CreateOutputs(List<ulong> amounts, KeysetId keysetId, Keyset keys)
    {
        var blindedMessages = new List<BlindedMessage>();
        var secrets = new List<ISecret>();
        var blindingFactors = new List<PrivKey>();

        if (amounts.Any(a => !keys.Keys.Contains(a)))
        {
            throw new ArgumentException("Invalid amounts");
        }

        foreach (var amount in amounts)
        {
            // Create secret
            var secretBytes = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(secretBytes);
            var secret = new StringSecret(Convert.ToHexString(secretBytes));
            secrets.Add(secret);

            // Create blinding factor
            var rBytes = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(rBytes);
            var r = new PrivKey(Convert.ToHexString(rBytes));
            blindingFactors.Add(r);

            // Create blinded message
            var B_ = DotNut.Cashu.ComputeB_(secret.ToCurve(), r);
            blindedMessages.Add(new BlindedMessage() { Amount = amount, B_ = B_, Id = keysetId });
        }

        return new OutputData()
        {
            BlindingFactors = blindingFactors.ToArray(),
            BlindedMessages = blindedMessages.ToArray(),
            Secrets = secrets.ToArray()
        };
    }

    /// <summary>
    /// Create proofs from blind signatures
    /// </summary>
    private Proof[] CreateProofs(BlindSignature[] promises, PrivKey[] rs, ISecret[] secrets, Keyset keyset)
    {
        var keysetId = promises.Select(p => p.Id).Distinct().ToList();
        if (keysetId.Count != 1)
        {
            throw new InvalidOperationException("Error while creating proofs. All promises should be the same keyset!");
        }

        if (!keyset.GetKeysetId().Equals(keysetId.Single()))
        {
            throw new InvalidOperationException(
                "Error while creating proofs. Id derived from keyset different from promises!");
        }

        var proofs = new List<Proof>();
        for (int i = 0; i < promises.Length; i++)
        {
            var p = promises[i];
            var r = rs[i];
            var secret = secrets[i];

            var A = keyset[Convert.ToUInt64(p.Amount)];

            // Unblind signature
            var C = DotNut.Cashu.ComputeC(p.C_, r, A);

            proofs.Add(new Proof
            {
                Id = p.Id,
                Amount = p.Amount,
                Secret = secret,
                C = C,
                DLEQ = p.DLEQ,
            });
        }

        return proofs.ToArray();
    }

    /// <summary>
    /// Split amount to proof amounts based on keyset
    /// </summary>
    public static List<ulong> SplitToProofsAmounts(ulong paymentAmount, Keyset keyset)
    {
        var outputAmounts = new List<ulong>();
        var possibleValues = keyset.Keys.OrderByDescending(x => x).ToList();
        foreach (var value in possibleValues)
        {
            while (paymentAmount >= value)
            {
                outputAmounts.Add(value);
                paymentAmount -= value;
            }

            if (paymentAmount == 0)
            {
                break;
            }
        }

        return outputAmounts;
    }
}

public class SwapResult
{
    public bool Success => Error == null && ResultProofs != null;
    public required OutputData ProvidedOutputs { get; set; }
    public Proof[]? ResultProofs { get; set; }
    public Exception? Error { get; set; }
}

public class OutputData
{
    public BlindedMessage[] BlindedMessages { get; set; } = Array.Empty<BlindedMessage>();
    public ISecret[] Secrets { get; set; } = Array.Empty<ISecret>();
    public PrivKey[] BlindingFactors { get; set; } = Array.Empty<PrivKey>();
}

