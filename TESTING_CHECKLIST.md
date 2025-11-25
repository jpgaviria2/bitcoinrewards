# Bitcoin Rewards Plugin - Testing Checklist

## ✅ Ready to Test (UI/Configuration)

### 1. Settings Page Functionality
- [ ] **Enable/Disable Plugin**
  - Toggle the "Enable Bitcoin Rewards" switch
  - Verify settings are saved and persist after page refresh
  
- [ ] **Reward Percentage**
  - Set percentage between 0-100
  - Test validation (should reject negative or >100)
  - Verify saved value
  
- [ ] **Delivery Method**
  - Switch between Email and SMS
  - Verify selection is saved
  
- [ ] **Platform Integration**
  - Enable/disable Shopify
  - Enable/disable Square
  - Test validation (at least one must be enabled when plugin is enabled)
  
- [ ] **Platform Credentials**
  - Enter Shopify credentials (Shop URL, Access Token)
  - Enter Square credentials (Application ID, Access Token, Location ID, Environment)
  - Verify credentials are saved (check that password fields are masked in UI)
  
- [ ] **Advanced Settings**
  - Set minimum transaction amount
  - Set maximum reward in satoshis
  - Enter custom email template
  - Verify all are saved
  
- [ ] **Reset Functionality**
  - Click "Reset" button
  - Verify all settings return to defaults
  - Verify plugin is disabled after reset

### 2. Rewards History Page
- [ ] Navigate to Rewards History page
- [ ] Verify page loads (should show empty list if no rewards yet)
- [ ] Test pagination (once rewards exist)
- [ ] Test filtering by status, platform, date range

### 3. Navigation
- [ ] "Bitcoin Rewards" appears in navigation menu when on a store
- [ ] Clicking navigation item goes to settings page
- [ ] Navigation item is properly styled/highlighted when active

## 🔨 Still Needs Implementation/Testing

### 4. Database
- [ ] **Table Creation**
  - Check if `BitcoinRewardRecords` table exists in database
  - Verify table schema matches `BitcoinRewardRecord` model
  - Check indexes are created (StoreId, Status, composite index)
  
- [ ] **Database Operations**
  - Test adding a reward record
  - Test updating a reward record
  - Test querying reward records by store

### 5. Shopify Integration
- [ ] **Webhook Endpoint**
  - Need to implement Shopify webhook controller
  - Register webhook endpoint: `/plugins/bitcoin-rewards/{storeId}/webhooks/shopify`
  - Test webhook receives orders/transactions from Shopify
  
- [ ] **Webhook Processing**
  - Verify webhook signature validation
  - Process `orders/create` or `orders/paid` webhooks
  - Extract transaction data from Shopify order
  - Convert to `TransactionData` model

### 6. Square Integration
- [ ] **Webhook Endpoint**
  - Test existing Square webhook controller: `/plugins/bitcoin-rewards/{storeId}/webhooks/square`
  - Verify webhook receives payment events from Square
  
- [ ] **Webhook Processing**
  - Implement webhook signature verification
  - Process `payment.created` and `payment.updated` events
  - Extract transaction data from Square payment
  - Convert to `TransactionData` model

### 7. Reward Processing Logic
- [ ] **Transaction Processing**
  - Test `BitcoinRewardsService.ProcessRewardAsync()`
  - Verify reward calculation (percentage × transaction amount)
  - Test minimum transaction amount check
  - Test maximum reward cap
  - Verify duplicate transaction prevention
  
- [ ] **Currency Conversion**
  - Test conversion from store currency to BTC/sats
  - Verify rate fetching from BTCPay Server rate service
  - Handle edge cases (missing rates, invalid currencies)

### 8. Cashu Integration
- [ ] **Cashu Service**
  - Current implementation is a stub (`CashuServiceAdapter`)
  - Need to integrate with actual Cashu plugin
  - Implement `GenerateEcashToken()` to create real tokens
  - Implement `ReclaimEcashToken()` to reclaim unclaimed tokens
  
- [ ] **Token Generation**
  - Test generating ecash token for reward amount
  - Verify token format is correct
  - Test with different reward amounts

### 9. Email Notifications
- [ ] **Email Service**
  - Verify Email plugin is installed (if using email delivery)
  - Test reflection-based email service integration
  - Send test reward email
  
- [ ] **Email Content**
  - Verify email template is used (if custom template provided)
  - Check email contains: reward amount, ecash token, order ID
  - Verify email formatting

### 10. SMS Notifications (Future)
- [ ] SMS delivery method is not yet implemented
- [ ] Will need SMS provider integration (Twilio, AWS SNS, etc.)

### 11. Reclaim Functionality
- [ ] **Reclaim Endpoint**
  - Test reclaim endpoint: `POST /plugins/bitcoin-rewards/{storeId}/history/reclaim/{rewardId}`
  - Verify only unclaimed/expired rewards can be reclaimed
  - Test Cashu token reclamation

### 12. Error Handling
- [ ] **Database Errors**
  - Test behavior when database table doesn't exist
  - Test with database connection issues
  
- [ ] **API Errors**
  - Test Shopify API errors
  - Test Square API errors
  - Test rate fetching errors
  - Test Cashu service errors

## 🧪 Recommended Testing Order

1. **Start with UI/Configuration** (Quick wins)
   - Test settings page thoroughly
   - Verify data persistence
   
2. **Database Setup**
   - Verify table creation
   - Test basic CRUD operations
   
3. **Basic Reward Processing** (Manual test)
   - Create a test reward manually in database
   - Test reward calculation logic
   - Test reward record creation
   
4. **Cashu Integration** (Critical)
   - Integrate with Cashu plugin
   - Test token generation
   
5. **Webhook Integration** (Platform-specific)
   - Start with Square (simpler)
   - Then Shopify
   - Test end-to-end flow
   
6. **Email Integration**
   - Test email sending
   - Verify email content

## 🐛 Known Issues / TODOs

- [ ] Shopify webhook controller not yet created (need to add)
- [ ] Cashu service is stubbed (needs real implementation)
- [ ] Square webhook signature verification not implemented
- [ ] SMS delivery not implemented
- [ ] Database migrations may need to be created manually
- [ ] Email template variables need documentation

## 📝 Test Scenarios

### Scenario 1: Basic Reward Flow (Happy Path)
1. Enable plugin for a store
2. Set reward percentage to 5%
3. Enable Shopify integration
4. Receive Shopify order webhook
5. Verify reward is calculated (5% of order amount)
6. Verify ecash token is generated
7. Verify email is sent to customer
8. Verify reward record is created in database
9. Verify reward appears in Rewards History

### Scenario 2: Edge Cases
1. Transaction below minimum amount (should not create reward)
2. Reward exceeds maximum cap (should cap at maximum)
3. Duplicate transaction (should not create duplicate reward)
4. Missing customer email (should handle gracefully)
5. Cashu service unavailable (should log error, not crash)

### Scenario 3: Configuration Changes
1. Disable plugin (should not process new rewards)
2. Change reward percentage (existing rewards unaffected)
3. Change delivery method (existing rewards unaffected)
4. Disable platform (should not process rewards from that platform)

