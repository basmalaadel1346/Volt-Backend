using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using UsersDA.Entities;

namespace UsersDA.Context;

public partial class UsersDbContext : DbContext
{
    public UsersDbContext()
    {
    }

    public UsersDbContext(DbContextOptions<UsersDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ParentChildLink> ParentChildLinks { get; set; }

    public virtual DbSet<PasswordResetOtp> PasswordResetOtps { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // ملحوظة: الكونكشن سترينج الحقيقي بييجي من appsettings.json عن طريق الـ DI
        // (شوف UsersModule.AddUsersModule). السطر ده بيفضل بس كـ Fallback
        // لو حد شغّل الـ DbContext مباشرة من غير DI (زي أدوات الـ Migration weird cases).
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=.;Database=Volt;Trusted_Connection=True;TrustServerCertificate=True;");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ParentChildLink>(entity =>
        {
            entity.ToTable("ParentChildLinks", "Users");

            entity.HasIndex(e => e.ChildUserId, "IX_ParentChildLinks_ChildUserId");

            entity.HasIndex(e => e.ParentUserId, "IX_ParentChildLinks_ParentUserId");

            entity.HasIndex(e => new { e.ParentUserId, e.ChildUserId }, "UQ_ParentChildLinks").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.ChildUser).WithMany(p => p.ParentChildLinkChildUsers)
                .HasForeignKey(d => d.ChildUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ParentChildLinks_Child");

            entity.HasOne(d => d.ParentUser).WithMany(p => p.ParentChildLinkParentUsers)
                .HasForeignKey(d => d.ParentUserId)
                .HasConstraintName("FK_ParentChildLinks_Parent");
        });

        modelBuilder.Entity<PasswordResetOtp>(entity =>
        {
            entity.ToTable("PasswordResetOTPs", "Users");

            entity.HasIndex(e => e.UserId, "IX_PasswordResetOTPs_UserId");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Otphash)
                .HasMaxLength(500)
                .HasColumnName("OTPHash");
            entity.Property(e => e.ResetTokenHash).HasMaxLength(500);
            entity.Property(e => e.ResetTokenExpiresAt);

            entity.HasOne(d => d.User).WithMany(p => p.PasswordResetOtps)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_PasswordResetOTPs_Users");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens", "Users");

            entity.HasIndex(e => e.UserId, "IX_RefreshTokens_UserId");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.TokenHash).HasMaxLength(500);

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_RefreshTokens_Users");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users", "Users");

            entity.HasIndex(e => e.Email, "UQ_Users_Email")
                .IsUnique()
                .HasFilter("([Email] IS NOT NULL)");

            entity.HasIndex(e => new { e.AuthProvider, e.ProviderUserId }, "UQ_Users_Provider")
                .IsUnique()
                .HasFilter("([ProviderUserId] IS NOT NULL)");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.AuthProvider).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PasswordHash).HasMaxLength(500);
            entity.Property(e => e.ProviderUserId).HasMaxLength(255);
            entity.Property(e => e.Role).HasMaxLength(20);
            entity.Property(e => e.Age);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
