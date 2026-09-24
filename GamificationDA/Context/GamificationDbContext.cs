using GamificationDA.Entities;
using Microsoft.EntityFrameworkCore;

namespace GamificationDA.Context;

public class GamificationDbContext : DbContext
{
    public GamificationDbContext(DbContextOptions<GamificationDbContext> options) : base(options)
    {
    }

    public virtual DbSet<LearnerWallet> LearnerWallets { get; set; } = null!;

    public virtual DbSet<SparkTransaction> SparkTransactions { get; set; } = null!;

    public virtual DbSet<ShopItem> ShopItems { get; set; } = null!;

    public virtual DbSet<LearnerItem> LearnerItems { get; set; } = null!;

    public virtual DbSet<LearnerBoost> LearnerBoosts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GamificationDbContext).Assembly);
}
