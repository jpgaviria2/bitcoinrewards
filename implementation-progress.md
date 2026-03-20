# Bolt Card Rewards Integration - Implementation Progress

## Status: ✅ Complete

### Phase 1: Code Reading ✅
- Read all existing bitcoinrewards plugin files
- Read boltcards-plugin (BoltcardBalance, TopupRequestHostedService)
- Read core BTCPay boltcard code (BoltcardDataExtensions, UIBoltcardController)
- Understood IssuerKey/NTag424 decryption and CMAC verification flow

### Phase 2: Implementation ✅
- [x] Database: BoltCardLink entity + migration (20260211000000_AddBoltCardLinks)
- [x] Service: BoltCardRewardService (decrypt, verify CMAC, top-up, balance, card listing)
- [x] Settings: BoltCardEnabled, BoltcardFactoryAppId, DefaultCardBalanceSats
- [x] Controller: BoltCardRewardsController (tap, cards, balance endpoints)
- [x] Display page: NFC tap UI with Web NFC API JavaScript
- [x] ViewModel updates for bolt card fields
- [x] Plugin DI registration
- [x] Unit tests: 20 tests in BoltCardRewardTests.cs
- [x] Documentation: README updated with setup/usage guide
- [x] Implementation summary written
