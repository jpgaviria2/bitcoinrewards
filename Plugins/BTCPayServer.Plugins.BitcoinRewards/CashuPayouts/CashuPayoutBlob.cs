using BTCPayServer.Data;

namespace BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;

public class CashuPayoutBlob : IPayoutProof
{
    public const string CashuPayoutBlobProofType = "CashuPayoutBlob";
    public string ProofType { get; } = CashuPayoutBlobProofType;
    
    public string Token { get; set; } = string.Empty;
    public string Mint { get; set; } = string.Empty;
    public ulong Amount { get; set; }
    public string Id => Token; // Use token as ID (first part of token string)
    public string Link => null; // No link for Cashu tokens
}

