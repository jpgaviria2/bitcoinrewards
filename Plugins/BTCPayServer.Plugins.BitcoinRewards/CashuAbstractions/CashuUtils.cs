using DotNut;

namespace BTCPayServer.Plugins.BitcoinRewards.CashuAbstractions;

/// <summary>
/// CashuUtils helper class matching Cashu plugin structure.
/// Contains OutputData class used by FailedTransaction.
/// </summary>
public static class CashuUtils
{
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

