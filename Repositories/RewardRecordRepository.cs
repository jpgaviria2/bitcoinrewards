using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BTCPayServer.Plugins.BitcoinRewards.Repositories
{
    public class RewardRecordRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public RewardRecordRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<RewardRecord> CreateAsync(RewardRecord record)
        {
            var entity = new RewardRecordEntity
            {
                Id = record.Id,
                OrderId = record.OrderId,
                StoreId = record.StoreId,
                CustomerEmail = record.CustomerEmail,
                CustomerPhone = record.CustomerPhone,
                RewardAmount = record.RewardAmount,
                BitcoinAddress = record.BitcoinAddress,
                TransactionId = record.TransactionId,
                Status = record.Status.ToString(),
                CreatedAt = record.CreatedAt,
                SentAt = record.SentAt,
                Source = record.Source
            };

            _dbContext.Set<RewardRecordEntity>().Add(entity);
            await _dbContext.SaveChangesAsync();
            return record;
        }

        public async Task<RewardRecord> UpdateAsync(RewardRecord record)
        {
            var entity = await _dbContext.Set<RewardRecordEntity>()
                .FirstOrDefaultAsync(r => r.Id == record.Id);

            if (entity != null)
            {
                entity.OrderId = record.OrderId;
                entity.StoreId = record.StoreId;
                entity.CustomerEmail = record.CustomerEmail;
                entity.CustomerPhone = record.CustomerPhone;
                entity.RewardAmount = record.RewardAmount;
                entity.BitcoinAddress = record.BitcoinAddress;
                entity.TransactionId = record.TransactionId;
                entity.Status = record.Status.ToString();
                entity.CreatedAt = record.CreatedAt;
                entity.SentAt = record.SentAt;
                entity.Source = record.Source;

                await _dbContext.SaveChangesAsync();
            }

            return record;
        }

        public async Task<RewardRecord?> GetByIdAsync(string id)
        {
            var entity = await _dbContext.Set<RewardRecordEntity>()
                .FirstOrDefaultAsync(r => r.Id == id);

            return entity != null ? MapToModel(entity) : null;
        }

        public async Task<List<RewardRecord>> GetByStoreIdAsync(string storeId)
        {
            var entities = await _dbContext.Set<RewardRecordEntity>()
                .Where(r => r.StoreId == storeId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return entities.Select(MapToModel).ToList();
        }

        public async Task<RewardRecord?> GetByCustomerEmailAsync(string email, string storeId)
        {
            var entity = await _dbContext.Set<RewardRecordEntity>()
                .Where(r => r.StoreId == storeId && r.CustomerEmail == email)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            return entity != null ? MapToModel(entity) : null;
        }

        private RewardRecord MapToModel(RewardRecordEntity entity)
        {
            return new RewardRecord
            {
                Id = entity.Id,
                OrderId = entity.OrderId,
                StoreId = entity.StoreId,
                CustomerEmail = entity.CustomerEmail,
                CustomerPhone = entity.CustomerPhone,
                RewardAmount = entity.RewardAmount,
                BitcoinAddress = entity.BitcoinAddress,
                TransactionId = entity.TransactionId,
                Status = Enum.TryParse<RewardStatus>(entity.Status, out var status) ? status : RewardStatus.Pending,
                CreatedAt = entity.CreatedAt,
                SentAt = entity.SentAt,
                Source = entity.Source
            };
        }
    }

    public class RewardRecordEntity
    {
        public string Id { get; set; } = null!;
        public string OrderId { get; set; } = null!;
        public string StoreId { get; set; } = null!;
        public string CustomerEmail { get; set; } = null!;
        public string CustomerPhone { get; set; } = null!;
        public decimal RewardAmount { get; set; }
        public string BitcoinAddress { get; set; } = null!;
        public string TransactionId { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string Source { get; set; } = null!;
    }

    public class RewardRecordEntityConfiguration : IEntityTypeConfiguration<RewardRecordEntity>
    {
        public void Configure(EntityTypeBuilder<RewardRecordEntity> builder)
        {
            builder.ToTable("BitcoinRewardRecords");
            builder.HasKey(x => x.Id);
            builder.HasIndex(x => x.StoreId);
            builder.HasIndex(x => new { x.StoreId, x.CustomerEmail });
            builder.Property(x => x.Id).HasMaxLength(50);
            builder.Property(x => x.OrderId).HasMaxLength(100);
            builder.Property(x => x.StoreId).HasMaxLength(50);
            builder.Property(x => x.CustomerEmail).HasMaxLength(255);
            builder.Property(x => x.CustomerPhone).HasMaxLength(50);
            builder.Property(x => x.BitcoinAddress).HasMaxLength(100);
            builder.Property(x => x.TransactionId).HasMaxLength(100);
            builder.Property(x => x.Status).HasMaxLength(20);
            builder.Property(x => x.Source).HasMaxLength(20);
        }
    }
}

