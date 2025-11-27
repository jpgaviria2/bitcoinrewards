# Bitcoin Rewards Plugin - Roadmap

## Future Enhancements

### NUT-13 Seed Phrase Support (Future)

**Status:** Planned - Not Implemented  
**Priority:** Medium  
**Standard:** Cashu NUT-13 (BIP39/BIP32)

#### Overview
Implement seed phrase-based wallet initialization and recovery following the Cashu NUT-13 standard. This will enable users to recover their wallet balance using a 12-word mnemonic seed phrase if they lose access to their database.

#### Current State
- ✅ Wallet uses random secret generation (matches Cashu plugin approach)
- ✅ Proofs stored in database
- ❌ No seed phrase generation
- ❌ No deterministic secret derivation
- ❌ No wallet recovery mechanism

#### Required Implementation

1. **Seed Phrase Generation (BIP39)**
   - Generate 12-word mnemonic on wallet initialization
   - Store seed phrase securely (encrypted in database or user-provided)
   - Add UI for seed phrase display/backup

2. **Deterministic Secret Derivation (BIP32)**
   - Derive secrets from seed using BIP32 paths
   - Each proof uses different derivation path (e.g., `m/0/0`, `m/0/1`, etc.)
   - Track derivation paths for each proof in database

3. **Wallet Recovery**
   - Allow wallet restoration from seed phrase
   - Request mint to reissue proofs using NUT-11 (Restore)
   - Recover wallet balance from seed phrase alone

4. **Database Schema Updates**
   - Add seed phrase storage (encrypted)
   - Add derivation path tracking for proofs
   - Migration for existing wallets

#### Notes
- The current BTCPay Cashu plugin does NOT implement NUT-13 seed phrases
- This is an optional enhancement for improved wallet recovery
- Client-side Cashu wallets (e.g., macadamia) do support seed phrases
- Implementation would make the wallet more user-friendly and secure

#### References
- [Cashu NUT-13 Specification](https://cashubtc.github.io/nuts/13/)
- BIP39: Mnemonic code for generating deterministic keys
- BIP32: Hierarchical Deterministic Wallets

---

## Completed Features

- ✅ Independent Cashu wallet implementation
- ✅ Proof storage in database
- ✅ Lightning-to-ecash top-up
- ✅ Wallet configuration UI
- ✅ Error handling improvements
- ✅ EF Core entity mapping fixes

