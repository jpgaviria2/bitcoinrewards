#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DotNut;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Models;

/// <summary>
/// Stored proof for Bitcoin Rewards plugin's independent Cashu wallet.
/// Similar to Cashu plugin's StoredProof but in our own schema.
/// </summary>
public class StoredProof : DotNut.Proof
{
    public Guid ProofId { get; set; }
    public string StoreId { get; set; } = string.Empty;
    public string MintUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Entity Framework requires empty constructor
    private StoredProof() { }

    public StoredProof(Proof proof, string storeId, string mintUrl)
    {
        this.Id = proof.Id;
        this.Amount = proof.Amount;
        this.Secret = proof.Secret;
        this.C = proof.C;
        this.DLEQ = proof.DLEQ;
        this.Witness = proof.Witness;
        this.StoreId = storeId;
        this.MintUrl = mintUrl;
    }

    public Proof ToDotNutProof()
    {
        return new Proof
        {
            Id = this.Id,
            Amount = this.Amount,
            Secret = this.Secret,
            C = this.C,
            DLEQ = this.DLEQ,
            Witness = this.Witness
        };
    }

    public static IEnumerable<StoredProof> FromBatch(IEnumerable<Proof> proofs, string storeId, string mintUrl)
    {
        return proofs.Select(p => new StoredProof(p, storeId, mintUrl));
    }
}

