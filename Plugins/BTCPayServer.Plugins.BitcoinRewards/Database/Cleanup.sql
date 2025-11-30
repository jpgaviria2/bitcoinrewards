-- SQL Script to Clean Up Bitcoin Rewards Plugin Database Schema
-- Run this script AFTER uninstalling the plugin to remove all plugin data

-- WARNING: This will permanently delete all Bitcoin Rewards data!
-- Make sure you have a backup if you need to preserve any data.

-- Drop the plugin schema and all its objects
DROP SCHEMA IF EXISTS "BTCPayServer.Plugins.BitcoinRewards" CASCADE;

-- Also drop the migrations history table if it exists in a different location
-- (Check your __EFMigrationsHistory table location first)
-- DROP TABLE IF EXISTS "__EFMigrationsHistory"."BTCPayServer.Plugins.BitcoinRewards" CASCADE;

-- Verify cleanup (should return 0 rows)
SELECT schema_name 
FROM information_schema.schemata 
WHERE schema_name = 'BTCPayServer.Plugins.BitcoinRewards';

