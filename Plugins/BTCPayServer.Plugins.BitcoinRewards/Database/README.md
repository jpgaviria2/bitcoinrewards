# Bitcoin Rewards Plugin Database

## Database Separation

✅ **This plugin uses a completely separate database schema** from the main BTCPay Server database.

- **Plugin Schema**: `BTCPayServer.Plugins.BitcoinRewards`
- **Main BTCPay Schema**: `public` (default)

All plugin data (including `BitcoinRewardRecords` table) is stored in the separate schema, ensuring:
- No conflicts with BTCPay Server core tables
- Easy cleanup when uninstalling the plugin
- Complete isolation from main database

## Schema Location

The plugin creates its own PostgreSQL schema:
- Schema Name: `BTCPayServer.Plugins.BitcoinRewards`
- Tables: `BitcoinRewardRecords`
- Migrations History: Stored in the plugin schema

## Uninstalling the Plugin

### Automatic Cleanup (Not Available)

BTCPay Server does not automatically clean up plugin database schemas when a plugin is uninstalled. The plugin file is simply deleted, but the database schema remains.

### Manual Cleanup

To completely remove all plugin data:

1. **Uninstall the plugin** from BTCPay Server UI (Server Settings → Plugins)

2. **Run the cleanup SQL script**:
   ```bash
   psql -U btcpay -d btcpay -f Cleanup.sql
   ```
   
   Or connect to PostgreSQL and run:
   ```sql
   DROP SCHEMA IF EXISTS "BTCPayServer.Plugins.BitcoinRewards" CASCADE;
   ```

3. **Verify cleanup**:
   ```sql
   SELECT schema_name 
   FROM information_schema.schemata 
   WHERE schema_name = 'BTCPayServer.Plugins.BitcoinRewards';
   ```
   Should return 0 rows.

### Docker Cleanup

If running BTCPay Server via Docker:

```bash
docker exec -i generated_btcpayserver_1 psql -U btcpay -d btcpay < Cleanup.sql
```

## Migration Management

The plugin uses Entity Framework Core migrations:

- Migrations are stored in: `Data/Migrations/`
- To create a new migration:
  ```bash
  dotnet ef migrations add MigrationName -p BTCPayServer.Plugins.BitcoinRewards -c BitcoinRewardsPluginDbContext -o Data/Migrations
  ```

- To apply migrations:
  ```bash
  dotnet ef database update -p BTCPayServer.Plugins.BitcoinRewards -c BitcoinRewardsPluginDbContext
  ```

## Data Backup

Before uninstalling, you can backup plugin data:

```sql
-- Backup to CSV
COPY "BTCPayServer.Plugins.BitcoinRewards"."BitcoinRewardRecords" 
TO '/tmp/bitcoinrewards_backup.csv' WITH CSV HEADER;

-- Or backup entire schema
pg_dump -U btcpay -d btcpay -n "BTCPayServer.Plugins.BitcoinRewards" -f bitcoinrewards_backup.sql
```

## Verification

To verify the schema is properly separated:

```sql
-- List all schemas
SELECT schema_name FROM information_schema.schemata 
WHERE schema_name LIKE 'BTCPayServer%';

-- Should show:
-- - BTCPayServer.Plugins.BitcoinRewards (plugin schema)
-- - Any other plugin schemas

-- List plugin tables
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'BTCPayServer.Plugins.BitcoinRewards';

-- Should show:
-- - BitcoinRewardRecords
```

