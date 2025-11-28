using System;
using System.Net.Http;
using DotNut;
using DotNut.Api;

namespace BTCPayServer.Plugins.BitcoinRewards.CashuAbstractions;

/// <summary>
/// CashuUtils helper class matching Cashu plugin structure.
/// Contains OutputData class used by FailedTransaction and helper methods.
/// </summary>
public static class CashuUtils
{
    /// <summary>
    /// Factory for cashu client - creates new httpclient for given mint (matching Cashu plugin)
    /// </summary>
    public static CashuHttpClient GetCashuHttpClient(string mintUrl)
    {
        //add trailing / so mint like https://mint.minibits.cash/Bitcoin will work correctly
        var mintUri = new Uri(mintUrl + "/");
        var client = new HttpClient { BaseAddress = mintUri };
        //Some operations, like Melt can take a long time. But 5 minutes should be more than ok.
        client.Timeout = TimeSpan.FromMinutes(5);
        var cashuClient = new CashuHttpClient(client);
        return cashuClient;
    }

    /// <summary>
    /// OutputData class for tracking swap/melt operation outputs (matching Cashu plugin).
    /// </summary>
    public class OutputData
    {
        public BlindedMessage[] BlindedMessages { get; set; } = Array.Empty<BlindedMessage>();
        public DotNut.ISecret[] Secrets { get; set; } = Array.Empty<DotNut.ISecret>();
        public PrivKey[] BlindingFactors { get; set; } = Array.Empty<PrivKey>();
    }
}

