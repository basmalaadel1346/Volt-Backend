using GamificationDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamificationDA.Configurations;

public class LearnerWalletConfiguration : IEntityTypeConfiguration<LearnerWallet>
{
    public void Configure(EntityTypeBuilder<LearnerWallet> entity)
    {
        entity.ToTable("LearnerWallets", "Gamification", tb =>
        {
            // A balance can reach zero but never go below it, whatever a bug in
            // the purchase path does.
            tb.HasCheckConstraint("CK_LearnerWallets_SparksBalance", "[SparksBalance] >= 0");
            tb.HasCheckConstraint("CK_LearnerWallets_LifetimeSparks", "[LifetimeSparks] >= 0");
            tb.HasCheckConstraint("CK_LearnerWallets_CurrentStreak", "[CurrentStreakDays] >= 0");
            tb.HasCheckConstraint("CK_LearnerWallets_LongestStreak",
                "[LongestStreakDays] >= [CurrentStreakDays]");
        });

        entity.HasKey(e => e.UserId);

        // Cross-module, no FK to Users.Users — the same convention Assessment
        // uses for QuizAttempts.UserId.
        entity.Property(e => e.UserId).ValueGeneratedNever();
        entity.Property(e => e.LastActivityOn).HasColumnType("date");
        entity.Property(e => e.CreatedAt).HasPrecision(3).HasDefaultValueSql("(sysutcdatetime())");
        entity.Property(e => e.UpdatedAt).HasPrecision(3).HasDefaultValueSql("(sysutcdatetime())");
        entity.Property(e => e.RowVersion).IsRowVersion();
    }
}

public class SparkTransactionConfiguration : IEntityTypeConfiguration<SparkTransaction>
{
    public void Configure(EntityTypeBuilder<SparkTransaction> entity)
    {
        entity.ToTable("SparkTransactions", "Gamification", tb =>
        {
            tb.HasCheckConstraint("CK_SparkTransactions_AmountNotZero", "[Amount] <> 0");
            tb.HasCheckConstraint("CK_SparkTransactions_BalanceAfter", "[BalanceAfter] >= 0");
        });

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Reason).HasMaxLength(40);
        entity.Property(e => e.ReferenceKey).HasMaxLength(100);
        entity.Property(e => e.CreatedAt).HasPrecision(3).HasDefaultValueSql("(sysutcdatetime())");

        // THE idempotency rule. A replayed submit, a retried request or two
        // instances racing all try to write the same (learner, reason, reference)
        // row; exactly one succeeds and the rest are recognised as duplicates.
        // Filtered, because a movement without a reference (an admin adjustment)
        // has no identity to be unique on.
        entity.HasIndex(e => new { e.UserId, e.Reason, e.ReferenceKey },
                "UQ_SparkTransactions_UserId_Reason_ReferenceKey")
            .IsUnique()
            .HasFilter("([ReferenceKey] IS NOT NULL)");

        entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_SparkTransactions_UserId_CreatedAt");

        entity.HasOne(e => e.Wallet).WithMany(w => w.SparkTransactions)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_SparkTransactions_LearnerWallets");
    }
}

public class ShopItemConfiguration : IEntityTypeConfiguration<ShopItem>
{
    public void Configure(EntityTypeBuilder<ShopItem> entity)
    {
        entity.ToTable("ShopItems", "Gamification", tb =>
        {
            tb.HasCheckConstraint("CK_ShopItems_Kind", "[Kind] IN ('StreakFreeze', 'Avatar', 'Boost')");
            tb.HasCheckConstraint("CK_ShopItems_Price", "[PriceSparks] >= 0");

            // A boost that multiplies by nothing, or runs for no time, is not a
            // boost — and a non-boost carrying either is a mis-seeded row.
            tb.HasCheckConstraint("CK_ShopItems_BoostIsComplete",
                "([Kind] = 'Boost' AND [BoostMultiplier] > 1 AND [BoostMinutes] > 0) "
              + "OR ([Kind] <> 'Boost' AND [BoostMultiplier] IS NULL AND [BoostMinutes] IS NULL)");
        });

        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.Code, "UQ_ShopItems_Code").IsUnique();
        entity.Property(e => e.Code).HasMaxLength(40);
        entity.Property(e => e.Kind).HasMaxLength(20);
        entity.Property(e => e.NameAr).HasMaxLength(100);
        entity.Property(e => e.NameEn).HasMaxLength(100);
        entity.Property(e => e.DescriptionAr).HasMaxLength(500);
        entity.Property(e => e.DescriptionEn).HasMaxLength(500);
        entity.Property(e => e.ImageUrl).HasMaxLength(500);
        entity.Property(e => e.IsActive).HasDefaultValue(true);
    }
}

public class LearnerItemConfiguration : IEntityTypeConfiguration<LearnerItem>
{
    public void Configure(EntityTypeBuilder<LearnerItem> entity)
    {
        entity.ToTable("LearnerItems", "Gamification", tb =>
            tb.HasCheckConstraint("CK_LearnerItems_Quantity", "[Quantity] > 0"));

        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.UserId, e.ShopItemId }, "UQ_LearnerItems_UserId_ShopItemId").IsUnique();
        entity.Property(e => e.AcquiredAt).HasPrecision(3).HasDefaultValueSql("(sysutcdatetime())");
        entity.Property(e => e.UpdatedAt).HasPrecision(3).HasDefaultValueSql("(sysutcdatetime())");

        entity.HasOne(e => e.Wallet).WithMany(w => w.Items)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_LearnerItems_LearnerWallets");

        entity.HasOne(e => e.ShopItem).WithMany(s => s.LearnerItems)
            .HasForeignKey(e => e.ShopItemId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_LearnerItems_ShopItems");
    }
}

public class LearnerBoostConfiguration : IEntityTypeConfiguration<LearnerBoost>
{
    public void Configure(EntityTypeBuilder<LearnerBoost> entity)
    {
        entity.ToTable("LearnerBoosts", "Gamification", tb =>
        {
            tb.HasCheckConstraint("CK_LearnerBoosts_Multiplier", "[Multiplier] > 1");
            tb.HasCheckConstraint("CK_LearnerBoosts_Window", "[ExpiresAt] > [StartedAt]");
        });

        entity.HasKey(e => e.Id);
        entity.Property(e => e.StartedAt).HasPrecision(3);
        entity.Property(e => e.ExpiresAt).HasPrecision(3);

        // Serves "is a boost running right now?", the only question asked of this
        // table on a hot path.
        entity.HasIndex(e => new { e.UserId, e.ExpiresAt }, "IX_LearnerBoosts_UserId_ExpiresAt");

        entity.HasOne(e => e.Wallet).WithMany(w => w.Boosts)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_LearnerBoosts_LearnerWallets");

        entity.HasOne(e => e.ShopItem).WithMany()
            .HasForeignKey(e => e.ShopItemId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_LearnerBoosts_ShopItems");
    }
}
